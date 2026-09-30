// カラーキー: キーの色と「同じ色」の画素を透明にする。スムージングより前に掛ける。
#include "Common.hlsli"

float4 main(float4 pos : SV_POSITION, float4 posScene : SCENE_POSITION, float4 uv0 : TEXCOORD0) : SV_Target
{
    float4 c = Input0.SampleLevel(Sampler0, uv0.xy, 0);
    if (c.a <= 0)
        return 0;
    float3 d = abs(c.rgb / c.a - Key.rgb);
    return max(d.r, max(d.g, d.b)) <= Params.x ? 0 : c;
}
