cbuffer Blit : register(b0)
{
    float2 renderSize;
    float viewToRenderScale;
    float mip;
    float2 offset;
    float2 viewSize;
};

Texture2D scene : register(t0);
SamplerState linearClamp : register(s0);

float4 VSMain(uint id : SV_VertexID) : SV_Position
{
    float2 corner = float2(id & 1, id >> 1);
    float2 px = (corner * renderSize - offset) / viewToRenderScale;
    return float4(px.x / viewSize.x * 2.0 - 1.0, 1.0 - px.y / viewSize.y * 2.0, 0.0, 1.0);
}

// 9-tap Catmull-Rom built from bilinear taps (Jimenez / MJP). samplePos is in texels of the mip being read.
float3 CatmullRom(float2 samplePos, float2 mipSize)
{
    float2 texPos1 = floor(samplePos - 0.5) + 0.5;
    float2 f = samplePos - texPos1;

    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);

    float2 w12 = w1 + w2;
    float2 offset12 = w2 / w12;

    float2 uv0 = (texPos1 - 1.0) / mipSize;
    float2 uv3 = (texPos1 + 2.0) / mipSize;
    float2 uv12 = (texPos1 + offset12) / mipSize;

    float3 result = 0.0;
    result += scene.SampleLevel(linearClamp, float2(uv0.x, uv0.y), mip).rgb * w0.x * w0.y;
    result += scene.SampleLevel(linearClamp, float2(uv12.x, uv0.y), mip).rgb * w12.x * w0.y;
    result += scene.SampleLevel(linearClamp, float2(uv3.x, uv0.y), mip).rgb * w3.x * w0.y;

    result += scene.SampleLevel(linearClamp, float2(uv0.x, uv12.y), mip).rgb * w0.x * w12.y;
    result += scene.SampleLevel(linearClamp, float2(uv12.x, uv12.y), mip).rgb * w12.x * w12.y;
    result += scene.SampleLevel(linearClamp, float2(uv3.x, uv12.y), mip).rgb * w3.x * w12.y;

    result += scene.SampleLevel(linearClamp, float2(uv0.x, uv3.y), mip).rgb * w0.x * w3.y;
    result += scene.SampleLevel(linearClamp, float2(uv12.x, uv3.y), mip).rgb * w12.x * w3.y;
    result += scene.SampleLevel(linearClamp, float2(uv3.x, uv3.y), mip).rgb * w3.x * w3.y;
    return saturate(result);
}

float4 PSMain(float4 position : SV_Position) : SV_Target
{
    float2 renderPx = offset + position.xy * viewToRenderScale;
    float2 mipSize = max(floor(renderSize / exp2(mip)), 1.0);
    float2 samplePos = renderPx * (mipSize / renderSize);
    return float4(CatmullRom(samplePos, mipSize), 1.0);
}
