using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows.Media.Imaging;
using OLMSmootherYMM4;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using WpfColor = System.Windows.Media.Color;

namespace RenderCheck;

static class Program
{
    static int Main(string[] args)
    {
        var ymm4 = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("YMM4DirPath");
        if (string.IsNullOrEmpty(ymm4) || !File.Exists(Path.Combine(ymm4, "YukkuriMovieMaker.Plugin.dll")))
        {
            Console.Error.WriteLine("usage: RenderCheck <YMM4 のフォルダ> [PNG の出力先]");
            return 2;
        }
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(ymm4, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        return Checks.Run(args.Length > 1 ? args[1] : null);
    }
}

static class Checks
{
    const float Tolerance = 3f / 255f;
    static readonly Vector4 White = Vector4.One;
    static readonly Vector4 Black = new(0, 0, 0, 1);
    static int failures;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string? pngDir)
    {
        using var graphics = new GraphicsDevices();
        using var devices = graphics.CreateContext();

        // 段差つきの長方形。手計算: (10,10) は左と上の U 字の端で、白を 0.45 ずつもらう -> 0.45
        var step = Image(40, 40, White, (x, y) => (x >= 10 && x < 30 && y >= 10 && y < 20) || (x >= 20 && x < 30 && y == 9) ? Black : null);
        var stepOut = Compare(devices, "step", step, false, 0);
        Expect("step (10,10) の色", stepOut[10, 10].X, 0.45f);
        Expect("step (10,10) の不透明度", stepOut[10, 10].W, 1f);

        // YMM4 では入力は別のエフェクトの出力で、原点が (0,0) とは限らない。平行移動しても結果が同じになること
        var movedOut = Compare(devices, "step 原点 (-21,-17)", step, false, 0, -21, -17);
        Expect("step 原点 (-21,-17) で原点の結果との最大差", MaxDiff(movedOut, stepOut, 0, 0, 40, 40), 0f);
        Expect("step 原点 (-21,-17) の (10,10) の色", movedOut[10, 10].X, 0.45f);

        // 円と、傾き 1/3 の 1px 線
        var disc = Image(64, 64, White, (x, y) =>
            (x - 32) * (x - 32) + (y - 30) * (y - 30) < 18 * 18 || (y == 55 + x / 3 - 10 && x < 40) ? Black : null);
        var discOut = Compare(devices, "disc", disc, false, 0);
        Save(pngDir, "disc", disc, discOut);

        // 白をキーで抜く。背景は透明、輪郭は半透明の黒になるはず
        var keyedOut = Compare(devices, "keyed", disc, true, 0);
        Expect("keyed 背景の不透明度", keyedOut[3, 3].W, 0f);
        Expect("keyed step と同じ形の角の不透明度", Compare(devices, "keyed-step", step, true, 0)[10, 10].W, 0.55f);
        Save(pngDir, "keyed", disc, keyedOut);

        // 縦のグラデーションの上の円。範囲 6 なら背景の段差は輪郭にならない
        var gradient = Image(64, 64, White, (x, y) =>
            (x - 32) * (x - 32) + (y - 30) * (y - 30) < 18 * 18 ? Black : null, y => new Vector4(0.4f + y / 255f, 0.6f + y / 255f, 1f, 1f));
        var grad0 = Compare(devices, "gradient range=0", gradient, false, 0);
        var grad6 = Compare(devices, "gradient range=6", gradient, false, 6);
        Expect("gradient range=6 で背景が変わらない", MaxDiff(grad6, gradient, 3, 3, 10, 60), 0f);
        Console.WriteLine($"   (参考) range=0 の背景の最大変化 {MaxDiff(grad0, gradient, 3, 3, 10, 60) * 255:F1} 階調");
        Save(pngDir, "gradient0", gradient, grad0);
        Save(pngDir, "gradient6", gradient, grad6);

        // 背景が透明なアルファつきの図形
        var alpha = Image(48, 48, Vector4.Zero, (x, y) => Math.Abs(x - 24) + Math.Abs(y - 24) / 2 < 12 ? new Vector4(0.8f, 0.2f, 0.1f, 1f) : null);
        Compare(devices, "alpha", alpha, false, 0);

        Console.WriteLine(failures == 0 ? "OK" : $"{failures} 件失敗");
        return failures == 0 ? 0 : 1;
    }

