using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL4;
using WydFormats;
using GlPixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;
using SysPixelFormat = System.Drawing.Imaging.PixelFormat;
using SysBuffer = System.Buffer;

namespace WydMapEditor;

public sealed class TexturePreviewCache : IDisposable
{
    private string _envFolder = "";
    private readonly Dictionary<string, int> _tex = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fail = new(StringComparer.OrdinalIgnoreCase);

    public void Configure(string envFolder)
    {
        if (string.Equals(_envFolder, envFolder, StringComparison.OrdinalIgnoreCase)) return;
        Clear();
        _envFolder = envFolder;
    }

    public int Get(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return 0;
        if (_tex.TryGetValue(baseName, out int t)) return t;
        if (_fail.Contains(baseName)) return 0;

        int tex = TryLoad(baseName);
        if (tex != 0) _tex[baseName] = tex;
        else _fail.Add(baseName);
        return tex;
    }

    public void Invalidate(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return;
        if (_tex.TryGetValue(baseName, out int t) && t != 0)
            GL.DeleteTexture(t);
        _tex.Remove(baseName);
        _fail.Remove(baseName);
    }

    public string? ResolvePath(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return null;
        if (string.IsNullOrWhiteSpace(_envFolder) || !Directory.Exists(_envFolder)) return null;
        return FindPath(baseName);
    }

    public (byte[] rgba, int width, int height, string path)? DecodeRgba(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return null;
        if (string.IsNullOrWhiteSpace(_envFolder) || !Directory.Exists(_envFolder)) return null;

        string? path = FindPath(baseName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".wys")
        {
            var res = WysLoader.Decode(path);
            if (res == null) return null;
            var (px, w, h) = res.Value;
            return (px, w, h, path);
        }
        if (ext == ".tga")
        {
            var (px, w, h) = ReadTgaPixels(path);
            return (px, w, h, path);
        }
        {
            var (px, w, h) = ReadBitmapPixels(path);
            return (px, w, h, path);
        }
    }

    private static readonly string[] s_exts = { ".wys", ".png", ".tga", ".jpg", ".jpeg", ".bmp" };

