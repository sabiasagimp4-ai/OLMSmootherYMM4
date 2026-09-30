// 輪郭検出: 各画素が「左隣との境界」と「上隣との境界」を受け持つ。
// r = 左の境界が輪郭, g = 上の境界が輪郭。
// 差が Params.x 以下なら同じ色とみなす（スムース範囲）。グラデーションの段差を輪郭として拾わないため。
#include "Common.hlsli"

float Diff(float4 a, float4 b)
{
    float4 d = abs(a - b);
    return max(max(d.r, d.g), max(d.b, d.a));
}

// 画像の外は端の画素が続いているものとして読む。
// アイテムの矩形の縁は線画の輪郭ではないので、滑らかにしない（写真などの角が欠けないように）。
float4 LoadClamped(float4 uv, float2 pos, float2 offset)
{
    float2 q = clamp(pos + offset, Rect0.xy + 0.5, Rect0.zw - 0.5);
    return Input0.SampleLevel(Sampler0, uv.xy + uv.zw * (q - pos), 0);
}

float4 main(float4 pos : SV_POSITION, float4 posScene : SCENE_POSITION, float4 uv0 : TEXCOORD0) : SV_Target
{
    float2 p = posScene.xy;
    float4 c = LoadClamped(uv0, p, 0);
    float left = Diff(c, LoadClamped(uv0, p, float2(-1, 0))) > Params.x ? 1 : 0;
    float top = Diff(c, LoadClamped(uv0, p, float2(0, -1))) > Params.x ? 1 : 0;
    return float4(left, top, 0, 1);
}