    static Vector4[,] Compare(IGraphicsDevicesAndContext devices, string name, Vector4[,] src, bool useKey, double range, int originX = 0, int originY = 0)
    {
        var gpu = Gpu.Run(devices, src, useKey, range, originX, originY, out var bounds);
        var cpu = Reference.Run(src, useKey, new Vector3(1, 1, 1), range);
        int w = src.GetLength(0), h = src.GetLength(1);
        if (bounds.Left != originX || bounds.Top != originY || bounds.Right != originX + w || bounds.Bottom != originY + h)
            Fail($"{name}: 出力範囲 {bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} (期待 {originX},{originY},{originX + w},{originY + h})");

        float worst = 0;
        int changed = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                worst = Math.Max(worst, Max(Vector4.Abs(gpu[x, y] - cpu[x, y])));
                if (Max(Vector4.Abs(gpu[x, y] - src[x, y])) > Tolerance)
                    changed++;
            }
        var ok = worst <= Tolerance;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: 参照実装との最大差 {worst * 255:F2} 階調, 変化した画素 {changed}");
        if (!ok)
            failures++;
        return gpu;
    }

    static void Expect(string name, float actual, float expected)
    {
        var ok = Math.Abs(actual - expected) <= Tolerance;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {actual:F3} (期待 {expected:F3})");
        if (!ok)
            failures++;
    }

    static void Fail(string message)
    {
        Console.WriteLine("FAIL " + message);
        failures++;
    }

    static float Max(Vector4 v) => Math.Max(Math.Max(v.X, v.Y), Math.Max(v.Z, v.W));

    static float MaxDiff(Vector4[,] output, Vector4[,] src, int x0, int y0, int x1, int y1)
    {
        float m = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                m = Math.Max(m, Max(Vector4.Abs(output[x, y] - src[x, y])));
        return m;
    }

    static Vector4[,] Image(int w, int h, Vector4 background, Func<int, int, Vector4?> shape, Func<int, Vector4>? backgroundRow = null)
    {
        var image = new Vector4[w, h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                image[x, y] = Quantize(shape(x, y) ?? backgroundRow?.Invoke(y) ?? background);
        return image;
    }

    // 入力は 8bit で GPU に渡すので、参照実装にも同じ値を渡す
    static Vector4 Quantize(Vector4 c) => new Vector4(Gpu.ToByte(c.X), Gpu.ToByte(c.Y), Gpu.ToByte(c.Z), Gpu.ToByte(c.W)) / 255f;

    /// <summary>左に元画像、右に結果を 8 倍で並べて、市松模様の上に描いた PNG を書き出す。</summary>
    static void Save(string? dir, string name, Vector4[,] src, Vector4[,] output)
    {
        if (dir is null)
            return;
        const int s = 8;
        int w = src.GetLength(0), h = src.GetLength(1);
        var pixels = new byte[w * 2 * s * h * s * 4];
        for (var y = 0; y < h * s; y++)
            for (var x = 0; x < w * 2 * s; x++)
            {
                int px = x / s % w, py = y / s;
                var c = x < w * s ? src[px, py] : output[px, py];
                var checker = (x / 16 + y / 16) % 2 == 0 ? 0.8f : 0.6f;
                var i = (y * w * 2 * s + x) * 4;
                pixels[i + 0] = Gpu.ToByte(c.Z + checker * (1 - c.W));
                pixels[i + 1] = Gpu.ToByte(c.Y + checker * (1 - c.W));
                pixels[i + 2] = Gpu.ToByte(c.X + checker * (1 - c.W));
                pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(w * 2 * s, h * s, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, w * 2 * s * 4);
        Directory.CreateDirectory(dir);
        using var file = File.Create(Path.Combine(dir, name + ".png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(file);
    }
}

/// <summary>プラグインを実際の Direct2D で動かす。</summary>
static class Gpu
{
    static readonly Vortice.DCommon.PixelFormat Format8 = new(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);

    public static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);

    /// <summary>プリマルチプライドの RGBA を入れて、同じ大きさの出力を返す。</summary>
    public static Vector4[,] Run(IGraphicsDevicesAndContext devices, Vector4[,] src, bool useKey, double range, int originX, int originY, out Vortice.RawRectF bounds)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dc = devices.DeviceContext;

        var bytes = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = src[x, y];
                var i = (y * w + x) * 4;
                bytes[i] = ToByte(c.Z);
                bytes[i + 1] = ToByte(c.Y);
                bytes[i + 2] = ToByte(c.X);
                bytes[i + 3] = ToByte(c.W);
            }
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        using var input = dc.CreateBitmap(new SizeI(w, h), pin.AddrOfPinnedObject(), w * 4, new BitmapProperties1(Format8));
        pin.Free();

        var effect = new OLMSmootherEffect { UseColorKey = useKey, KeyColor = WpfColor.FromRgb(255, 255, 255) };
        effect.SmoothRange.AddToEachValues(range);
        using var processor = effect.CreateVideoEffect(devices);
        // 原点をずらすときは組み込みの AffineTransform2D を通す（入力がビットマップではなくエフェクトの出力になる）
        using var moved = new Vortice.Direct2D1.Effects.AffineTransform2D(dc)
        {
            TransformMatrix = Matrix3x2.CreateTranslation(originX, originY),
            InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor,
        };
        moved.SetInput(0, input, true);
        using var movedOut = moved.Output;
        processor.SetInput(originX == 0 && originY == 0 ? input : movedOut);
        processor.Update(Description());
        var output = processor.Output;
        bounds = dc.GetImageLocalBounds(output);

        var size = new SizeI(w, h);
        using var target = dc.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(Format8, 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        dc.Target = target;
        dc.BeginDraw();
        dc.Transform = Matrix3x2.Identity;
        dc.Clear(new Color4(0, 0, 0, 0));
        dc.DrawImage(output, new Vector2(-originX, -originY));
        dc.EndDraw();
        dc.Target = null;

        using var readback = dc.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(Format8, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        readback.CopyFromBitmap(target);
        var map = readback.Map(MapOptions.Read);
        var result = new Vector4[w, h];
        var row = new byte[size.Width * 4];
        for (var y = 0; y < size.Height; y++)
        {
            Marshal.Copy(map.Bits + y * map.Pitch, row, 0, row.Length);
            for (var x = 0; x < size.Width; x++)
                result[x, y] = new Vector4(row[x * 4 + 2], row[x * 4 + 1], row[x * 4], row[x * 4 + 3]) / 255f;
        }
        readback.Unmap();
        processor.ClearInput();
        return result;
    }

    static EffectDescription Description()
    {
        var timeline = new TimelineSourceDescription(new System.Drawing.Size(1920, 1080), new YukkuriMovieMaker.Player.Video.FrameTime(0, 60), new YukkuriMovieMaker.Player.Video.FrameTime(60, 60), 60, TimelineSourceUsage.Paused, Guid.Empty, []);
        var item = new TimelineItemSourceDescription(timeline, 0, 60, 1);
        var draw = new DrawDescription(Vector3.Zero, Vector2.Zero, Vector2.One, Vector3.Zero, Matrix4x4.Identity, InterpolationMode.Linear, 1, false, []);
        return new EffectDescription(item, draw, 0, 1, 0, 1);
    }
}

/// <summary>
/// シェーダと同じ計算を CPU で行う参照実装。座標の約束も同じにする:
/// 輪郭検出では画像の外を端の画素の延長として読む（縁は輪郭にならない）。それ以外で範囲外は 0。
/// </summary>
static class Reference
{
    const int MaxSearch = 64;

    public static Vector4[,] Run(Vector4[,] src, bool useKey, Vector3 key, double range)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var t = (float)((range + 0.5) / 255);

        Vector4 Color(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return Vector4.Zero;
            var c = src[x, y];
            if (useKey && c.W > 0)
            {
                var d = Vector3.Abs(new Vector3(c.X, c.Y, c.Z) / c.W - key);
                if (Math.Max(d.X, Math.Max(d.Y, d.Z)) <= t)
                    return Vector4.Zero;
            }
            return c;
        }

        static float Diff(Vector4 a, Vector4 b)
        {
            var d = Vector4.Abs(a - b);
            return Math.Max(Math.Max(d.X, d.Y), Math.Max(d.Z, d.W));
        }

        Vector4 Clamped(int x, int y) => Color(Math.Clamp(x, 0, w - 1), Math.Clamp(y, 0, h - 1));
        var left = new bool[w, h];
        var top = new bool[w, h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                left[x, y] = Diff(Clamped(x, y), Clamped(x - 1, y)) > t;
                top[x, y] = Diff(Clamped(x, y), Clamped(x, y - 1)) > t;
            }
        bool L(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && left[x, y];
        bool T(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && top[x, y];

        static float EndHeight(bool neighbor, bool own) => neighbor == own ? 0 : neighbor ? 0.5f : -0.5f;

        // 折れ線 (0,h0) -> (d/2,0) -> (d,h1) と境界線の間の面積を、画素 [a,a+1] の範囲で数値積分する。
        // シェーダは解析解なので、ここは別の方法で同じ量を求める。
        static (float neighbor, float own) Area(int a, int d, float h0, float h1)
        {
            const int n = 4096;
            float c = d / 2f, pos = 0, neg = 0;
            for (var i = 0; i < n; i++)
            {
                var x = a + (i + 0.5f) / n;
                var hx = x < c ? h0 * (1 - x / c) : h1 * (x - c) / c;
                if (hx > 0) pos += hx / n;
                else neg -= hx / n;
            }
            return (pos, neg);
        }

        var weights = new Vector4[w, h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var wt = Vector4.Zero;
                if (top[x, y])
                {
                    int l = 0, r = 0;
                    float h0 = 0, h1 = 0;
                    for (var i = 0; i < MaxSearch; i++, l++)
                    {
                        bool up = L(x - i, y - 1), own = L(x - i, y);
                        if (up || own) { h0 = EndHeight(up, own); break; }
                        if (!T(x - i - 1, y)) break;
                    }
                    for (var i = 0; i < MaxSearch; i++, r++)
                    {
                        bool up = L(x + i + 1, y - 1), own = L(x + i + 1, y);
                        if (up || own) { h1 = EndHeight(up, own); break; }
                        if (!T(x + i + 1, y)) break;
                    }
                    var (nb, ow) = Area(l, l + r + 1, h0, h1);
                    wt.X = nb;
                    wt.Y = ow;
                }
                if (left[x, y])
                {
                    int u = 0, d = 0;
                    float h0 = 0, h1 = 0;
                    for (var i = 0; i < MaxSearch; i++, u++)
                    {
                        bool lf = T(x - 1, y - i), own = T(x, y - i);
                        if (lf || own) { h0 = EndHeight(lf, own); break; }
                        if (!L(x, y - i - 1)) break;
                    }
                    for (var i = 0; i < MaxSearch; i++, d++)
                    {
                        bool lf = T(x - 1, y + i + 1), own = T(x, y + i + 1);
                        if (lf || own) { h1 = EndHeight(lf, own); break; }
                        if (!L(x, y + i + 1)) break;
                    }
                    var (nb, ow) = Area(u, u + d + 1, h0, h1);
                    wt.Z = nb;
                    wt.W = ow;
                }
                weights[x, y] = wt;
            }
        Vector4 W(int x, int y) => x >= 0 && y >= 0 && x < w && y < h ? weights[x, y] : Vector4.Zero;

        var output = new Vector4[w, h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = Color(x, y);
                float up = W(x, y).Y, lf = W(x, y).W, dn = W(x, y + 1).X, rt = W(x + 1, y).Z;
                var sum = up + dn + lf + rt;
                output[x, y] = sum < 1e-5f ? c :
                    (up * Vector4.Lerp(c, Color(x, y - 1), up)
                   + dn * Vector4.Lerp(c, Color(x, y + 1), dn)
                   + lf * Vector4.Lerp(c, Color(x - 1, y), lf)
                   + rt * Vector4.Lerp(c, Color(x + 1, y), rt)) / sum;
            }
        return output;
    }
}
