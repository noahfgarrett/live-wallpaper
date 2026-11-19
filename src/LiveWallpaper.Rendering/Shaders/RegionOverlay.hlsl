// Region outlines and handles for the editor preview. Each instance is one shape:
// a segment (edge) or a circle (handle, ghost; A == B), drawn as a quad around it and shaded by distance.

#define MAX_SHAPES 1100

cbuffer Overlay : register(b0)
{
    float2 resolution;
    float scale;
    float shapeCount;
    float4 shapes[MAX_SHAPES * 2]; // [2i] = A.xy, B.xy in pixels; [2i + 1].x = kind
};

static const float3 White = float3(1.0, 1.0, 1.0);
static const float3 Dark = float3(0.05, 0.07, 0.1);
static const float3 Accent = float3(0.376, 0.804, 1.0); // #60CDFF

struct Style
{
    float radius;     // half the line width, or the circle radius
    float ring;       // outline width around it
    float4 fill;      // straight (not premultiplied) colour
    float4 outline;
};

Style StyleFor(uint kind)
{
    Style s;
    switch (kind)
    {
        case 1u: // selected edge
            s.radius = 1.25; s.ring = 1.0; s.fill = float4(Accent, 1.0); s.outline = float4(Dark, 0.6);
            break;
        case 2u: // handle
            s.radius = 6.0; s.ring = 1.5; s.fill = float4(White, 1.0); s.outline = float4(Dark, 0.9);
            break;
        case 3u: // selected handle
            s.radius = 7.0; s.ring = 1.5; s.fill = float4(Accent, 1.0); s.outline = float4(White, 1.0);
            break;
        case 4u: // ghost: where a click adds a corner
            s.radius = 5.0; s.ring = 1.0; s.fill = float4(White, 0.6); s.outline = float4(Dark, 0.6);
            break;
        default: // edge
            s.radius = 1.0; s.ring = 1.0; s.fill = float4(White, 0.9); s.outline = float4(Dark, 0.6);
            break;
    }
    s.radius *= scale;
    s.ring *= scale;
    return s;
}

struct VSOut
{
    float4 position : SV_Position;
    nointerpolation float4 segment : TEXCOORD0;
    nointerpolation uint kind : TEXCOORD1;
};

VSOut VSMain(uint vertexId : SV_VertexID, uint id : SV_InstanceID)
{
    float4 segment = shapes[id * 2u];
    uint kind = (uint)shapes[id * 2u + 1u].x;
    Style s = StyleFor(kind);
    float extent = s.radius + s.ring + 1.0;
    float2 lo = min(segment.xy, segment.zw) - extent;
    float2 hi = max(segment.xy, segment.zw) + extent;
    float2 corner = float2(float(vertexId & 1u), float(vertexId >> 1u));
    float2 px = lerp(lo, hi, corner);

    VSOut o;
    o.position = float4(px.x / resolution.x * 2.0 - 1.0, 1.0 - px.y / resolution.y * 2.0, 0.0, 1.0);
    o.segment = segment;
    o.kind = id < (uint)shapeCount ? kind : 0u;
    return o;
}

float4 PSMain(VSOut input) : SV_Target
{
    float2 p = input.position.xy;
    float2 a = input.segment.xy;
    float2 ab = input.segment.zw - a;
    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
    float d = length(p - (a + ab * t));

    Style s = StyleFor(input.kind);
    float fill = saturate(s.radius - d + 0.5);
    float outer = saturate(s.radius + s.ring - d + 0.5);
    float ring = outer - fill;
    float alpha = s.fill.a * fill + s.outline.a * ring;
    float3 colour = s.fill.rgb * s.fill.a * fill + s.outline.rgb * s.outline.a * ring;
    return float4(colour, alpha);
}
