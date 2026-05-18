using System.Text;

namespace WydFormats;

public static class EnvTextureListBin
{
    public static EnvTextureEntry[] Load(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs, Encoding.ASCII);
        return Load(br);
    }

    public static void Save(string path, EnvTextureEntry[] entries)
    {
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs, Encoding.ASCII);
        Save(bw, entries);
    }

    public static EnvTextureEntry[] Load(BinaryReader br)
    {
        var entries = new EnvTextureEntry[512];
        for (int i = 0; i < entries.Length; i++)
        {
            var nameBytes = br.ReadBytes(255);
            int end = Array.IndexOf(nameBytes, (byte)0);
            if (end < 0)
                end = nameBytes.Length;

            var fileName = Encoding.ASCII.GetString(nameBytes, 0, end).Trim();
            byte alpha = br.ReadByte();
            uint lastUsed = br.ReadUInt32();
            uint showTime = br.ReadUInt32();

            entries[i] = new EnvTextureEntry(fileName, alpha, lastUsed, showTime);
        }
        return entries;
    }

    public static void Save(BinaryWriter bw, EnvTextureEntry[] entries)
    {
        for (int i = 0; i < 512; i++)
        {
            var e = i < entries.Length ? entries[i] : default;
            var bytes = Encoding.ASCII.GetBytes(e.FileName ?? "");
            int len = Math.Min(255, bytes.Length);
            bw.Write(bytes, 0, len);
            if (len < 255)
                bw.Write(new byte[255 - len]);
            bw.Write(e.Alpha);
            bw.Write(e.LastUsedTime);
            bw.Write(e.ShowTime);
        }
    }
}

public readonly record struct EnvTextureEntry(string FileName, byte Alpha, uint LastUsedTime, uint ShowTime);
