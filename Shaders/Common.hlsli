// 全パス共通の定数と読み出し。C# 側 PassConstants（SmootherPasses.cs）と 1:1 で対応する。

#ifndef OLM_SMOOTHER_COMMON
#define OLM_SMOOTHER_COMMON

// 輪郭の端を探す最大距離 [px]。SmootherPasses.cs の MaxSearch と一致させること。
#define MAX_SEARCH 64

cbuffer Constants : register(b0)
{
    float4 Rect0;   // 入力 0 の有効範囲 (left, top, right, bottom)。外側は透明として扱う
    float4 Rect1;   // 入力 1 の有効範囲
    float4 Key;     // rgb = キーの色（ストレートアルファ）
    float4 Params;  // x = 同じ色とみなす差の上限 (0..1)
};

Texture2D<float4> Input0 : register(t0);
SamplerState Sampler0 : register(s0);
Texture2D<float4> Input1 : register(t1);
SamplerState Sampler1 : register(s1);

bool Inside(float2 pos, float4 rect)
{
    return all(pos >= rect.xy) && all(pos < rect.zw);
}

// uv.zw は 1 画素ぶんのテクスチャ座標（d2d1effecthelpers.hlsli の D2DSampleInputAtOffset と同じ）。
// 探索ループの中で使うので Sample ではなく SampleLevel で読む。
// 入力の外は D2D が何を返すか保証されないので、範囲外は透明にする。
float4 Load0(float4 uv, float2 pos, float2 offset)
{
    return Inside(pos + offset, Rect0) ? Input0.SampleLevel(Sampler0, uv.xy + uv.zw * offset, 0) : 0;
}

float4 Load1(float4 uv, float2 pos, float2 offset)
{
    return Inside(pos + offset, Rect1) ? Input1.SampleLevel(Sampler1, uv.xy + uv.zw * offset, 0) : 0;
}

#endif
