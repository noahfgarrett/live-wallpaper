cbuffer Frame : register(b0)
{
    float4 windOffsets;
    float2 resolution;
    float fallPhase;
    float timePhase;
    float windPxPerSec;
    float speedMul;
    float sizeMul;
    float margin;
    float scale;
    float motionBlur;
    float period;
    float flakeCount;
};

cbuffer Layers : register(b1)
{
    float4 layerA[3];
    float4 layerB[3];
    float4 layerC[3];
    float4 shares;
};

Texture2D atlas : register(t0);
SamplerState linearClamp : register(s0);

static const float TwoPi = 6.2831853;
static const float AtlasCells = 4.0;

struct VSOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float alpha : TEXCOORD1;
    float softness : TEXCOORD2;
    float sprite : TEXCOORD3;
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
    float4 c = layerC[layer];

    float size = lerp(a.x, a.y, Rnd(id, 1u)) * sizeMul * scale;
    float fall = lerp(a.z, a.w, Rnd(id, 2u)) * scale;
    float swayAmp = lerp(c.x, c.y, Rnd(id, 4u)) * scale;
    float swayHz = max(1.0, round(lerp(c.z, c.w, Rnd(id, 5u)) * period)) / period;
    float phase = Rnd(id, 6u) * TwoPi;

    float2 span = resolution + 2.0 * margin;
    float k = max(1.0, round(fall * period / span.y));
    float y = span.y * frac(Rnd(id, 3u) + k * fallPhase) - margin;

    float swayArg = TwoPi * frac(swayHz * period * timePhase) + phase;
    float windOffset = layer == 0u ? windOffsets.x : (layer == 1u ? windOffsets.y : windOffsets.z);
    float x = PosMod(Rnd(id, 7u) * span.x + windOffset + swayAmp * sin(swayArg), span.x) - margin;

    float2 corner = float2(float(vertexId & 1u), float(vertexId >> 1u)) * 2.0 - 1.0;
    float alpha = lerp(b.x, b.y, Rnd(id, 8u));
    float2 offset;
    if (motionBlur > 0.5)
    {
        float2 velocity = float2(windPxPerSec * b.w + swayAmp * TwoPi * swayHz * cos(swayArg), fall * speedMul);
        float speed = max(length(velocity), 1e-3);
        float2 along = velocity / speed;
        float2 across = float2(-along.y, along.x);
        float streak = speed * 0.04;
        offset = across * corner.x * size * 0.5 + along * corner.y * (size + streak) * 0.5;
        alpha *= size / (size + streak * 0.6);
    }
    else
    {
        float spinCycles = round((Rnd(id, 10u) - 0.5) * 3.0 * period / TwoPi);
        float angle = Rnd(id, 9u) * TwoPi + TwoPi * frac(spinCycles * timePhase);
        float s = sin(angle);
        float co = cos(angle);
        float2 local = corner * size * 0.5;
        offset = float2(local.x * co - local.y * s, local.x * s + local.y * co);
    }

    float2 px = float2(x, y) + offset;
    VSOut o;
    o.position = float4(px.x / resolution.x * 2.0 - 1.0, 1.0 - px.y / resolution.y * 2.0, 0.0, 1.0);
    o.uv = corner * 0.5 + 0.5;
    o.alpha = alpha;
    o.softness = b.z;
    o.sprite = floor(Rnd(id, 11u) * AtlasCells);
    return o;
}

float4 PSMain(VSOut input) : SV_Target
{
    float sprite = atlas.Sample(linearClamp, float2((input.sprite + input.uv.x) / AtlasCells, input.uv.y)).a;
    float2 d = input.uv * 2.0 - 1.0;
    float blob = exp(-dot(d, d) * 3.2) * (1.0 - smoothstep(0.85, 1.0, length(d)));
    float shape = lerp(sprite, blob, input.softness);
    float a = shape * input.alpha;
    return float4(float3(0.95, 0.975, 1.0) * a, a);
}
