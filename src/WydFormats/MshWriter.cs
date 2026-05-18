using System.Text;

namespace WydFormats;

/// <summary>
/// Escreve um MshMesh no formato binário .msa do WYD.
///
/// Formato escrito: FVF 274 (0x112) = XYZ(12) + Normal(12) + UV(8) = 32 bytes/vértice.
///
/// Binary layout:
///   [FVF:uint32][sizeVertex:int32][attCount:int32]
///   [attRanges: attCount * 20 bytes  — 5 x int32 por sub-mesh]
///   [texNames:  attCount * 11 bytes  — ASCII sem extensão, zero-padded]
///   [ibSize:int32][indices: uint16 each]
///   [vbSize:int32][vertices: 32 bytes each — xyz normal uv]
/// </summary>
public static class MshWriter
{
    private const uint FVF_WRITE    = 0x112u; // XYZ + Normal + Tex1
    private const int  VERTEX_BYTES = 32;     // 12+12+8

    /// <summary>Salva mesh no caminho especificado.</summary>
    public static void Save(MshMesh mesh, string path)
    {
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs, Encoding.ASCII);
        Save(mesh, bw);
    }

    public static void Save(MshMesh mesh, BinaryWriter bw)
    {
        int attCount  = mesh.SubMeshes.Length;
        int vCount    = mesh.Positions.Length / 3;
        int idxCount  = mesh.Indices.Length;

        // ── Header ───────────────────────────────────────────────────────────
        bw.Write(FVF_WRITE);
        bw.Write(VERTEX_BYTES);
        bw.Write(attCount);

        // ── Attribute ranges (5 × int32 = 20 bytes per sub-mesh) ────────────
        for (int i = 0; i < attCount; i++)
        {
            var sub = mesh.SubMeshes[i];
            bw.Write(i);                  // AttribId
            bw.Write(sub.FaceStart);
            bw.Write(sub.FaceCount);
            bw.Write(sub.VertexStart);
            bw.Write(sub.VertexCount);
        }

        // ── Texture names (11 bytes each, zero-padded, no extension) ─────────
        for (int i = 0; i < attCount; i++)
        {
            string texName = (i < mesh.TextureNames.Length) ? mesh.TextureNames[i] : "";
            // Strip extension + directory just in case
            texName = Path.GetFileNameWithoutExtension(texName);
            // Truncate to 10 chars max (need null terminator in 11 bytes)
            if (texName.Length > 10) texName = texName[..10];

            byte[] buf = new byte[11];
            if (texName.Length > 0)
            {
                byte[] ascii = Encoding.ASCII.GetBytes(texName);
                Buffer.BlockCopy(ascii, 0, buf, 0, ascii.Length);
            }
            bw.Write(buf);
        }

        // ── Index buffer (uint16 each) ────────────────────────────────────────
        int ibSize = idxCount * 2;
        bw.Write(ibSize);
        foreach (var idx in mesh.Indices)
        {
            if (idx > ushort.MaxValue)
                throw new InvalidDataException($"Índice fora do range uint16: {idx}");
            bw.Write((ushort)idx);
        }

        // ── Vertex buffer (32 bytes each: pos xyz | norm xyz | uv) ───────────
        int vbSize = vCount * VERTEX_BYTES;
        bw.Write(vbSize);
        for (int i = 0; i < vCount; i++)
        {
            // Position (xyz)
            bw.Write(mesh.Positions.Length > i * 3 + 2 ? mesh.Positions[i * 3 + 0] : 0f);
            bw.Write(mesh.Positions.Length > i * 3 + 2 ? mesh.Positions[i * 3 + 1] : 0f);
            bw.Write(mesh.Positions.Length > i * 3 + 2 ? mesh.Positions[i * 3 + 2] : 0f);
            // Normal (xyz)
            bw.Write(mesh.Normals.Length > i * 3 + 2 ? mesh.Normals[i * 3 + 0] : 0f);
            bw.Write(mesh.Normals.Length > i * 3 + 2 ? mesh.Normals[i * 3 + 1] : 1f);
            bw.Write(mesh.Normals.Length > i * 3 + 2 ? mesh.Normals[i * 3 + 2] : 0f);
            // UV
            bw.Write(mesh.TexCoords.Length > i * 2 + 1 ? mesh.TexCoords[i * 2 + 0] : 0f);
            bw.Write(mesh.TexCoords.Length > i * 2 + 1 ? mesh.TexCoords[i * 2 + 1] : 0f);
        }
    }
}
