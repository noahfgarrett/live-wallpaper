cbuffer Quad : register(b0)
{
    float4 destRect;
    float2 targetSize;
    float2 padding;
};

Texture2D image : register(t0);
SamplerState linearClamp : register(s0);

struct VSOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    float2 corner = float2(id & 1, id >> 1);
    float2 px = destRect.xy + corner * destRect.zw;
    VSOut o;
    o.position = float4(px.x / targetSize.x * 2.0 - 1.0, 1.0 - px.y / targetSize.y * 2.0, 0.0, 1.0);
    o.uv = corner;
    return o;
}

float4 PSMain(VSOut input) : SV_Target
{
    return float4(image.Sample(linearClamp, input.uv).rgb, 1.0);
}