    private int TryLoad(string baseName)
    {
        if (string.IsNullOrWhiteSpace(_envFolder) || !Directory.Exists(_envFolder)) return 0;

        string? path = FindPath(baseName);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                byte[] pixels;
                int w, h;
                if (ext == ".wys")
                {
                    var res = WysLoader.Decode(path);
                    if (res == null) return 0;
                    (pixels, w, h) = res.Value;
                    return UploadGl(pixels, w, h, true);
                }

                if (ext == ".tga")
                {
                    (pixels, w, h) = ReadTgaPixels(path);
                    return UploadGl(pixels, w, h, true);
                }

                (pixels, w, h) = ReadBitmapPixels(path);
                return UploadGl(pixels, w, h, true);
            }
            catch
            {
                return 0;
            }
        }

        return 0;
    }

    private string? FindPath(string baseName)
    {
        var dirs = new[]
        {
            _envFolder,
            Path.Combine(_envFolder, "Tile"),
            Path.Combine(_envFolder, "tile"),
            Path.Combine(_envFolder, "Texture"),
            Path.Combine(_envFolder, "texture"),
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var ext in s_exts)
            {
                string p = Path.Combine(dir, baseName + ext);
                if (!File.Exists(p)) continue;
                return p;
            }
        }
        return null;
    }

    private static int UploadGl(byte[] data, int width, int height, bool flipY)
    {
        if (flipY) FlipY(data, width, height);

        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            width, height, 0, GlPixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    private static void FlipY(byte[] data, int width, int height)
    {
        int rowBytes = width * 4;
        var tmp = new byte[rowBytes];
        for (int y = 0; y < height / 2; y++)
        {
            int topOff = y * rowBytes;
            int botOff = (height - 1 - y) * rowBytes;
            SysBuffer.BlockCopy(data, topOff, tmp, 0, rowBytes);
            SysBuffer.BlockCopy(data, botOff, data, topOff, rowBytes);
            SysBuffer.BlockCopy(tmp, 0, data, botOff, rowBytes);
        }
    }

    private static (byte[] pixels, int w, int h) ReadBitmapPixels(string path)
    {
        using var bmp = new Bitmap(path);
        using var bmp32 = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), SysPixelFormat.Format32bppArgb);

        var bd = bmp32.LockBits(new Rectangle(0, 0, bmp32.Width, bmp32.Height),
            ImageLockMode.ReadOnly, SysPixelFormat.Format32bppArgb);
        int bytes = Math.Abs(bd.Stride) * bmp32.Height;
        var data = new byte[bytes];
        System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, data, 0, bytes);
        bmp32.UnlockBits(bd);

        for (int i = 0; i < data.Length; i += 4)
        {
            byte b = data[i];
            data[i] = data[i + 2];
            data[i + 2] = b;
        }

        return (data, bmp32.Width, bmp32.Height);
    }

    private static (byte[] pixels, int w, int h) ReadTgaPixels(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        int idLen = br.ReadByte();
        br.ReadByte();
        int imageType = br.ReadByte();
        br.ReadBytes(5);
        br.ReadInt16();
        br.ReadInt16();
        int width = br.ReadInt16();
        int height = br.ReadInt16();
        int depth = br.ReadByte();
        int desc = br.ReadByte();

        br.ReadBytes(idLen);

        bool topLeft = (desc & 0x20) != 0;
        bool hasAlpha = depth == 32;
        int ch = hasAlpha ? 4 : 3;

        var pixels = new byte[width * height * 4];

        switch (imageType)
        {
            case 2:
                TgaReadUncompressed(br, pixels, width, height, ch, topLeft);
                break;
            case 10:
                TgaReadRle(br, pixels, width, height, ch, topLeft);
                break;
            default:
                throw new NotSupportedException();
        }

        return (pixels, width, height);
    }

    private static void TgaReadUncompressed(BinaryReader br, byte[] pixels, int width, int height, int srcCh, bool topLeft)
    {
        for (int y = 0; y < height; y++)
        {
            int dstRow = topLeft ? y : height - 1 - y;
            for (int x = 0; x < width; x++)
                ReadTgaPixel(br, pixels, (dstRow * width + x) * 4, srcCh);
        }
    }

    private static void TgaReadRle(BinaryReader br, byte[] pixels, int width, int height, int srcCh, bool topLeft)
    {
        int total = width * height;
        int pixel = 0;
        var tmp = new byte[4];

        while (pixel < total)
        {
            byte rep = br.ReadByte();
            if ((rep & 0x80) != 0)
            {
                int count = (rep & 0x7F) + 1;
                ReadTgaPixel(br, tmp, 0, srcCh);
                for (int k = 0; k < count && pixel < total; k++, pixel++)
                {
                    int row = pixel / width;
                    int col = pixel % width;
                    int dstRow = topLeft ? row : height - 1 - row;
                    int off = (dstRow * width + col) * 4;
                    pixels[off] = tmp[0];
                    pixels[off + 1] = tmp[1];
                    pixels[off + 2] = tmp[2];
                    pixels[off + 3] = tmp[3];
                }
            }
            else
            {
                int count = (rep & 0x7F) + 1;
                for (int k = 0; k < count && pixel < total; k++, pixel++)
                {
                    int row = pixel / width;
                    int col = pixel % width;
                    int dstRow = topLeft ? row : height - 1 - row;
                    ReadTgaPixel(br, pixels, (dstRow * width + col) * 4, srcCh);
                }
            }
        }
    }

    private static void ReadTgaPixel(BinaryReader br, byte[] dst, int off, int srcCh)
    {
        byte b = br.ReadByte();
        byte g = br.ReadByte();
        byte r = br.ReadByte();
        byte a = srcCh == 4 ? br.ReadByte() : (byte)255;
        dst[off] = r;
        dst[off + 1] = g;
        dst[off + 2] = b;
        dst[off + 3] = a;
    }

    public void Clear()
    {
        foreach (var t in _tex.Values)
            if (t != 0) GL.DeleteTexture(t);
        _tex.Clear();
        _fail.Clear();
    }

    public void Dispose() => Clear();
}
