namespace Kumiko.Rendering;

// HLSL for shader model 4.0 (feature level 10_0 and later, including WARP), after OpenGlShader.cs.
// Direct3D textures and framebuffers are top-down, so no pass flips rows. Derivatives and atlas
// samples are taken before any discard or branch, which keeps them defined for every quad pixel.
internal static class D3D11Shader
{
    internal const string Source = """
        cbuffer Uniforms : register(b0)
        {
            float2 viewport;
            float2 origin;
            float2 size;
            float down;
            float scale;
            float4 area;
            float2 direction;
            int sampleDown;
            int radius;
            float sigma;
        };
        Texture2D source : register(t0);
        SamplerState linearClamp : register(s0);

        struct Input
        {
            float4 rect : INSTANCE0;
            float4 color : INSTANCE1;
            float4 borderColor : INSTANCE2;
            float4 uv : INSTANCE3;
            float4 clipRect : INSTANCE4;
            float4 style : INSTANCE5;
            float4 turn : INSTANCE6;
            uint id : SV_VertexID;
        };

        struct Fragment
        {
            float4 position : SV_Position;
            float2 world : WORLD;
            float2 local : LOCAL;
            float2 texel : TEXEL;
            nointerpolation float4 rect : RECT;
            nointerpolation float4 color : COLOR;
            nointerpolation float4 borderColor : BORDER;
            nointerpolation float4 clipRect : CLIPRECT;
            nointerpolation float4 style : STYLE;
            nointerpolation float4 uv : UV;
        };

        Fragment vertexMain(Input i)
        {
            float2 corner = float2(i.id & 1, i.id >> 1);
            float grow = i.style.z == 2.0 ? i.style.y * 3.0 : 0.0;
            float2 p = i.rect.xy - grow + corner * (i.rect.zw + 2.0 * grow);
            Fragment o;
            // Clipping and shading stay in the upright frame; only the position turns.
            float2 q = p;
            if (i.turn.x != 0.0)
            {
                float s = sin(i.turn.x);
                float c = cos(i.turn.x);
                float2 d = p - i.turn.yz;
                q = i.turn.yz + float2(d.x * c - d.y * s, d.x * s + d.y * c);
            }
            o.position = float4(q / viewport * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
            o.world = p;
            o.local = p - i.rect.xy;
            o.texel = lerp(i.uv.xy, i.uv.zw, corner);
            o.rect = i.rect;
            o.color = i.color;
            o.borderColor = i.borderColor;
            o.clipRect = i.clipRect;
            o.style = i.style;
            o.uv = i.uv;
            return o;
        }

        float rounded(float2 p, float2 h, float r)
        {
            float2 q = abs(p) - h + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }

        float2 erf2(float2 x)
        {
            float2 a = abs(x);
            float2 s = sign(x);
            float2 v = 1.0 + (0.278393 + (0.230389 + 0.078108 * a * a) * a) * a;
            v *= v;
            return s - s / (v * v);
        }

        float shadow(float2 p, float2 h, float r, float deviation)
        {
            float low = p.y - h.y;
            float high = p.y + h.y;
            float start = clamp(-3.0 * deviation, low, high);
            float end = clamp(3.0 * deviation, low, high);
            float stride = (end - start) / 4.0;
            float sum = 0.0;
            [unroll]
            for (int k = 0; k < 4; k++)
            {
                float y = start + stride * (k + 0.5);
                float dy = p.y - y;
                float delta = min(h.y - r - abs(dy), 0.0);
                float curved = h.x - r + sqrt(max(0.0, r * r - delta * delta));
                float2 v = 0.5 + 0.5 * erf2((p.x + float2(-curved, curved)) * (0.70710678 / deviation));
                sum += (v.y - v.x) * exp(-y * y / (2.0 * deviation * deviation)) / (2.5066283 * deviation) * stride;
            }
            return sum;
        }

        bool outside(Fragment f)
        {
            return f.world.x < f.clipRect.x || f.world.y < f.clipRect.y || f.world.x >= f.clipRect.z || f.world.y >= f.clipRect.w;
        }

        uint cornerBit(float2 q)
        {
            return q.y < 0.0 ? (q.x < 0.0 ? 1u : 2u) : (q.x < 0.0 ? 8u : 4u);
        }

        float4 pixelMain(Fragment f) : SV_Target
        {
            uint corners = (uint)floor(f.style.w / 4096.0);
            float clipRadius = f.style.w - corners * 4096.0;
            float2 ch = (f.clipRect.zw - f.clipRect.xy) * 0.5;
            float2 cq = f.world - f.clipRect.xy - ch;
            float cd = rounded(cq, ch, min(clipRadius, min(ch.x, ch.y)));
            float caa = max(fwidth(cd), 0.0001) * 0.5;
            float2 h = f.rect.zw * 0.5;
            float2 p = f.local - h;
            float r = min(f.style.x, min(h.x, h.y));
            float d = rounded(p, h, r);
            float aa = max(fwidth(d), 0.0001) * 0.5;
            uint atlasWidth;
            uint atlasHeight;
            source.GetDimensions(atlasWidth, atlasHeight);
            float4 sampled = source.SampleLevel(linearClamp, f.texel / float2(atlasWidth, atlasHeight), 0.0);
            if (outside(f))
                discard;
            float cut = 1.0;
            if (f.style.w > 0.0 && (corners & cornerBit(cq)) != 0u)
                cut = 1.0 - smoothstep(-caa, caa, cd);
            float kind = f.style.z;
            if (kind == 1.0)
            {
                float glyph = sampled.a * f.color.a * cut;
                return float4(f.color.rgb * glyph, glyph);
            }
            if (kind == 2.0)
            {
                float shade = shadow(p, h, r, max(f.style.y, 0.25)) * f.color.a * cut;
                return float4(f.color.rgb * shade, shade);
            }
            float coverage = (1.0 - smoothstep(-aa, aa, d)) * cut;
            if (kind == 3.0)
                return sampled * (coverage * f.color.a);
            float4 c = f.color;
            if (f.style.y > 0.0 && f.borderColor.a > 0.0)
                c = lerp(c, f.borderColor, smoothstep(-f.style.y - aa, -f.style.y + aa, d));
            float alpha = c.a * coverage;
            return float4(c.rgb * alpha, alpha);
        }

        """;
}
