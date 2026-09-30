using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace OLMSmootherYMM4;

/// <summary>全パス共通の定数バッファ。Shaders/Common.hlsli の cbuffer と 1:1。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PassConstants
{
    public Vector4 Rect0;
    public Vector4 Rect1;
    public Vector4 Key;
    public Vector4 Params;
}

/// <summary>1 本のピクセルシェーダを包むエフェクト。定数は Key と Params だけを外から渡す。</summary>
internal sealed class Pass<TImpl>(IGraphicsDevicesAndContext devices)
    : D2D1CustomShaderEffectBase(Create<TImpl>(devices))
    where TImpl : ID2D1EffectImpl, new()
{
    public Vector4 Key { set => SetValue(PassImpl.KeyIndex, value); }
    public Vector4 Params { set => SetValue(PassImpl.ParamsIndex, value); }
}

internal static class PassImpl
{
    public const int KeyIndex = 0;
    public const int ParamsIndex = 1;

    /// <summary>輪郭の端を探す最大距離 [px]。Shaders/Common.hlsli の MAX_SEARCH と一致させること。</summary>
    public const int MaxSearch = 64;
}

internal abstract class PassImpl<T>(string shaderName, int margin)
    : D2D1CustomShaderEffectImplBase<T>(LoadShader(shaderName))
    where T : PassImpl<T>
{
    // 入力範囲が届く前に描かれても真っ黒にならないよう、初期値は「どこでも範囲内」にしておく。
    static readonly Vector4 Unbounded = new(-1e9f, -1e9f, 1e9f, 1e9f);

    protected PassConstants constants = new() { Rect0 = Unbounded, Rect1 = Unbounded };

    protected override void UpdateConstants() => drawInformation?.SetPixelShaderConstantBuffer(constants);

    public override void MapInputRectsToOutputRect(RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
    {
        // シェーダは入力の外を読まない（透明か端の画素として扱う）。そのために入力の範囲をここで渡す。
        constants.Rect0 = ToVector(inputRects[0]);
        if (inputRects.Length > 1)
            constants.Rect1 = ToVector(inputRects[1]);
        UpdateConstants();

        // 画像の縁は輪郭として扱わないので、どのパスも出力は入力と同じ範囲になる
        outputRect = inputRects[0];
        outputOpaqueSubRect = default;
    }

    public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
    {
        // 近傍として読む距離だけ広げる。無限大の矩形でも桁あふれしないようにする
        static int Shift(int value, long delta) => (int)Math.Clamp(value + delta, int.MinValue / 2, int.MaxValue / 2);
        var rect = new RawRect(Shift(outputRect.Left, -margin), Shift(outputRect.Top, -margin), Shift(outputRect.Right, margin), Shift(outputRect.Bottom, margin));
        for (var i = 0; i < inputRects.Length; i++)
            inputRects[i] = rect;
    }

    static Vector4 ToVector(RawRect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    static byte[] LoadShader(string name)
    {
        using var stream = typeof(PassImpl).Assembly.GetManifestResourceStream($"OLMSmootherYMM4.{name}.cso")
            ?? throw new InvalidOperationException($"シェーダー リソースがありません: {name}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}

// D2D のプロパティは具象クラスに宣言する。Vortice の登録は DeclaredProperties しか見ないので、
// 基底クラスに置いたプロパティは存在しない扱いになり SetValue が失敗する。

/// <summary>カラーキー。</summary>
[CustomEffect(1)]
internal sealed class KeyImpl() : PassImpl<KeyImpl>("Key", 0)
{
    [CustomEffectProperty(PropertyType.Vector4, PassImpl.KeyIndex)]
    public Vector4 Key { get => constants.Key; set { constants.Key = value; UpdateConstants(); } }

    [CustomEffectProperty(PropertyType.Vector4, PassImpl.ParamsIndex)]
    public Vector4 Params { get => constants.Params; set { constants.Params = value; UpdateConstants(); } }
}

/// <summary>輪郭検出。左と上の隣を読む。</summary>
[CustomEffect(1)]
internal sealed class EdgeImpl() : PassImpl<EdgeImpl>("Edge", 1)
{
    // Key は使わないが、Params の番号を KeyImpl と揃えるために置いておく
    [CustomEffectProperty(PropertyType.Vector4, PassImpl.KeyIndex)]
    public Vector4 Key { get => constants.Key; set { constants.Key = value; UpdateConstants(); } }

    [CustomEffectProperty(PropertyType.Vector4, PassImpl.ParamsIndex)]
    public Vector4 Params { get => constants.Params; set { constants.Params = value; UpdateConstants(); } }
}

/// <summary>混ぜる量の計算。輪郭を上下左右に MaxSearch 画素まで辿る。</summary>
[CustomEffect(1)]
internal sealed class WeightImpl() : PassImpl<WeightImpl>("Weight", PassImpl.MaxSearch + 2);

/// <summary>近傍との混合。上下左右の隣を読む。</summary>
[CustomEffect(2)]
internal sealed class BlendImpl() : PassImpl<BlendImpl>("Blend", 1);
