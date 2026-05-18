using System.Text;

namespace WydFormats;

public static class MeshTextureListBin
{
    public const int MaxModelTexture = 3048;
    private const int EntrySize = 264;
    private const int NameSize = 255;

    public static bool EnsureEntry(string meshTextureListBinPath, string meshRelativeWysPath, bool alpha)
    {
        if (string.IsNullOrWhiteSpace(meshTextureListBinPath)) return false;
        if (!File.Exists(meshTextureListBinPath)) return false;
        if (string.IsNullOrWhiteSpace(meshRelativeWysPath)) return false;

        string want = meshRelativeWysPath.Replace('/', '\\').Trim();
        var buf = File.ReadAllBytes(meshTextureListBinPath);
        if (buf.Length < MaxModelTexture * EntrySize) return false;

        int empty = -1;
        for (int i = 0; i < MaxModelTexture; i++)
        {
            int off = i * EntrySize;
            byte b0 = buf[off];
            if (b0 == 0)
            {
                if (empty < 0) empty = i;
                continue;
            }

            string name = ReadCStr(buf, off, NameSize);
            if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (empty < 0) return false;

        int eoff = empty * EntrySize;
        WriteCStr(buf, eoff, NameSize, want);
        buf[eoff + NameSize] = (byte)(alpha ? 'A' : 'C');

        File.WriteAllBytes(meshTextureListBinPath, buf);
        return true;
    }

    private static string ReadCStr(byte[] buf, int off, int max)
    {
        int len = 0;
        while (len < max && buf[off + len] != 0) len++;
        return Encoding.ASCII.GetString(buf, off, len);
    }

    private static void WriteCStr(byte[] buf, int off, int max, string s)
    {
        Array.Clear(buf, off, max);
        var bytes = Encoding.ASCII.GetBytes(s);
        int n = Math.Min(max - 1, bytes.Length);
        Buffer.BlockCopy(bytes, 0, buf, off, n);
        buf[off + n] = 0;
    }
}

