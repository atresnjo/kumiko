namespace Kumiko.Rendering;

// Glass passes in HLSL, after OpenGlGlassShader.cs and Glass.cs. Appended to D3D11Shader.Source,
// whose constant buffer, Fragment and helpers they share. Backdrop textures are top-down like the
// frame, so the downsample reads frame rows directly where OpenGL flips them.
internal static class D3D11GlassShader
{
    internal const string Source = """
        float4 passVertex(uint id : SV_VertexID) : SV_Position
        {
            float2 p = float2((id << 1) & 2, id & 2);
            return float4(p * 2.0 - 1.0, 0.0, 1.0);
        }

        float4 downMain(float4 position : SV_Position) : SV_Target
        {
            int2 start = int2(area.xy) + int2(position.xy) * sampleDown;
            int2 stop = min(start + sampleDown, int2(area.zw));
            float3 sum = float3(0.0, 0.0, 0.0);
            [loop]
            for (int y = start.y; y < stop.y; y++)
            {
                [loop]
                for (int x = start.x; x < stop.x; x++)
                    sum += source.Load(int3(x, y, 0)).rgb;
            }
            return float4(sum / float((stop.x - start.x) * (stop.y - start.y)), 1.0);
        }

        float4 blurMain(float4 position : SV_Position) : SV_Target
        {
            int2 at = int2(position.xy);
            int2 offset = int2(direction);
            float3 sum = float3(0.0, 0.0, 0.0);
            float total = 0.0;
            [loop]
            for (int k = -radius; k <= radius; k++)
            {
                int2 p = clamp(at + offset * k, int2(0, 0), int2(size) - 1);
                float w = exp(-float(k * k) / (2.0 * sigma * sigma));
                sum += source.Load(int3(p, 0)).rgb * w;
                total += w;
            }
            return float4(sum / total, 1.0);
        }

        float2 distanceGradient(float2 p, float2 h, float r)
        {
            float2 q = abs(p) - h + r;
            float2 m = max(q, 0.0);
            float len = length(m);
            return float2(len + min(max(q.x, q.y), 0.0) - r, len > 0.0 ? (m.x + m.y) / len : 1.0);
        }

        float3 backdropTexel(int2 p)
        {
            return source.Load(int3(clamp(p, int2(0, 0), int2(size) - 1), 0)).rgb;
        }

        float3 backdrop(float2 q)
        {
            float2 u = (q - origin) / down - 0.5;
            float2 f = floor(u);
            float2 w = u - f;
            int2 p = int2(f);
            float3 top = lerp(backdropTexel(p), backdropTexel(p + int2(1, 0)), w.x);
            float3 bottom = lerp(backdropTexel(p + int2(0, 1)), backdropTexel(p + int2(1, 1)), w.x);
            return lerp(top, bottom, w.y);
        }

        float4 glassMain(Fragment f) : SV_Target
        {
            if (outside(f))
                discard;
            float cut = 1.0;
            if (f.style.w > 0.0)
            {
                uint corners = (uint)floor(f.style.w / 4096.0);
                float clipRadius = f.style.w - corners * 4096.0;
                float2 ch = (f.clipRect.zw - f.clipRect.xy) * 0.5;
                float2 cq = f.world - f.clipRect.xy - ch;
                float2 cg = distanceGradient(cq, ch, min(clipRadius, min(ch.x, ch.y)));
                float caa = max(cg.y / scale, 0.0001) * 0.5;
                if ((corners & cornerBit(cq)) != 0u)
                    cut = 1.0 - smoothstep(-caa, caa, cg.x);
            }
            float2 h = f.rect.zw * 0.5;
            float2 q = f.local - h;
            float r = min(f.style.x, min(h.x, h.y));
            float2 dg = distanceGradient(q, h, r);
            float aa = max(dg.y / scale, 0.0001) * 0.5;
            float coverage = (1.0 - smoothstep(-aa, aa, dg.x)) * cut * f.uv.z;
            if (coverage <= 0.0)
                discard;
            float depth = max(0.0, -dg.x);
            float bezel = f.uv.x;
            float2 normal = float2(0.0, 0.0);
            if (depth < bezel || depth < 3.0 * f.uv.w)
            {
                float rr = min(r * 1.5, min(h.x, h.y));
                float2 edge = abs(q) - h + rr;
                if (edge.x > 0.0 || edge.y > 0.0)
                    normal = normalize(max(edge, 0.0)) * float2(sign(q));
                else
                    normal = edge.x > edge.y ? float2(sign(q.x), 0.0) : float2(0.0, sign(q.y));
            }
            float bend = 0.0;
            if (depth < bezel && bezel > 0.0)
            {
                float u = 1.0 - depth / bezel;
                float v = 1.0 - u * u * u * u;
                float a = sqrt(v * sqrt(v));
                float b = u * u * u;
                float n = sqrt(a * a + b * b);
                float sn = b / n;
                float cs = a / n;
                float st = sn / 1.5;
                float ct = sqrt(1.0 - st * st);
                bend = f.uv.y * (sn * ct - cs * st) / (cs * ct + sn * st) / 1.118034;
            }
            float3 c = backdrop((f.world - normal * bend) * scale);
            float l = dot(c, float3(0.2126, 0.7152, 0.0722));
            float4 tone = f.borderColor;
            float level = tone.x + (tone.y - tone.x) * (1.0 - pow(max(1.0 - l, 0.0), tone.z));
            c = saturate(level.xxx + (c - l.xxx) * ((tone.y - tone.x) * tone.w));
            c = lerp(c, f.color.rgb, f.color.a);
            if (depth < 3.0 * f.uv.w)
            {
                float rim = depth / f.uv.w;
                float3 light = abs(normal.y);
                c = lerp(c, light, 0.35 * exp(-rim * rim));
            }
            return float4(c * coverage, coverage);
        }

        """;
}
