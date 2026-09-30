using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace OLMSmootherYMM4;

/// <summary>
/// パス構成（MLAA）
///
///   Key     カラーキー（オフのときは通さない）
///   Edge    隣との色の差から輪郭を出す
///   Weight  輪郭を端まで辿って形を判定し、隣の色を混ぜる量を出す
///   Blend   その量だけ上下左右の色を混ぜる
/// </summary>
internal sealed class OLMSmootherProcessor : IVideoEffectProcessor
{
    readonly OLMSmootherEffect item;
    readonly Pass<KeyImpl> key;
    readonly Pass<EdgeImpl> edge;
    readonly Pass<WeightImpl> weight;
    readonly Pass<BlendImpl> blend;
    readonly ID2D1Image? keyOut, edgeOut, weightOut, blendOut;
    readonly bool enabled;

    ID2D1Image? input;

    public OLMSmootherProcessor(IGraphicsDevicesAndContext devices, OLMSmootherEffect item)
    {
        this.item = item;
        key = new(devices);
        edge = new(devices);
        weight = new(devices);
        blend = new(devices);

        // シェーダを読めない環境ではパススルーする
        enabled = key.IsEnabled && edge.IsEnabled && weight.IsEnabled && blend.IsEnabled;
        if (!enabled)
            return;

        keyOut = key.Output;
        edgeOut = edge.Output;
        weightOut = weight.Output;
        blendOut = blend.Output;
        weight.SetInput(0, edgeOut, true);
        blend.SetInput(1, weightOut, true);
    }

    public ID2D1Image Output => (enabled && input is not null ? blendOut : input)
        ?? throw new InvalidOperationException("入力が未設定です。");

    public void SetInput(ID2D1Image? image)
    {
        input = image;
        if (!enabled)
            return;
        key.SetInput(0, image, true);
        edge.SetInput(0, image, true);
        blend.SetInput(0, image, true);
    }

    public void ClearInput() => SetInput(null);

    public DrawDescription Update(EffectDescription effectDescription)
    {
        if (!enabled || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;

        // スムース範囲は 8bit の階調で指定する。+0.5 は 8bit 由来の値の丸め誤差を同じ色に含めるため。
        var range = Math.Clamp(item.SmoothRange.GetValue(frame, length, fps), 0d, 255d);
        var parameters = new Vector4((float)((range + 0.5) / 255d), 0, 0, 0);
        edge.Params = parameters;

        var color = input;
        if (item.UseColorKey)
        {
            var k = item.KeyColor;
            key.Key = new Vector4(k.R / 255f, k.G / 255f, k.B / 255f, 1f);
            key.Params = parameters;
            color = keyOut!;
        }
        edge.SetInput(0, color, true);
        blend.SetInput(0, color, true);

        return effectDescription.DrawDescription;
    }

    public void Dispose()
    {
        ClearInput();
        if (enabled)
        {
            weight.SetInput(0, null, true);
            blend.SetInput(1, null, true);
        }
        blendOut?.Dispose();
        weightOut?.Dispose();
        edgeOut?.Dispose();
        keyOut?.Dispose();
        blend.Dispose();
        weight.Dispose();
        edge.Dispose();
        key.Dispose();
    }
}
