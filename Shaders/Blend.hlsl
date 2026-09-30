// 近傍との混合。入力 0 = 色, 入力 1 = Weight.hlsl の出力。
// 上下左右それぞれ「隣の色をもらう量」を集め、方向ごとに混ぜた色をその量で加重平均する
// （MLAA の Neighborhood Blending と同じ。量の合計が 1 を超えても色がはみ出さない）。
// 色はプリマルチプライドのまま混ぜるので、キーで抜いた背景との境目は半透明になる。
#include "Common.hlsli"

float4 main(float4 pos : SV_POSITION, float4 posScene : SCENE_POSITION, float4 uv0 : TEXCOORD0, float4 uv1 : TEXCOORD1) : SV_Target
{
    float2 p = posScene.xy;
    float4 c = Load0(uv0, p, 0);

    float4 own = Load1(uv1, p, 0);
    float up = own.g;
    float left = own.a;
    float down = Load1(uv1, p, float2(0, 1)).r;
    float right = Load1(uv1, p, float2(1, 0)).b;

    float sum = up + down + left + right;
    if (sum < 1e-5)
        return c;

    float4 acc = up * lerp(c, Load0(uv0, p, float2(0, -1)), up)
               + down * lerp(c, Load0(uv0, p, float2(0, 1)), down)
               + left * lerp(c, Load0(uv0, p, float2(-1, 0)), left)
               + right * lerp(c, Load0(uv0, p, float2(1, 0)), right);
    return acc / sum;
}
