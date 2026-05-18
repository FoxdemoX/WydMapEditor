using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL4;
using WydFormats;

// Resolve ambiguidades com System.Drawing.Imaging e System.Buffer
using GlPixelFormat  = OpenTK.Graphics.OpenGL4.PixelFormat;
using SysPixelFormat = System.Drawing.Imaging.PixelFormat;
using SysBuffer      = System.Buffer;

namespace WydMapEditor;

/// <summary>
/// Cache de texturas OpenGL para tiles do WYD.
/// Tenta carregar PNG, depois TGA (uncompressed e RLE).
/// As texturas são carregadas lazy (só quando solicitadas).
/// </summary>
public sealed class TileTextureCache : IDisposable
{
    // ── Cache ────────────────────────────────────────────────────────────────
    private readonly Dictionary<int, int>  _textures  = new();  // tileIndex → GL tex ID
    private readonly HashSet<int>          _failed    = new();  // tileIndex que não carregou
    private readonly Dictionary<int, (byte[] rgba, int w, int h)> _pixelData = new(); // raw RGBA pixels

    // ── Configuração ─────────────────────────────────────────────────────────
    private string                  _envFolder  = "";
    private Dictionary<int, string> _tileNames  = new();

    // ── Configurar (chame após carregar mapa) ───────────────────────────────
    public void Configure(string envFolder, Dictionary<int, string> tileNames)
    {
        if (_envFolder == envFolder && ReferenceEquals(_tileNames, tileNames)) return;
        Clear();
        _envFolder = envFolder;
        _tileNames = tileNames;
    }

    /// <summary>
    /// Sobrecarga que carrega os nomes de tile diretamente do arquivo de texturas do env
    /// (EnvTextureList.bin, EnvTextureList3.bin, ou arquivo .txt com mapeamento id→nome).
    /// Útil para configurar o cache sem precisar de EditorState.TileNameById.
    /// </summary>
    public void Configure(string envFolder, string texListPath)
    {
        var names = LoadTileNamesFromFile(texListPath);
        Clear();
        _envFolder = envFolder;
        _tileNames = names;
    }

    /// <summary>
    /// Busca automaticamente o arquivo de lista de texturas de tile no EnvFolder.
    /// Tenta variações comuns de nome: EnvTextureList3.bin, EnvTextureList.bin, EnvTexture.txt, etc.
    /// Retorna o caminho completo ou null se não encontrado.
    /// </summary>
    public static string? FindEnvTextureList(string envFolder)
    {
        if (string.IsNullOrWhiteSpace(envFolder) || !Directory.Exists(envFolder))
            return null;

        // Nomes comuns usados por diferentes versões do cliente WYD
        var candidates = new[]
        {
            "EnvTextureList3.bin", "EnvTextureList2.bin", "EnvTextureList.bin",
            "EnvTexture.txt", "EnvTextureList.txt", "TileList.txt",
            "envtexturelist3.bin", "envtexturelist.bin", "envtexture.txt",
        };
        foreach (var name in candidates)
        {
            string p = Path.Combine(envFolder, name);
            if (File.Exists(p)) return p;
        }
        // Fallback: primeiro .bin ou .txt que tiver "texture" ou "tile" no nome
        foreach (var f in Directory.GetFiles(envFolder, "*.bin"))
        {
            string fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            if (fn.Contains("texture") || fn.Contains("tile")) return f;
        }
        foreach (var f in Directory.GetFiles(envFolder, "*.txt"))
        {
            string fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            if (fn.Contains("texture") || fn.Contains("tile")) return f;
        }
        return null;
    }

