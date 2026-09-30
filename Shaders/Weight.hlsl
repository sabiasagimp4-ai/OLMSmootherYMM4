// 混ぜる量の計算（MLAA の形状判定）。
//
// 輪郭を 1 本の直線区間として端まで辿り、両端で交わる短い輪郭（段差）がどちら側にあるかを見る。
// 段差の中点を頂点とし、区間の中央で境界線と交わる折れ線を本来の輪郭とみなす
// （Z 字・U 字・L 字のどれもこの 1 つの規則で表せる）。
// 折れ線と境界線に挟まれた面積が、その画素に隣の色を混ぜる量になる。
//
// 出力（この画素が受け持つ上と左の境界について）
//   r = 上の画素がこの画素の色をもらう量
//   g = この画素が上の画素の色をもらう量
//   b = 左の画素がこの画素の色をもらう量
//   a = この画素が左の画素の色をもらう量
#include "Common.hlsli"

bool2 EdgeAt(float4 uv, float2 p, float2 offset)
{
    return Load0(uv, p, offset).rg > 0.5;
}

// 区間の端の段差から、その端での折れ線の高さを決める。
// 隣の画素の側に段差があれば +0.5（輪郭が隣へはみ出す）、自分の側なら -0.5、
// 両側にある（十字路）か無いなら 0。
float EndHeight(bool neighborSide, bool ownSide)
{
    return neighborSide == ownSide ? 0 : (neighborSide ? 0.5 : -0.5);
}

// 長さ d の区間 [0, d] のうち、画素 [a, a + 1] の上で折れ線と境界線に挟まれた面積。
// 折れ線は (0, h0) -> (d/2, 0) -> (d, h1)。
// x = 隣の側の面積, y = 自分の側の面積。
float2 Area(float a, float d, float h0, float h1)
{
    float c = d * 0.5;
    float b = a + 1;
    float2 w = 0;

    float u1 = min(b, c);
    if (u1 > a)
    {
        float s = h0 * ((u1 - a) - (u1 * u1 - a * a) / (2 * c));
        w += s > 0 ? float2(s, 0) : float2(0, -s);
    }

    float v0 = max(a, c);
    if (b > v0)
    {
        float s = h1 * ((b * b - v0 * v0) * 0.5 - c * (b - v0)) / c;
        w += s > 0 ? float2(s, 0) : float2(0, -s);
    }
    return w;
}

// 上の境界（横向きの輪郭）。左右へ辿り、端の段差を上の行（隣側）と自分の行で調べる。
float2 TopEdge(float4 uv, float2 p)
{
    int left = 0;
    float h0 = 0;
    [loop]
    for (int i = 0; i < MAX_SEARCH; i++)
    {
        float2 b = float2(-i, 0); // 画素 p + b の左の境界
        bool up = EdgeAt(uv, p, b + float2(0, -1)).r;
        bool own = EdgeAt(uv, p, b).r;
        if (up || own)
        {
            h0 = EndHeight(up, own);
            break;
        }
        if (!EdgeAt(uv, p, b + float2(-1, 0)).g)
            break;
        left++;
    }

    int right = 0;
    float h1 = 0;
    [loop]
    for (int j = 0; j < MAX_SEARCH; j++)
    {
        float2 b = float2(j + 1, 0); // 画素 p + b の左の境界 = 区間の右端の候補
        bool up = EdgeAt(uv, p, b + float2(0, -1)).r;
        bool own = EdgeAt(uv, p, b).r;
        if (up || own)
        {
            h1 = EndHeight(up, own);
            break;
        }
        if (!EdgeAt(uv, p, b).g)
            break;
        right++;
    }

    return Area(left, left + right + 1, h0, h1);
}

// 左の境界（縦向きの輪郭）。TopEdge を転置したもの。隣側は左の列。
float2 LeftEdge(float4 uv, float2 p)
{
    int top = 0;
    float h0 = 0;
    [loop]
    for (int i = 0; i < MAX_SEARCH; i++)
    {
        float2 b = float2(0, -i); // 画素 p + b の上の境界
        bool lf = EdgeAt(uv, p, b + float2(-1, 0)).g;
        bool own = EdgeAt(uv, p, b).g;
        if (lf || own)
        {
            h0 = EndHeight(lf, own);
            break;
        }
        if (!EdgeAt(uv, p, b + float2(0, -1)).r)
            break;
        top++;
    }

    int bottom = 0;
    float h1 = 0;
    [loop]
    for (int j = 0; j < MAX_SEARCH; j++)
    {
        float2 b = float2(0, j + 1);
        bool lf = EdgeAt(uv, p, b + float2(-1, 0)).g;
        bool own = EdgeAt(uv, p, b).g;
        if (lf || own)
        {
            h1 = EndHeight(lf, own);
            break;
        }
        if (!EdgeAt(uv, p, b).r)
            break;
        bottom++;
    }

    return Area(top, top + bottom + 1, h0, h1);
}

float4 main(float4 pos : SV_POSITION, float4 posScene : SCENE_POSITION, float4 uv0 : TEXCOORD0) : SV_Target
{
    float2 p = posScene.xy;
    bool2 e = EdgeAt(uv0, p, 0);

    float2 top = 0;
    float2 left = 0;
    [branch]
    if (e.g)
        top = TopEdge(uv0, p);
    [branch]
    if (e.r)
        left = LeftEdge(uv0, p);
    return float4(top, left);
}
