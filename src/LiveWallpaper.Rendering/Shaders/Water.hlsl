cbuffer Frame : register(b0)
{
    float4 imageDest;
    float2 resolution;
    float phase;
    float cycle;
    float foamAmount;
    float scale;
    float mist;
    float mistReach;
};

Texture2D image : register(t0);
Texture2D regionMask : register(t1);
Texture2D flowField : register(t2);
SamplerState linearClamp : register(s0);

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

uint Cell(float2 c)
{
    return Hash(uint(int(c.x) + 32768) * 0x10001u ^ Hash(uint(int(c.y) + 32768)));
}

float H2(float2 c)
{
    return Rnd(Cell(c), 0u);
}

float Noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(H2(i), H2(i + float2(1, 0)), f.x), lerp(H2(i + float2(0, 1)), H2(i + float2(1, 1)), f.x), f.y);
}

float3 Img(float2 px)
{
    return image.SampleLevel(linearClamp, (px - imageDest.xy) / imageDest.zw, 0).rgb;
}

float MaskAt(float2 px)
{
    return regionMask.SampleLevel(linearClamp, px / resolution, 0).r;
}

float4 VSMain(uint id : SV_VertexID) : SV_Position
{
    float2 p = float2(float((id << 1) & 2u), float(id & 2u));
    return float4(p * 2.0 - 1.0, 0.0, 1.0);
}

float4 PSMain(float4 position : SV_Position) : SV_Target
{
    float2 px = position.xy;
    float2 flow = flowField.SampleLevel(linearClamp, px / resolution, 0).rg;
    float speed = length(flow);
    if (speed < 1e-3)
    {
        discard;
    }
    float2 dir = flow / speed;
    float2 perp = float2(-dir.y, dir.x);
    float m = MaskAt(px);

    // Mist: water upstream within reach, and no water just downstream (the base of the fall).
    float plume = 0.0;
    for (int k = 1; k <= 6; k++)
    {
        float f = float(k) / 6.0;
        float2 up = px - dir * mistReach * f;
        float spread = mistReach * f * 0.45;
        float w = max(MaskAt(up), max(MaskAt(up + perp * spread), MaskAt(up - perp * spread)));
        plume = max(plume, w * (1.0 - f * 0.8));
    }
    float base = plume * (1.0 - MaskAt(px + dir * mistReach * 0.3));
    float2 sq = px / scale;
    float rise = 38.0;
    float mp0 = frac(phase * 0.5 + 0.13);
    float mp1 = frac(mp0 + 0.5);
    float mw = abs(2.0 * mp0 - 1.0);
    float n0 = Noise((sq + float2(0.0, rise * mp0) + dir * 20.0 * mp0) / 46.0) * 0.6 + Noise((sq + float2(0.0, rise * mp0)) / 17.0) * 0.4;
    float n1 = Noise((sq + float2(0.0, rise * mp1) + dir * 20.0 * mp1) / 46.0 + 7.3) * 0.6 + Noise((sq + float2(0.0, rise * mp1)) / 17.0 + 3.1) * 0.4;
    float haze = lerp(n0, n1, mw);
    float mistA = mist * smoothstep(0.0, 0.85, base) * (0.35 + 0.65 * haze) * 0.8;
    if (m < 0.002 && mistA < 0.004)
    {
        discard;
    }

    // Two-phase flow: two samples displaced along the flow, cross-faded so neither reset shows.
    float2 travel = flow * cycle;
    float2 q = float2(dot(px, perp), dot(px, dir)) / scale;
    float n = Noise(q / float2(70.0, 140.0));
    float p0 = frac(phase + n);
    float p1 = frac(p0 + 0.5);
    float fade = abs(2.0 * p0 - 1.0);
    float2 wobble = perp * (Noise(q / float2(9.0, 30.0) - float2(0.0, p0 * 4.0)) - 0.5) * 2.0 * scale;
    float2 a = px - travel * p0 + wobble;
    float2 b = px - travel * p1 + wobble;
    float3 still = Img(px);

    // Samples that land outside the water (rock above a waterfall) are dropped, so nothing foreign is dragged in.
    float wa = (1.0 - fade) * MaskAt(a);
    float wb = fade * MaskAt(b);
    float ws = 0.02;
    float3 c = (Img(a) * wa + Img(b) * wb + still * ws) / (wa + wb + ws);

    float lum = dot(still, float3(0.299, 0.587, 0.114));
    float along = length(travel) / scale;
    float f0 = Noise(float2(q.x / 3.5, (q.y - along * p0) / 26.0));
    float f1 = Noise(float2(q.x / 3.5, (q.y - along * p1) / 26.0));
    float foam = smoothstep(0.55, 0.95, lerp(f0, f1, fade)) * smoothstep(0.35, 0.85, lum) * foamAmount;
    c = lerp(c, float3(0.97, 0.98, 1.0), foam * 0.55);

    float3 mistColor = lerp(float3(0.9, 0.93, 0.97), still * 0.5 + 0.55, 0.3);
    return float4(c * m * (1.0 - mistA) + mistColor * mistA, m * (1.0 - mistA) + mistA);
}