    private static Dictionary<int, string> LoadTileNamesFromFile(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".bin")
            {
                var entries = EnvTextureListBin.Load(path);
                var map = new Dictionary<int, string>();
                for (int i = 0; i < entries.Length; i++)
                {
                    int ti = i - 10; if (ti < 0) continue;
                    var fn = entries[i].FileName;
                    if (!string.IsNullOrWhiteSpace(fn))
                        map[ti] = Path.GetFileNameWithoutExtension(fn.Replace("\\", "/"));
                }
                return map;
            }
            else
            {
                return TextLists.LoadIdToName(path);
            }
        }
        catch { return new(); }
    }

    // ── Obter textura (lazy load) ────────────────────────────────────────────
    /// <summary>Retorna o GL texture ID, ou 0 se não encontrado.</summary>
    public int Get(int tileIndex)
    {
        if (_textures.TryGetValue(tileIndex, out int tex)) return tex;
        if (_failed.Contains(tileIndex)) return 0;

        tex = TryLoad(tileIndex);
        if (tex != 0)
            _textures[tileIndex] = tex;
        else
            _failed.Add(tileIndex);

        return tex;
    }

    public void Invalidate(int tileIndex)
    {
        if (_textures.TryGetValue(tileIndex, out int tex) && tex != 0)
            GL.DeleteTexture(tex);
        _textures.Remove(tileIndex);
        _failed.Remove(tileIndex);
        _pixelData.Remove(tileIndex);
    }

    // ── Pixels brutos (para Texture2DArray no terreno) ───────────────────────
    /// <summary>Retorna pixels RGBA8 crus, ou null se não carregado ou não é WYS.</summary>
    public (byte[] rgba, int w, int h)? GetPixels(int tileIndex)
    {
        // Garante que o tile foi tentado carregar
        if (!_textures.ContainsKey(tileIndex) && !_failed.Contains(tileIndex))
            Get(tileIndex);
        if (_pixelData.TryGetValue(tileIndex, out var data)) return data;
        return null;
    }

    // ── Paths de busca ───────────────────────────────────────────────────────
    private static readonly string[] s_exts = { ".wys", ".png", ".tga", ".jpg", ".bmp" };

    private int TryLoad(int tileIndex)
    {
        if (string.IsNullOrEmpty(_envFolder)) return 0;
        if (!_tileNames.TryGetValue(tileIndex, out var name) || string.IsNullOrEmpty(name))
            return 0;

        // Pastas onde procurar
        var dirs = new[]
        {
            _envFolder,
            Path.Combine(_envFolder, "Tile"),
            Path.Combine(_envFolder, "Texture"),
            Path.Combine(_envFolder, "tile"),
        };

        // Variantes de nome: original, com prefixo M (MTile...), minúsculas
        // O EnvTextureList3.bin referencia "Tile00000.wys" mas alguns clientes
        // salvam os arquivos como "MTile00000.wys" (com prefixo M).
        var nameVariants = new[] { name, "M" + name, name.ToLowerInvariant(), ("M" + name).ToLowerInvariant() };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var variant in nameVariants)
            foreach (var ext in s_exts)
            {
                var path = Path.Combine(dir, variant + ext);
                if (!File.Exists(path)) continue;

                try
                {
                    byte[]? pixels = null;
                    int w = 0, h = 0;

                    if (ext == ".wys")
                    {
                        var res = WysLoader.Decode(path);
                        if (res == null) continue;
                        (pixels, w, h) = res.Value;
                    }
                    else if (ext == ".tga")
                    {
                        (pixels, w, h) = ReadTgaPixels(path);
                    }
                    else
                    {
                        (pixels, w, h) = ReadBitmapPixels(path);
                    }

                    if (pixels == null || w == 0 || h == 0) continue;

                    // Guarda referência ANTES do UploadGl — que faz FlipY in-place.
                    // Após o retorno, _pixelData.rgba já estará na orientação GL (bottom-first),
                    // pronto para BuildTexArrayCore fazer TexSubImage3D sem flip adicional.
                    _pixelData[tileIndex] = (pixels, w, h);
                    return UploadGl(pixels, w, h, true);
                }
                catch { /* tenta próximo */ }
            }
        }
        return 0;
    }

    // ── Loader via System.Drawing (PNG, JPG, BMP) → pixels RGBA ─────────────
    private static (byte[] pixels, int w, int h) ReadBitmapPixels(string path)
    {
        using var bmp   = new Bitmap(path);
        using var bmp32 = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                    SysPixelFormat.Format32bppArgb);

        var bd   = bmp32.LockBits(new Rectangle(0, 0, bmp32.Width, bmp32.Height),
                                   ImageLockMode.ReadOnly, SysPixelFormat.Format32bppArgb);
        int bytes = Math.Abs(bd.Stride) * bmp32.Height;
        var data  = new byte[bytes];
        System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, data, 0, bytes);
        bmp32.UnlockBits(bd);

        // Windows: BGRA → RGBA para OpenGL
        for (int i = 0; i < data.Length; i += 4)
        {
            byte b = data[i]; data[i] = data[i + 2]; data[i + 2] = b;
        }

        return (data, bmp32.Width, bmp32.Height);
    }

    // ── Loader TGA manual (uncompressed type 2 e RLE type 10) → pixels RGBA ──
    private static (byte[] pixels, int w, int h) ReadTgaPixels(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        int idLen       = br.ReadByte();
        /*colorMapType*/ br.ReadByte();
        int imageType   = br.ReadByte();
        br.ReadBytes(5);                    // color map spec
        br.ReadInt16();                     // x origin
        br.ReadInt16();                     // y origin
        int width       = br.ReadInt16();
        int height      = br.ReadInt16();
        int depth       = br.ReadByte();
        int desc        = br.ReadByte();

        br.ReadBytes(idLen);                // image ID

        bool topLeft  = (desc & 0x20) != 0;
        bool hasAlpha = depth == 32;
        int  ch       = hasAlpha ? 4 : 3;

        var pixels = new byte[width * height * 4]; // sempre RGBA no output

        switch (imageType)
        {
            case 2:  TgaReadUncompressed(br, pixels, width, height, ch, topLeft); break;
            case 10: TgaReadRle(br, pixels, width, height, ch, topLeft);          break;
            default: throw new NotSupportedException($"TGA type {imageType} não suportado.");
        }

        return (pixels, width, height);
    }

    private static void TgaReadUncompressed(BinaryReader br, byte[] pixels,
        int width, int height, int srcCh, bool topLeft)
    {
        for (int y = 0; y < height; y++)
        {
            int dstRow = topLeft ? y : height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                ReadTgaPixel(br, pixels, (dstRow * width + x) * 4, srcCh);
            }
        }
    }

    private static void TgaReadRle(BinaryReader br, byte[] pixels,
        int width, int height, int srcCh, bool topLeft)
    {
        int total = width * height;
        int pixel = 0;

        while (pixel < total)
        {
            byte rep = br.ReadByte();
            if ((rep & 0x80) != 0)           // RLE packet
            {
                int count = (rep & 0x7F) + 1;
                var tmp = new byte[4];
                ReadTgaPixel(br, tmp, 0, srcCh);
                for (int k = 0; k < count && pixel < total; k++, pixel++)
                {
                    int row = pixel / width;
                    int col = pixel % width;
                    int dstRow = topLeft ? row : height - 1 - row;
                    int off = (dstRow * width + col) * 4;
                    pixels[off] = tmp[0]; pixels[off+1] = tmp[1];
                    pixels[off+2] = tmp[2]; pixels[off+3] = tmp[3];
                }
            }
            else                             // raw packet
            {
                int count = rep + 1;
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
        // TGA armazena BGR(A) → convertemos para RGBA
        byte b = br.ReadByte();
        byte g = br.ReadByte();
        byte r = br.ReadByte();
        byte a = srcCh == 4 ? br.ReadByte() : (byte)255;
        dst[off]   = r;
        dst[off+1] = g;
        dst[off+2] = b;
        dst[off+3] = a;
    }

    // ── Upload para OpenGL ────────────────────────────────────────────────────
    private static int UploadGl(byte[] data, int width, int height, bool flipY)
    {
        if (flipY) FlipY(data, width, height);

        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            width, height, 0, GlPixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
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

    // ── Limpeza ──────────────────────────────────────────────────────────────
    public void Clear()
    {
        foreach (var tex in _textures.Values)
            if (tex != 0) GL.DeleteTexture(tex);
        _textures.Clear();
        _failed.Clear();
        _pixelData.Clear();
    }

    public int LoadedCount  => _textures.Count;
    public int FailedCount  => _failed.Count;
    public int PixelCount   => _pixelData.Count;

    public void Dispose() => Clear();
}
