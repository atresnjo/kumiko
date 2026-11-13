// Liquid Glass kernels adapted from mygo's glass plugin, Copyright (c) 2026 MyGo contributors (MIT;
// see third_party/licenses/mygo-MIT.txt). Shadow and element techniques follow GPUI, Copyright 2022-2025
// Zed Industries, Inc. (Apache 2.0; see third_party/licenses/GPUI-Apache-2.0.txt).
namespace Kumiko.Rendering;

internal static class MetalShader
{
    internal const string Source = """
// kumiko's one shader: instanced quads that are rounded rectangles with
// borders (kind 0), glyph runs and icons sampled from the atlas (kind 1),
// shadows (kind 2): Gaussian blurs of rounded boxes, as GPUI draws them, or
// pictures sampled from the image atlas (kind 3), their corners rounded.
// Panes of glass (kind 4) have their own fragment function (MetalShader.cs).
// Each is clipped to its clip rectangle, some of whose corners may be
// rounded (a box with rounded corners that clips its children).
#include <metal_stdlib>
using namespace metal;

struct Instance {
    float4 rect;         // x, y, w, h in points
    float4 color;        // straight alpha
    float4 border_color;
    float4 uv;           // atlas u0, v0, u1, v1
    float4 clip;         // x0, y0, x1, y1
    float radius;
    float border_width;  // blur radius for shadows
    float kind;
    float clip_round;    // corners * 4096 + radius (Instance.cs's clipRound)
    float4 turn;         // radians clockwise, pivot x, y; applied after clipping
};

struct VOut {
    float4 pos [[position]];
    float2 local;
    float2 world;
    float2 uv;
    uint inst [[flat]];
};

vertex VOut vs(uint vid [[vertex_id]], uint iid [[instance_id]],
               constant Instance *instances [[buffer(0)]],
               constant float2 &viewport [[buffer(1)]]) {
    Instance i = instances[iid];
    float2 corner = float2(vid & 1, vid >> 1);
    // Shadows reach three standard deviations past their box.
    float grow = i.kind == 2 ? i.border_width * 3.0 : 0.0;
    float2 p = i.rect.xy - grow + corner * (i.rect.zw + 2.0 * grow);
    VOut o;
    // Clipping and shading stay in the upright frame; only the position turns.
    float2 q = p;
    if (i.turn.x != 0.0) {
        float s = sin(i.turn.x), c = cos(i.turn.x);
        float2 d = p - i.turn.yz;
        q = i.turn.yz + float2(d.x * c - d.y * s, d.x * s + d.y * c);
    }
    o.pos = float4(q / viewport * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    o.local = p - i.rect.xy;
    o.world = p;
    o.uv = mix(i.uv.xy, i.uv.zw, corner);
    o.inst = iid;
    return o;
}

// Shadows as GPUI draws them (after Evan Wallace's "fast rounded rectangle
// shadows"): the box's coverage blurred by a Gaussian, exact along x with
// erf, sampled four times along y.
static float gaussian(float x, float sigma) {
    return exp(-(x * x) / (2.0 * sigma * sigma)) / (sqrt(2.0 * M_PI_F) * sigma);
}

static float2 erf2(float2 x) {
    float2 s = sign(x), a = abs(x);
    x = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
    x *= x;
    return s - s / (x * x);
}

static float blur_along_x(float x, float y, float sigma, float corner, float2 half_size) {
    float delta = min(half_size.y - corner - abs(y), 0.0);
    float curved = half_size.x - corner + sqrt(max(0.0, corner * corner - delta * delta));
    float2 integral = 0.5 + 0.5 * erf2((x + float2(-curved, curved)) * (sqrt(0.5) / sigma));
    return integral.y - integral.x;
}

static float shadow_alpha(float2 p, float2 half_size, float corner, float sigma) {
    float low = p.y - half_size.y;
    float high = p.y + half_size.y;
    float start = clamp(-3.0 * sigma, low, high);
    float end = clamp(3.0 * sigma, low, high);
    float step = (end - start) / 4.0;
    float y = start + step * 0.5;
    float alpha = 0.0;
    for (int k = 0; k < 4; k++) {
        alpha += blur_along_x(p.x, p.y - y, sigma, corner, half_size) * gaussian(y, sigma) * step;
        y += step;
    }
    return alpha;
}

static float rounded_box(float2 p, float2 half_size, float r) {
    float2 q = abs(p) - half_size + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

static float4 shade(VOut in, Instance i, texture2d<float> atlas, texture2d<float> images);

// How much of the fragment the instance's clip lets through: discarded
// outside it, cut in its rounded corners.
static float clip_coverage(VOut in, Instance i) {
    if (in.world.x < i.clip.x || in.world.y < i.clip.y || in.world.x > i.clip.z || in.world.y > i.clip.w)
        discard_fragment();
    float clip_cover = 1.0;
    if (i.clip_round > 0.0) {
        float corners = floor(i.clip_round / 4096.0);
        float rad = i.clip_round - corners * 4096.0;
        float2 ch = (i.clip.zw - i.clip.xy) * 0.5;
        float2 q = in.world - i.clip.xy - ch;
        // Derivatives outside the branch on the corner: it differs by pixel.
        float cd = rounded_box(q, ch, min(rad, min(ch.x, ch.y)));
        float caa = max(fwidth(cd), 0.0001) * 0.5;
        uint bit = q.y < 0.0 ? (q.x < 0.0 ? 1u : 2u) : (q.x < 0.0 ? 8u : 4u);
        if ((uint(corners) & bit) != 0u) clip_cover = 1.0 - smoothstep(-caa, caa, cd);
        if (clip_cover <= 0.0) discard_fragment();
    }
    return clip_cover;
}

fragment float4 fs(VOut in [[stage_in]],
                   constant Instance *instances [[buffer(0)]],
                   texture2d<float> atlas [[texture(0)]],
                   texture2d<float> images [[texture(1)]]) {
    Instance i = instances[in.inst];
    float clip_cover = clip_coverage(in, i);
    return shade(in, i, atlas, images) * clip_cover;
}

static float4 shade(VOut in, Instance i, texture2d<float> atlas, texture2d<float> images) {
    if (i.kind == 1.0) {
        constexpr sampler s(filter::linear, address::clamp_to_edge);
        float a = atlas.sample(s, in.uv / float2(atlas.get_width(), atlas.get_height())).a * i.color.a;
        return float4(i.color.rgb * a, a);
    }

    float2 half_size = i.rect.zw * 0.5;
    float2 p = in.local - half_size;
    float r = min(i.radius, min(half_size.x, half_size.y));
    float d = rounded_box(p, half_size, r);

    if (i.kind == 3.0) {
        // Premultiplied already: scaled by the corners' coverage and opacity.
        constexpr sampler s(filter::linear, address::clamp_to_edge);
        float aa = max(fwidth(d), 0.0001) * 0.5;
        float coverage = 1.0 - smoothstep(-aa, aa, d);
        return images.sample(s, in.uv / float2(images.get_width(), images.get_height())) * (coverage * i.color.a);
    }

    if (i.kind == 2.0) {
        float sigma = max(i.border_width, 0.25);
        float a = shadow_alpha(p, half_size, r, sigma) * i.color.a;
        return float4(i.color.rgb * a, a);
    }

    float aa = max(fwidth(d), 0.0001) * 0.5;
    float coverage = 1.0 - smoothstep(-aa, aa, d);
    float4 c = i.color;
    if (i.border_width > 0.0 && i.border_color.a > 0.0) {
        float inner = smoothstep(-i.border_width - aa, -i.border_width + aa, d);
        c = mix(c, i.border_color, inner);
    }
    float a = c.a * coverage;
    return float4(c.rgb * a, a);
}
// Panes of Liquid Glass (Glass.cs), after MetalShader.cs: the backdrop's
// kernels, which average the frame's pixels around a pane and blur them,
// and the pane's fragment function. Glass.cs computes the same on the CPU.

// A pane's backdrop (Glass.cs's Backdrop): the frame's pixels of area
// (x0, y0, x1, y1), averaged over squares of down pixels into size texels,
// blurred by sigma texels out to radius each way.
struct GlassBackdrop {
    int4 area;
    int2 size;
    int down;
    int radius;
    float sigma;
};

kernel void glass_down(texture2d<float, access::read> frame [[texture(0)]],
                       texture2d<float, access::write> dst [[texture(1)]],
                       constant GlassBackdrop &b [[buffer(0)]],
                       uint2 gid [[thread_position_in_grid]]) {
    if (int(gid.x) >= b.size.x || int(gid.y) >= b.size.y) return;
    int x0 = b.area.x + int(gid.x) * b.down;
    int y0 = b.area.y + int(gid.y) * b.down;
    int x1 = min(x0 + b.down, b.area.z);
    int y1 = min(y0 + b.down, b.area.w);
    float3 sum = float3(0.0);
    for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++) sum += frame.read(uint2(x, y)).rgb;
    dst.write(float4(sum / float((x1 - x0) * (y1 - y0)), 1.0), gid);
}

kernel void glass_blur(texture2d<float, access::read> src [[texture(0)]],
                       texture2d<float, access::write> dst [[texture(1)]],
                       constant GlassBackdrop &b [[buffer(0)]],
                       constant int2 &dir [[buffer(1)]],
                       uint2 gid [[thread_position_in_grid]]) {
    if (int(gid.x) >= b.size.x || int(gid.y) >= b.size.y) return;
    float3 sum = float3(0.0);
    float wsum = 0.0;
    for (int k = -b.radius; k <= b.radius; k++) {
        int2 p = clamp(int2(gid) + dir * k, int2(0), b.size - 1);
        float w = exp(-float(k * k) / (2.0 * b.sigma * b.sigma));
        sum += src.read(uint2(p)).rgb * w;
        wsum += w;
    }
    dst.write(float4(sum / wsum, 1.0), gid);
}

// Where the pane's backdrop lies: its area's corner in the frame's pixels,
// its size in texels, the averaging, and the frame's pixels a point.
struct GlassPane {
    float2 origin;
    float2 size;
    float down;
    float scale;
};

static float3 glass_texel(texture2d<float> t, int2 p, int2 size) {
    return t.read(uint2(clamp(p, int2(0), size - 1))).rgb;
}

// The backdrop at pixel q of the frame, bilinear from its texels.
static float3 glass_sample(texture2d<float> t, GlassPane pane, float2 q) {
    float2 u = (q - pane.origin) / pane.down - 0.5;
    float2 f = floor(u);
    float2 w = u - f;
    int2 p = int2(f);
    int2 size = int2(pane.size);
    float3 top = glass_texel(t, p, size) * (1.0 - w.x) + glass_texel(t, p + int2(1, 0), size) * w.x;
    float3 bot = glass_texel(t, p + int2(0, 1), size) * (1.0 - w.x) + glass_texel(t, p + int2(1, 1), size) * w.x;
    return top * (1.0 - w.y) + bot * w.y;
}

static float glass_lens(float t, float bezel) {
    if (bezel <= 0.0 || t >= bezel) return 0.0;
    float u = 1.0 - max(t, 0.0) / bezel;
    float v = 1.0 - u * u * u * u;
    float a = sqrt(v * sqrt(v));
    float b = u * u * u;
    float n = sqrt(a * a + b * b);
    float s = b / n, c = a / n;
    float st = s / 1.5;
    float ct = sqrt(1.0 - st * st);
    return (s * ct - c * st) / (c * ct + s * st) / 1.1180340;
}

static float2 glass_normal(float2 q, float2 h, float radius) {
    float r = min(radius * 1.5, min(h.x, h.y));
    float2 a = abs(q) - (h - r);
    if (a.x > 0.0 || a.y > 0.0) {
        float2 m = max(a, float2(0.0));
        return sign(q) * m / sqrt(m.x * m.x + m.y * m.y);
    }
    return a.x > a.y ? float2(sign(q.x), 0.0) : float2(0.0, sign(q.y));
}

static float3 glass_tone(float3 c, float4 tone) {
    float l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
    float t = tone.x + (tone.y - tone.x) * (1.0 - pow(max(1.0 - l, 0.0), tone.z));
    float k = (tone.y - tone.x) * tone.w;
    return clamp(t + (c - l) * k, 0.0, 1.0);
}

fragment float4 fs_glass(VOut in [[stage_in]],
                         constant Instance *instances [[buffer(0)]],
                         constant GlassPane &pane [[buffer(1)]],
                         texture2d<float> backdrop [[texture(2)]]) {
    Instance i = instances[in.inst];
    float clip_cover = clip_coverage(in, i);
    // The pane's edge, antialiased as a quad's.
    float2 hp = i.rect.zw * 0.5;
    float d = rounded_box(in.local - hp, hp, min(i.radius, min(hp.x, hp.y)));
    float aa = max(fwidth(d), 0.0001) * 0.5;
    float coverage = (1.0 - smoothstep(-aa, aa, d)) * clip_cover * i.uv.z;

    // The optics, in the frame's pixels.
    float s = pane.scale;
    float2 p = in.world * s;
    float2 h = hp * s;
    float2 q = p - i.rect.xy * s - h;
    float rr = min(i.radius * s, min(h.x, h.y));
    float t = max(-rounded_box(q, h, rr), 0.0);
    float bezel = i.uv.x * s;
    float rim_width = i.uv.w * s;
    float2 n = float2(0.0);
    if (t < bezel || t < 3.0 * rim_width) n = glass_normal(q, h, rr);
    float2 at = p;
    if (t < bezel) at -= n * (i.uv.y * s * glass_lens(t, bezel));
    float3 c = glass_tone(glass_sample(backdrop, pane, at), i.border_color);
    c += (i.color.rgb - c) * i.color.a;
    if (t < 3.0 * rim_width) {
        float r = t / rim_width;
        float a = 0.35 * exp(-r * r);
        // Light from above and below: the more the edge faces it, the lighter.
        c += (abs(n.y) - c) * a;
    }
    return float4(c, 1.0) * coverage;
}

""";
}
