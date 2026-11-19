// Region outlines and handles for the editor preview. Each instance is one shape:
// a segment (edge), a circle (handle, ghost; A == B) or an arrow (segment with a triangular head at B),
// drawn as a quad around it and shaded by signed distance.

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
static const float3 Flow = float3(0.490, 0.827, 0.988); // #7DD3FC
static const float ArrowKind = 5.0;
static const float ArrowHeadLength = 10.0; // DIP
static const float ArrowHeadHalfWidth = 4.5; // DIP

struct Style
{
    float radius;     // half the line width, or the circle radius
    float ring;       // outline width around it
    float reach;      // how far the shape extends past its segment's bounds beyond radius and ring
    float4 fill;      // straight (not premultiplied) colour
    float4 outline;
};

Style StyleFor(uint kind)
{
    Style s;
    s.reach = 0.0;
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
        case 5u: // flow arrow shaft; the head is added in the shader
            s.radius = 1.25; s.ring = 1.0; s.fill = float4(Flow, 1.0); s.outline = float4(Dark, 0.6);
            s.reach = ArrowHeadLength;
            break;
        case 6u: // flow arrow handle
            s.radius = 7.0; s.ring = 1.5; s.fill = float4(Flow, 1.0); s.outline = float4(White, 1.0);
            break;
        default: // edge
            s.radius = 1.0; s.ring = 1.0; s.fill = float4(White, 0.9); s.outline = float4(Dark, 0.6);
            break;
    }
    s.radius *= scale;
    s.ring *= scale;
    s.reach *= scale;
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
    float extent = s.radius + s.ring + s.reach + 1.0;
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

// Signed distance to a triangle (negative inside).
float TriangleDistance(float2 p, float2 p0, float2 p1, float2 p2)
{
    float2 e0 = p1 - p0;
    float2 e1 = p2 - p1;
    float2 e2 = p0 - p2;
    float2 v0 = p - p0;
    float2 v1 = p - p1;
    float2 v2 = p - p2;
    float2 q0 = v0 - e0 * saturate(dot(v0, e0) / dot(e0, e0));
    float2 q1 = v1 - e1 * saturate(dot(v1, e1) / dot(e1, e1));
    float2 q2 = v2 - e2 * saturate(dot(v2, e2) / dot(e2, e2));
    float winding = sign(e0.x * e2.y - e0.y * e2.x);
    float2 d = min(min(float2(dot(q0, q0), winding * (v0.x * e0.y - v0.y * e0.x)),
                       float2(dot(q1, q1), winding * (v1.x * e1.y - v1.y * e1.x))),
                   float2(dot(q2, q2), winding * (v2.x * e2.y - v2.y * e2.x)));
    return -sqrt(d.x) * sign(d.y);
}

float4 PSMain(VSOut input) : SV_Target
{
    float2 p = input.position.xy;
    float2 a = input.segment.xy;
    float2 ab = input.segment.zw - a;
    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
    float d = length(p - (a + ab * t));

    Style s = StyleFor(input.kind);
    float signedDistance = d - s.radius;
    float length2 = dot(ab, ab);
    if (input.kind == (uint)ArrowKind && length2 > 1.0)
    {
        float2 direction = ab * rsqrt(length2);
        float2 normal = float2(-direction.y, direction.x);
        float2 tip = input.segment.zw;
        float2 baseCentre = tip - direction * (ArrowHeadLength * scale);
        float2 halfBase = normal * (ArrowHeadHalfWidth * scale);
        signedDistance = min(signedDistance, TriangleDistance(p, tip, baseCentre + halfBase, baseCentre - halfBase));
    }
    float fill = saturate(0.5 - signedDistance);
    float outer = saturate(s.ring + 0.5 - signedDistance);
    float ring = outer - fill;
    float alpha = s.fill.a * fill + s.outline.a * ring;
    float3 colour = s.fill.rgb * s.fill.a * fill + s.outline.rgb * s.outline.a * ring;
    return float4(colour, alpha);
}
