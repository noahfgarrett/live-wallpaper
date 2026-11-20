cbuffer Frame : register(b0)
{
    float4 windOffsets;
    float2 resolution;
    float fallPhase;
    float windPxPerSec;
    float speedMul;
    float exposure;
    float margin;
    float scale;
    float opacity;
    float period;
    float streakCount;
    float padding;
};

cbuffer Layers : register(b1)
{
    float4 layerA[3];
    float4 layerB[3];
    float4 shares;
};

Texture2D regionMask : register(t1);
SamplerState linearClamp : register(s0);

struct VSOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float alpha : TEXCOORD1;
};

uint Hash(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float Rnd(uint id, uint k)
{
    return float(Hash(id * 0x9e3779b9u + k * 0x85ebca6bu) & 0xffffffu) / 16777216.0;
}

float PosMod(float v, float m)
{
    return v - m * floor(v / m);
}

VSOut VSMain(uint vertexId : SV_VertexID, uint id : SV_InstanceID)
{
    float r = Rnd(id, 0u);
    uint layer = r < shares.x ? 0u : (r < shares.y ? 1u : 2u);
    float4 a = layerA[layer];
    float4 b = layerB[layer];

    float width = lerp(a.x, a.y, Rnd(id, 1u)) * scale;
    float fall = lerp(a.z, a.w, Rnd(id, 2u)) * scale;

    float2 span = resolution + 2.0 * margin;
    float k = max(1.0, round(fall * period / span.y));
    float y = span.y * frac(Rnd(id, 3u) + k * fallPhase) - margin;
    float windOffset = layer == 0u ? windOffsets.x : (layer == 1u ? windOffsets.y : windOffsets.z);
    float x = PosMod(Rnd(id, 7u) * span.x + windOffset, span.x) - margin;

    float2 velocity = float2(windPxPerSec * b.w, (k * span.y / period) * speedMul);
    float speed = length(velocity);
    float2 along = velocity / speed;
    float2 across = float2(-along.y, along.x);
    float len = max(speed * exposure * lerp(0.8, 1.2, Rnd(id, 5u)), width * 2.0);

    float2 corner = float2(float(vertexId & 1u), float(vertexId >> 1u));
    float2 px = float2(x, y) + across * (corner.x - 0.5) * width * 2.0 - along * corner.y * len;
    VSOut o;
    o.position = float4(px.x / resolution.x * 2.0 - 1.0, 1.0 - px.y / resolution.y * 2.0, 0.0, 1.0);
    o.uv = corner;
    o.alpha = lerp(b.x, b.y, Rnd(id, 8u)) * opacity;
    return o;
}

float4 PSMain(VSOut input) : SV_Target
{
    float across = 1.0 - abs(input.uv.x * 2.0 - 1.0);
    float a = across * across * smoothstep(0.0, 0.25, input.uv.y) * smoothstep(1.0, 0.45, input.uv.y) * input.alpha;
    a *= regionMask.Sample(linearClamp, input.position.xy / resolution).r;
    return float4(float3(0.82, 0.87, 0.94) * a, a);
}
