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

static const int MaxWideTaps = 8;
static const float MaxReduction = 1.999;
static const float ReductionTolerance = 1e-4;

float CatmullRomWeight(float x)
{
    x = abs(x);
    if (x < 1.0)
    {
        return (1.5 * x - 2.5) * x * x + 1.0;
    }
    if (x < 2.0)
    {
        return ((-0.5 * x + 2.5) * x - 4.0) * x + 2.0;
    }
    return 0.0;
}

// Catmull-Rom stretched by reduction (support 2 * reduction texels each side), for the reduction a mip leaves over:
// it averages detail finer than a view pixel that plain Catmull-Rom would alias. Up to 8 x 8 texel loads.
float3 WideCatmullRom(float2 samplePos, float2 mipSize, float2 reduction)
{
    int2 first = int2(floor(samplePos - 2.0 * reduction - 0.5)) + 1;
    int2 last = int2(ceil(samplePos + 2.0 * reduction - 0.5)) - 1;
    int2 count = min(last - first + 1, MaxWideTaps);
    int2 maxTexel = int2(mipSize) - 1;
    int level = int(mip);

    float wx[MaxWideTaps];
    float wy[MaxWideTaps];
    float sumX = 0.0;
    float sumY = 0.0;
    [unroll]
    for (int k = 0; k < MaxWideTaps; k++)
    {
        wx[k] = k < count.x ? CatmullRomWeight((first.x + k + 0.5 - samplePos.x) / reduction.x) : 0.0;
        wy[k] = k < count.y ? CatmullRomWeight((first.y + k + 0.5 - samplePos.y) / reduction.y) : 0.0;
        sumX += wx[k];
        sumY += wy[k];
    }

    float3 result = 0.0;
    [loop]
    for (int j = 0; j < MaxWideTaps; j++)
    {
        if (j >= count.y)
        {
            break;
        }
        int y = clamp(first.y + j, 0, maxTexel.y);
        float3 row = 0.0;
        [loop]
        for (int i = 0; i < MaxWideTaps; i++)
        {
            if (i >= count.x)
            {
                break;
            }
            int x = clamp(first.x + i, 0, maxTexel.x);
            row += scene.Load(int3(x, y, level)).rgb * wx[i];
        }
        result += row * wy[j];
    }
    return saturate(result / (sumX * sumY));
}

float4 PSMain(float4 position : SV_Position) : SV_Target
{
    float2 renderPx = offset + position.xy * viewToRenderScale;
    float2 mipSize = max(floor(renderSize / exp2(mip)), 1.0);
    float2 texelsPerRenderPx = mipSize / renderSize;
    float2 samplePos = renderPx * texelsPerRenderPx;
    // How many texels of this mip one view pixel spans: 1 or less at 100 % and above, or exactly on a mip.
    float2 reduction = min(viewToRenderScale * texelsPerRenderPx, MaxReduction);
    float3 colour;
    [branch]
    if (any(reduction > 1.0 + ReductionTolerance))
    {
        colour = WideCatmullRom(samplePos, mipSize, max(reduction, 1.0));
    }
    else
    {
        colour = CatmullRom(samplePos, mipSize);
    }
    return float4(colour, 1.0);
}
