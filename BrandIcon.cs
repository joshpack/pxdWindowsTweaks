using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace pxdWindowsTweaks;

/// <summary>
/// pxd Windows Tweaks' icon: three settings sliders with neon-green knobs on an indigo tile.
/// Drawn from vector shapes so it stays crisp at every size; used for the window and the exe's .ico.
/// </summary>
public static class BrandIcon
{
    public static Control Build(double size)
    {
        var a = new Canvas { Width = 100, Height = 100 };
        void Track(double y, double knob)
        {
            a.Children.Add(new ShapePath
            {
                Data = Geometry.Parse(FormattableString.Invariant($"M16,{y} L84,{y}")),
                Stroke = new SolidColorBrush(Color.Parse("#2A2470")), StrokeThickness = 9, StrokeLineCap = PenLineCap.Round,
            });
            a.Children.Add(new ShapePath
            {
                Data = Geometry.Parse(FormattableString.Invariant($"M16,{y} L{knob},{y}")),
                Stroke = new SolidColorBrush(Color.Parse("#E6E9FF")), StrokeThickness = 9, StrokeLineCap = PenLineCap.Round,
            });
            a.Children.Add(new ShapePath
            {
                Data = new EllipseGeometry(new Rect(knob - 11, y - 11, 22, 22)),
                Fill = new SolidColorBrush(Color.Parse("#3DFF9A")),
                Stroke = new SolidColorBrush(Color.Parse("#0B0F1A")), StrokeThickness = 4,
            });
        }

        Track(26, 62);
        Track(50, 34);
        Track(74, 74);
        return Tile(size, "#8C7CFF", "#3B1FC2", a, 0.13);
    }

    public static RenderTargetBitmap Render(int size)
    {
        var c = Build(size);
        c.Measure(new Size(size, size));
        c.Arrange(new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(c);
        return bitmap;
    }

    public static WindowIcon Window(int size = 64) => new(Render(size));

    /// <summary>Writes a multi-size .ico (PNG entries) for the exe, or a 256 px PNG when the path ends in .png.</summary>
    public static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            using var png = File.Create(path);
            Render(256).Save(png, PngBitmapEncoderOptions.Default);
            return;
        }

        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var images = sizes.Select(s =>
        {
            using var ms = new MemoryStream();
            Render(s).Save(ms, PngBitmapEncoderOptions.Default);
            return ms.ToArray();
        }).ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(b);
            w.Write(b);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }

        foreach (var img in images) w.Write(img);
    }

    private static Grid Tile(double size, string from, string to, Canvas art, double margin)
    {
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.22),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
            },
        });
        g.Children.Add(new Viewbox { Margin = new Thickness(size * margin), Child = art });
        return g;
    }
}
