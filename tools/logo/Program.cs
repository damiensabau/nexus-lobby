// Logo lumineux sur fond noir -> PNG transparent (lueur conservée) + .ico multi-tailles.
// Usage : logo-tool <source.png> <dossier-sortie>
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

var source = args[0];
var outDir = args[1];
Directory.CreateDirectory(outDir);

using var original = new Bitmap(source);
using var transparent = UnmultiplyBlack(original);
var bounds = ContentBounds(transparent, alphaThreshold: 10);
Console.WriteLine($"contenu : {bounds}");

// Carré centré sur le contenu, avec une marge pour ne pas couper la lueur.
var side = (int)(Math.Max(bounds.Width, bounds.Height) * 1.10);
var square = new Rectangle(bounds.X + bounds.Width / 2 - side / 2, bounds.Y + bounds.Height / 2 - side / 2, side, side);
using var cropped = Crop(transparent, square);

foreach (var size in new[] { 1024, 512, 256 })
{
    using var resized = Resize(cropped, size);
    resized.Save(Path.Combine(outDir, $"logo-{size}.png"), ImageFormat.Png);
}

// Icône : versions PNG dans le .ico (pris en charge depuis Windows Vista), de 256 à 16 px.
var iconSizes = new[] { 256, 128, 64, 48, 32, 24, 16 };
var frames = iconSizes.Select(s =>
{
    using var bmp = Resize(cropped, s);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return (Size: s, Png: ms.ToArray());
}).ToList();
WriteIco(Path.Combine(outDir, "app.ico"), frames);
Console.WriteLine($"écrit : logo-1024/512/256.png + app.ico ({string.Join(", ", iconSizes)} px)");

// ── Fond noir -> transparence : alpha = luminosité max, couleur « dé-prémultipliée » ──
static Bitmap UnmultiplyBlack(Bitmap src)
{
    var w = src.Width; var h = src.Height;
    var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    var rect = new Rectangle(0, 0, w, h);
    var s = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var d = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    var buf = new byte[s.Stride * h];
    Marshal.Copy(s.Scan0, buf, 0, buf.Length);
    for (var i = 0; i < buf.Length; i += 4)
    {
        int b = buf[i], g = buf[i + 1], r = buf[i + 2];
        var m = Math.Max(r, Math.Max(g, b));
        // Seuil bas : le « noir » de l'image n'est pas parfaitement 0 (compression) -> entièrement transparent.
        const int floor = 6;
        var a = m <= floor ? 0 : (int)Math.Round((m - floor) * 255.0 / (255 - floor));
        if (a == 0) { buf[i] = buf[i + 1] = buf[i + 2] = buf[i + 3] = 0; continue; }
        buf[i] = (byte)Math.Min(255, b * 255 / m);
        buf[i + 1] = (byte)Math.Min(255, g * 255 / m);
        buf[i + 2] = (byte)Math.Min(255, r * 255 / m);
        buf[i + 3] = (byte)a;
    }
    Marshal.Copy(buf, 0, d.Scan0, buf.Length);
    src.UnlockBits(s); dst.UnlockBits(d);
    return dst;
}

static Rectangle ContentBounds(Bitmap bmp, int alphaThreshold)
{
    int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var buf = new byte[data.Stride * bmp.Height];
    Marshal.Copy(data.Scan0, buf, 0, buf.Length);
    bmp.UnlockBits(data);
    for (var y = 0; y < bmp.Height; y++)
    for (var x = 0; x < bmp.Width; x++)
    {
        if (buf[y * data.Stride + x * 4 + 3] <= alphaThreshold) continue;
        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
    }
    return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
}

static Bitmap Crop(Bitmap src, Rectangle area)
{
    var dst = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dst);
    g.Clear(Color.Transparent);
    g.DrawImage(src, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);
    return dst;
}

static Bitmap Resize(Bitmap src, int size)
{
    var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dst);
    g.Clear(Color.Transparent);
    g.CompositingMode = CompositingMode.SourceOver;
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    using var attrs = new ImageAttributes();
    attrs.SetWrapMode(WrapMode.TileFlipXY); // pas de liseré sombre sur les bords
    g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
    return dst;
}

static void WriteIco(string path, List<(int Size, byte[] Png)> frames)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count);    // ICONDIR
    var offset = 6 + 16 * frames.Count;
    foreach (var f in frames)                                             // ICONDIRENTRY
    {
        w.Write((byte)(f.Size >= 256 ? 0 : f.Size));
        w.Write((byte)(f.Size >= 256 ? 0 : f.Size));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((short)1); w.Write((short)32);
        w.Write(f.Png.Length); w.Write(offset);
        offset += f.Png.Length;
    }
    foreach (var f in frames) w.Write(f.Png);
}
