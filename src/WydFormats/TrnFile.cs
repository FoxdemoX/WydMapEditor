using System.Text;

namespace WydFormats;

public sealed class TrnFile
{
    public byte NameLength { get; set; }
    public string MapName { get; set; } = "";
    public byte EnvPosX { get; set; }
    public byte EnvPosY { get; set; }
    public TileInfo[] Tiles { get; } = new TileInfo[64 * 64];

    public static TrnFile Load(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs, Encoding.ASCII);
        return Load(br);
    }

    public static TrnFile Load(BinaryReader br)
    {
        var trn = new TrnFile();
        trn.NameLength = br.ReadByte();

        var nameLen = trn.NameLength;
        if (nameLen > 128)
            nameLen = 128;

        var nameBytes = br.ReadBytes(nameLen);
        trn.MapName = Encoding.ASCII.GetString(nameBytes);
        trn.EnvPosX = br.ReadByte();
        trn.EnvPosY = br.ReadByte();

        for (int i = 0; i < trn.Tiles.Length; i++)
        {
            var t = new TileInfo();
            t.Height = unchecked((sbyte)br.ReadByte());
            t.B0 = br.ReadByte();
            t.B1 = br.ReadByte();
            t.B2 = br.ReadByte();
            t.B3 = br.ReadByte();
            t.B4 = br.ReadByte();
            t.B5 = br.ReadByte();
            t.B6 = br.ReadByte();
            t.B7 = br.ReadByte();
            t.B8 = br.ReadByte();
            t.B9 = br.ReadByte();
            t.B10 = br.ReadByte();
            trn.Tiles[i] = t;
        }

        return trn;
    }

    public void Save(string path)
    {
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs, Encoding.ASCII);
        Save(bw);
    }

    public void Save(BinaryWriter bw)
    {
        var nameBytes = Encoding.ASCII.GetBytes(MapName ?? "");
        var len = (byte)Math.Min(128, nameBytes.Length);
        bw.Write(len);
        bw.Write(nameBytes, 0, len);
        bw.Write(EnvPosX);
        bw.Write(EnvPosY);

        for (int i = 0; i < Tiles.Length; i++)
        {
            var t = Tiles[i];
            bw.Write(unchecked((byte)t.Height));
            bw.Write(t.B0);
            bw.Write(t.B1);
            bw.Write(t.B2);
            bw.Write(t.B3);
            bw.Write(t.B4);
            bw.Write(t.B5);
            bw.Write(t.B6);
            bw.Write(t.B7);
            bw.Write(t.B8);
            bw.Write(t.B9);
            bw.Write(t.B10);
        }
    }
}

public struct TileInfo
{
    public sbyte Height;
    public byte B0;
    public byte B1;
    public byte B2;
    public byte B3;
    public byte B4;
    public byte B5;
    public byte B6;
    public byte B7;
    public byte B8;
    public byte B9;
    public byte B10;

    public byte TileIndex
    {
        readonly get => B0;
        set => B0 = value;
    }

    public byte TileCoord
    {
        readonly get => B1;
        set => B1 = value;
    }

    public byte BackTileIndex
    {
        readonly get => B2;
        set => B2 = value;
    }

    public byte BackTileCoord
    {
        readonly get => B3;
        set => B3 = value;
    }

    /// <summary>
    /// Cor do tile em formato R|(G&lt;&lt;8)|(B&lt;&lt;16)|(A&lt;&lt;24).
    /// Use SetColor para modificar.
    /// </summary>
    public readonly uint Color
    {
        get => (uint)(B9 | (B8 << 8) | (B7 << 16) | (B10 << 24));
    }

    /// <summary>Aplica cor no formato R|(G&lt;&lt;8)|(B&lt;&lt;16). Alpha é sempre 0xFF.</summary>
    public void SetColor(uint rgbColor)
    {
        B9 = (byte)(rgbColor & 0xFF);
        B8 = (byte)((rgbColor >> 8) & 0xFF);
        B7 = (byte)((rgbColor >> 16) & 0xFF);
        B10 = 0xFF;
    }
}
