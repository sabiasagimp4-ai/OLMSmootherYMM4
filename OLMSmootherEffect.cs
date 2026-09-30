using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace OLMSmootherYMM4;

/// <summary>
/// アニメの線画向けのスムージング。二値化した線などのジャギーを、ぼかさずに滑らかにする。
/// OLM Digital の OLM Smoother（After Effects / Nuke 用）と同じ考え方（MLAA）の独自実装。
/// </summary>
[VideoEffect("OLM Smoother", ["加工"],
    ["OLM Smoother", "OLMSmoother", "スムージング", "スムーザー", "アンチエイリアス", "ジャギー", "MLAA", "線画", "アニメ"],
    IsAviUtlSupported = false)]
public sealed class OLMSmootherEffect : VideoEffectBase
{
    public override string Label => "OLM Smoother";

    bool useColorKey;
    Color keyColor = Colors.White;

    [Display(GroupName = "OLM Smoother", Name = "カラーキーを使う", Description = "Use Color Key\nオンにすると、スムージングの前に「キーの色」を透明にします。抜いた背景との境目は半透明で滑らかになります。", Order = 0)]
    [ToggleSlider]
    public bool UseColorKey { get => useColorKey; set => Set(ref useColorKey, value); }

    [Display(GroupName = "OLM Smoother", Name = "キーの色", Description = "Color Key\n「カラーキーを使う」がオンのときに透明にする色です。", Order = 1)]
    [ColorPicker]
    public Color KeyColor { get => keyColor; set => Set(ref keyColor, value); }

    [Display(GroupName = "OLM Smoother", Name = "スムース範囲", Description = "Do Smooth Range\n色の差（0〜255 階調）がこの値以下なら同じ色とみなします。グラデーションのある画像で線が滑らかにならないときに上げてください。キーの色の許容範囲にも使います。", Order = 2)]
    [AnimationSlider("F0", "", 0, 32)]
    public Animation SmoothRange { get; } = new(0, 0, 255);

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new OLMSmootherProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables() => [SmoothRange];
}
