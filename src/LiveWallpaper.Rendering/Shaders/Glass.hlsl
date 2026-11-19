cbuffer Frame : register(b0)
{
    float2 resolution;
    float timePhase;
    float scale;
    float drops;
    float sizeMul;
    float sliding;
    float fog;
    float paneCount;
    float3 framePadding;
};

struct GlassPane
{
    float4 inv0;
    float4 inv1;
    float4 inv2;
    float4 fwd0;
    float4 fwd1;
    float4 fwd2;
    float4 boundsPx;
    float2 panePx;
    float2 padding;
};

cbuffer Panes : register(b1)
{
    GlassPane panes[16];
};

Texture2D frameCopy : register(t0);
Texture2D regionMask : register(t1);
SamplerState linearClamp : register(s0);

static const float Period = 3600.0;
static const float TwoPi = 6.2831853;
static const float MinDepth = 1e-6;
static const uint MaxPanes = 16;

struct VSOut
{
    float4 position : SV_Position;
    nointerpolation uint pane : PANE;
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

uint Cell(float2 c)
{
    return Hash(uint(int(c.x) + 32768) * 0x10001u ^ Hash(uint(int(c.y) + 32768)));
}

float3 Scene(float2 px, float lod)
{
    return frameCopy.SampleLevel(linearClamp, px / resolution, lod).rgb;
}

/// Unit pane uv to target pixels.
float2 ToPx(GlassPane pane, float2 uv)
{
    float3 h = float3(uv, 1.0);
    float3 s = float3(dot(pane.fwd0.xyz, h), dot(pane.fwd1.xyz, h), dot(pane.fwd2.xyz, h));
    return s.xy / max(s.z, MinDepth);
}

float2 ToScreen(GlassPane pane, float2 paneRef)
{
    return ToPx(pane, paneRef * scale / pane.panePx);
}

/// How far px lies outside the edge a -> b, in pixels; negative on the inner side.
float EdgeOutside(float2 a, float2 b, float winding, float2 px)
{
    float2 e = b - a;
    return -winding * (e.x * (px.y - a.y) - e.y * (px.x - a.x)) / max(length(e), MinDepth);
}

/// Signed distance in pixels from px to the pane's quad: negative inside.
float PaneDistance(GlassPane pane, float2 px)
{
    float2 c0 = ToPx(pane, float2(0.0, 0.0));
    float2 c1 = ToPx(pane, float2(1.0, 0.0));
    float2 c2 = ToPx(pane, float2(1.0, 1.0));
    float2 c3 = ToPx(pane, float2(0.0, 1.0));
    float2 e0 = c1 - c0;
    float2 e1 = c2 - c1;
    float winding = e0.x * e1.y - e0.y * e1.x >= 0.0 ? 1.0 : -1.0;
    return max(
        max(EdgeOutside(c0, c1, winding, px), EdgeOutside(c1, c2, winding, px)),
        max(EdgeOutside(c2, c3, winding, px), EdgeOutside(c3, c0, winding, px)));
}

/// Bounding boxes can overlap other panes, so each pixel belongs to the pane it is deepest inside (lowest index on ties).
bool OwnsPixel(uint index, float2 px)
{
    float mine = PaneDistance(panes[index], px);
    uint count = uint(paneCount);
    [loop]
    for (uint j = 0; j < MaxPanes; j++)
    {
        if (j >= count)
        {
            break;
        }
        if (j == index)
        {
            continue;
        }
        float other = PaneDistance(panes[j], px);
        if (other < mine || (other == mine && j < index))
        {
            return false;
        }
    }
    return true;
}

/// Keeps the nearest drop: best.x = normalized distance (< 1 inside), best.yz = centre in pane reference px, best.w = radius.
void Consider(inout float4 best, inout float bestAlpha, float2 p, float2 c, float r, float alpha, float stretchTop)
{
    float2 d = p - c;
    d.y *= d.y < 0.0 ? stretchTop : 1.0;
    float nd = length(d) / r;
    if (nd < 1.0 && alpha > 0.01 && nd / max(alpha, 0.01) < best.x / max(bestAlpha, 0.01))
    {
        best = float4(nd, c, r);
        bestAlpha = alpha;
    }
}

void Beads(inout float4 best, inout float bestAlpha, float2 p, float cs, float rMin, float rMax, float density, float salt)
{
    float2 cell = floor(p / cs);
    uint id = Cell(cell + salt * 131.0);
    float cycles = max(1.0, round(Period / lerp(25.0, 70.0, Rnd(id, 1u))));
    float t = timePhase * cycles + Rnd(id, 2u);
    uint life = Hash(id ^ (uint(int(floor(t))) * 0x27d4eb2du));
    float lp = frac(t);
    if (Rnd(life, 3u) > density)
    {
        return;
    }
    float bias = Rnd(life, 4u);
    float r = lerp(rMin, rMax, bias * bias) * sizeMul;
    float2 c = (cell + 0.5 + (float2(Rnd(life, 5u), Rnd(life, 6u)) - 0.5) * max(0.0, 1.0 - 2.2 * r / cs)) * cs;
    float alpha = smoothstep(0.0, 0.04, lp) * smoothstep(1.0, 0.55, lp);
    Consider(best, bestAlpha, p, c, r * lerp(0.75, 1.0, alpha), alpha, 1.0);
}

VSOut VSMain(uint vertexId : SV_VertexID, uint instance : SV_InstanceID)
{
    float4 bounds = panes[instance].boundsPx;
    float2 corner = float2(float(vertexId & 1u), float(vertexId >> 1u));
    float2 px = bounds.xy + corner * bounds.zw;
    VSOut o;
    o.position = float4(px.x / resolution.x * 2.0 - 1.0, 1.0 - px.y / resolution.y * 2.0, 0.0, 1.0);
    o.pane = instance;
    return o;
}

float4 PSMain(VSOut input) : SV_Target
{
    float2 px = input.position.xy;
    float m = regionMask.SampleLevel(linearClamp, px / resolution, 0).r;
    if (m < 0.002 || !OwnsPixel(input.pane, px))
    {
        discard;
    }
    GlassPane pane = panes[input.pane];
    float3 h = float3(px, 1.0);
    float3 q = float3(dot(pane.inv0.xyz, h), dot(pane.inv1.xyz, h), dot(pane.inv2.xyz, h));
    if (q.z < MinDepth)
    {
        // Beyond the pane's horizon, where its perspective has no meaning.
        discard;
    }
    float2 uv = q.xy / q.z;
    float2 p = uv * pane.panePx / scale;
    float paneH = pane.panePx.y / scale;
    float4 sharp = frameCopy.Load(int3(int2(px), 0));

    float4 best = float4(9.0, 0.0, 0.0, 1.0);
    float bestAlpha = 0.0;
    Beads(best, bestAlpha, p, 16.0, 1.5, 4.5, drops, 0.0);
    Beads(best, bestAlpha, p, 44.0, 4.0, 12.0, drops * 0.8, 1.0);

    // Sliding drops: one per column at a time; each pixel checks its own column and its two neighbours.
    float clear = 0.0;
    float colW = 80.0 * sizeMul;
    [unroll]
    for (int dc = -1; dc <= 1; dc++)
    {
        float col = floor(p.x / colW) + float(dc);
        uint id = Cell(float2(col, 977.0));
        float cycles = max(1.0, round(Period / lerp(6.0, 16.0, Rnd(id, 1u))));
        float t = timePhase * cycles + Rnd(id, 2u);
        uint run = Hash(id ^ (uint(int(floor(t))) * 0x165667b1u));
        if (Rnd(run, 3u) > sliding)
        {
            continue;
        }
        float ph = frac(t);
        float start = lerp(0.25, 0.55, Rnd(run, 4u));
        float u = saturate((ph - start) / (1.0 - start));
        float hops = lerp(3.0, 7.0, Rnd(run, 5u));
        float prog = u - sin(TwoPi * hops * u) / (TwoPi * hops) * 0.9;
        float y0 = Rnd(run, 6u) * paneH * 0.6 - 10.0;
        float y = y0 + prog * (paneH * 1.15 - y0 + 20.0);
        float baseX = (col + 0.5 + (Rnd(run, 7u) - 0.5) * 0.5) * colW;
        float wobF = lerp(0.03, 0.07, Rnd(run, 8u));
        float wobA = lerp(1.0, 4.0, Rnd(run, 9u));
        float wobPhase = Rnd(run, 11u) * 6.28;
        float r = lerp(7.0, 13.0, Rnd(run, 10u)) * sizeMul;
        float2 c = float2(baseX + sin(y * wobF + wobPhase) * wobA, y);
        Consider(best, bestAlpha, p, c, r, 1.0, 0.72);

        // The trail above the drop: a clear streak through the fog, dotted with tiny beads.
        float pathX = baseX + sin(p.y * wobF + wobPhase) * wobA;
        float behind = y - p.y;
        if (behind > 0.0 && p.y > y0 - r)
        {
            float trailW = r * 0.55;
            float fade = exp(-behind / (paneH * 0.35));
            clear = max(clear, smoothstep(trailW, trailW * 0.4, abs(p.x - pathX)) * fade);
            float seg = 7.0 * sizeMul;
            float k = floor(p.y / seg);
            uint bid = Hash(run ^ (uint(int(k) + 4096) * 0x9e3779b9u));
            if (Rnd(bid, 1u) < 0.55 && (k + 0.5) * seg < y - r)
            {
                float2 bc = float2(pathX + (Rnd(bid, 2u) - 0.5) * r * 0.5, (k + 0.5) * seg);
                Consider(best, bestAlpha, p, bc, r * lerp(0.14, 0.32, Rnd(bid, 3u)), fade, 1.0);
            }
        }
    }

    float3 fogged = Scene(px, lerp(1.5, 6.0, fog)) * 0.9 + 0.08 * fog + 0.02;
    float fogAmt = smoothstep(0.0, 0.15, fog) * 0.92 * (1.0 - clear);
    float3 base = lerp(sharp.rgb, fogged, fogAmt);

    float3 glass = base;
    if (best.x < 1.0)
    {
        float d = best.x;
        float2 cs = ToScreen(pane, best.yz);
        float rPx = best.w * length(ToScreen(pane, best.yz + float2(1.0, 0.0)) - cs);
        float2 off = px - cs;
        float3 lens = Scene(cs - off * 2.6 + float2(0.0, -rPx * 1.2), 0.6);
        float2 nrm = (p - best.yz) / best.w;
        lens *= lerp(1.08, 0.55, smoothstep(0.55, 1.0, d));
        lens += smoothstep(0.32, 0.0, length(nrm - float2(-0.32, -0.42))) * 0.45;
        float aa = smoothstep(0.0, 1.0, (1.0 - d) * rPx / 1.2);
        glass = lerp(base, lens, aa * bestAlpha);
    }
    return float4(lerp(sharp.rgb, glass, m), sharp.a);
}
