using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssetGen;

/// <summary>
/// Regenerates the app's image assets from XAML:
///   src/Soundboard.App/Assets/Logo.xaml  →  Assets/app.ico (16–256 px) and docs/logo.png
///   tools/AssetGen/Splash.xaml           →  Assets/splash.png
/// </summary>
public static class Program
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    [STAThread]
    public static int Main()
    {
        var root = FindRepoRoot();
        var assets = Path.Combine(root, "src", "Soundboard.App", "Assets");
        var docs = Path.Combine(root, "docs");
        Directory.CreateDirectory(docs);

        // StaticResource lookups in Splash.xaml fall back to application resources.
        var app = new Application();
        var logo = LoadXaml<ResourceDictionary>(Path.Combine(assets, "Logo.xaml"));
        app.Resources.MergedDictionaries.Add(logo);

        var full = (ImageSource)logo["LogoImage"];
        var small = (ImageSource)logo["LogoImageSmall"];

        var icon = Path.Combine(assets, "app.ico");
        WriteIco(icon, IconSizes.Select(size => RenderImage(size <= 24 ? small : full, size)).ToList());
        Console.WriteLine($"Wrote {icon}");

        var logoPng = Path.Combine(docs, "logo.png");
        SavePng(RenderImage(full, 256), logoPng);
        Console.WriteLine($"Wrote {logoPng}");

        var splash = LoadXaml<FrameworkElement>(Path.Combine(root, "tools", "AssetGen", "Splash.xaml"));
        var splashPng = Path.Combine(assets, "splash.png");
        SavePng(RenderElement(splash), splashPng);
        Console.WriteLine($"Wrote {splashPng}");
        return 0;
    }

    private static T LoadXaml<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return (T)XamlReader.Load(stream);
    }

    private static BitmapSource RenderImage(ImageSource image, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, size, size));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static BitmapSource RenderElement(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.Width, (int)element.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void SavePng(BitmapSource bitmap, string path) => File.WriteAllBytes(path, EncodePng(bitmap));

    /// <summary>
    /// Writes a Windows .ico. The 256 px entry is PNG-compressed; smaller ones are classic 32-bit DIBs,
    /// because several consumers (GDI+, some shell paths) can't decode small PNG entries.
    /// </summary>
    private static void WriteIco(string path, IReadOnlyList<BitmapSource> images)
    {
        var pngs = images.Select(i => i.PixelWidth >= 256 ? EncodePng(i) : EncodeIconDib(i)).ToList();
        using var writer = new BinaryWriter(File.Create(path));

        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)images.Count);

        int offset = 6 + 16 * images.Count;
        for (int i = 0; i < images.Count; i++)
        {
            int size = images[i].PixelWidth;
            writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);          // palette colors
            writer.Write((byte)0);          // reserved
            writer.Write((ushort)1);        // color planes
            writer.Write((ushort)32);       // bits per pixel
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs)
        {
            writer.Write(png);
        }
    }

    /// <summary>BITMAPINFOHEADER + bottom-up BGRA pixels + an empty AND mask (alpha does the masking).</summary>
    private static byte[] EncodeIconDib(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); // un-premultiply
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        int maskStride = ((w + 31) / 32) * 4;
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(40);                   // header size
        writer.Write(w);
        writer.Write(h * 2);                // XOR image + AND mask
        writer.Write((ushort)1);            // planes
        writer.Write((ushort)32);           // bpp
        writer.Write(0);                    // BI_RGB
        writer.Write(stride * h + maskStride * h);
        writer.Write(0); writer.Write(0);   // resolution
        writer.Write(0); writer.Write(0);   // palette
        for (int y = h - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * stride, stride);
        }
        writer.Write(new byte[maskStride * h]);
        writer.Flush();
        return ms.ToArray();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Soundboard.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find Soundboard.slnx above " + AppContext.BaseDirectory);
    }
}
