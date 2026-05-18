using System.Globalization;
using System.Text;

namespace WydFormats;

public sealed class DatFile
{
    public List<DatRecord> Records { get; } = new List<DatRecord>();

    public static DatFile Load(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs, Encoding.ASCII);
        return Load(br);
    }

    public static DatFile Load(BinaryReader br)
    {
        var dat = new DatFile();
        long len = br.BaseStream.Length;

        while (br.BaseStream.Position + 28 <= len)
        {
            var r = new DatRecord();
            r.ObjType = br.ReadUInt32();
            r.PosX = br.ReadSingle();
            r.PosY = br.ReadSingle();
            r.Height = br.ReadSingle();
            r.Angle = br.ReadSingle();
            r.TextureSetIndex = br.ReadInt32();
            r.MaskIndex = br.ReadInt32();

            if (r.ObjType >= 501 && r.ObjType < 600 && br.BaseStream.Position + 8 <= len)
            {
                r.ScaleH = br.ReadSingle();
                r.ScaleV = br.ReadSingle();
                r.HasScale = true;
            }
            else
            {
                r.ScaleH = 1f;
                r.ScaleV = 1f;
                r.HasScale = false;
            }

            dat.Records.Add(r);
        }

        return dat;
    }

    public void Save(string path)
    {
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs, Encoding.ASCII);
        Save(bw);
    }

    public void Save(BinaryWriter bw)
    {
        for (int i = 0; i < Records.Count; i++)
        {
            var r = Records[i];
            bw.Write(r.ObjType);
            bw.Write(r.PosX);
            bw.Write(r.PosY);
            bw.Write(r.Height);
            bw.Write(r.Angle);
            bw.Write(r.TextureSetIndex);
            bw.Write(r.MaskIndex);
            if (r.ObjType >= 501 && r.ObjType < 600)
            {
                bw.Write(r.ScaleH);
                bw.Write(r.ScaleV);
            }
        }
    }
}

public struct DatRecord
{
    public uint ObjType;
    public float PosX;
    public float PosY;
    public float Height;
    public float Angle;
    public int TextureSetIndex;
    public int MaskIndex;
    public bool HasScale;
    public float ScaleH;
    public float ScaleV;

    // ── Cor de vertex light (editor-only, não salvo no .dat binário) ──────────
    // Persistido em arquivo auxiliar .datcolor ao lado do .dat.
    // Representa um multiplicador de cor sobre a textura (semelhante ao D3DMATERIAL9.Diffuse do cliente).
    // HasColorOverride=false → cor neutra (branco, sem multiplicação).
    public bool HasColorOverride;
    public byte ColorR;
    public byte ColorG;
    public byte ColorB;

    // ── Textura override (editor-only, não salvo no .dat binário) ────────────
    // Nome base da textura (sem extensão) a substituir todas as sub-texturas do mesh.
    // Vazio/null = sem override (usa texturas originais do .msa).
    public string? TextureOverrideName;
}
