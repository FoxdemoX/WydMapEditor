namespace WydFormats;

/// <summary>
/// Parsed geometry for one MSA/MSH file.
/// Positions, Normals and TexCoords are parallel arrays (vertexCount elements each).
/// Indices reference into those arrays.
/// </summary>
public sealed class MshMesh
{
    public float[]      Positions    { get; init; } = Array.Empty<float>(); // xyz triples
    public float[]      Normals      { get; init; } = Array.Empty<float>(); // xyz triples
    public float[]      TexCoords    { get; init; } = Array.Empty<float>(); // uv pairs
    public uint[]       Indices      { get; init; } = Array.Empty<uint>();
    public MshSubMesh[] SubMeshes    { get; init; } = Array.Empty<MshSubMesh>();
    public string[]     TextureNames { get; init; } = Array.Empty<string>(); // base name, no ext
    public string       SourcePath   { get; init; } = "";
}

public sealed class MshSubMesh
{
    public int FaceStart          { get; init; }
    public int FaceCount          { get; init; }
    public int VertexStart        { get; init; }
    public int VertexCount        { get; init; }
    public int TextureNameIndex   { get; init; } // index into MshMesh.TextureNames
}

/// <summary>
/// Loads WYD .msa / .msh mesh files into CPU-side float arrays ready for GL upload.
///
/// Supported FVF formats:
///   274 (0x112) = D3DFVF_XYZ + D3DFVF_NORMAL + D3DFVF_TEX1  = 32 bytes  (most objects)
///   530 (0x212) = D3DFVF_XYZ + D3DFVF_NORMAL + D3DFVF_TEX2  = 40 bytes  (RDVERTEX2)
///   322 (0x142) = D3DFVF_XYZ + D3DFVF_DIFFUSE + D3DFVF_TEX1 = 24 bytes  (some effects)
///
/// Binary layout:
///   [FVF:4][sizeVertex:4][attCount:4]
///   [attRanges: attCount x 20 bytes (5 x int32 each)]
///   [texNames:  attCount x 11 bytes each]
///   [ibSize:4][indices: ibSize bytes, uint16 each]
///   [vbSize:4][vertices: vbSize bytes]
/// </summary>
public static class MshLoader
{
    // D3D FVF flag constants
    private const uint FVF_XYZ     = 0x002u;
    private const uint FVF_NORMAL  = 0x010u;
    private const uint FVF_DIFFUSE = 0x040u;
    private const uint FVF_TEX1    = 0x100u;
    private const uint FVF_TEX2    = 0x200u;

    // ── Entry points ────────────────────────────────────────────────────────

    public static MshMesh? Load(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            return Load(br, path);
        }
        catch { return null; }
    }

    public static MshMesh? Load(BinaryReader br, string sourcePath = "")
    {
        try { return LoadCore(br, sourcePath); }
        catch { return null; }
    }

    // ── Core parser ─────────────────────────────────────────────────────────

    private static MshMesh? LoadCore(BinaryReader br, string sourcePath)
    {
        uint fvf        = br.ReadUInt32();
        int  sizeVertex = br.ReadInt32();
        int  attCount   = br.ReadInt32();

        // Sanity guards
        if (sizeVertex <= 0 || sizeVertex > 512) return null;
        if (attCount   <  0 || attCount   >  32) return null;

        // ── Attribute ranges (D3DXATTRIBUTERANGE = 5 x int32 = 20 bytes) ──
        var attRanges = new (int AttribId, int FaceStart, int FaceCount,
                             int VertexStart, int VertexCount)[attCount];
        for (int i = 0; i < attCount; i++)
            attRanges[i] = (br.ReadInt32(), br.ReadInt32(), br.ReadInt32(),
                            br.ReadInt32(), br.ReadInt32());

        // ── Texture names (11 bytes each, one per attribute) ──────────────
        // The client strips directory and extension; we do the same.
        var texNames = new string[attCount];
        for (int i = 0; i < attCount; i++)
        {
            byte[] raw = br.ReadBytes(11);
            int len = 0;
            while (len < raw.Length && raw[len] != 0) len++;
            string name = System.Text.Encoding.ASCII.GetString(raw, 0, len).Trim();
            // Strip directory separators
            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slash >= 0) name = name[(slash + 1)..];
            // Strip extension
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name[..dot];
            texNames[i] = name.Trim();
        }

        // ── Index buffer (16-bit indices) ─────────────────────────────────
        int ibSize = br.ReadInt32();
        if (ibSize < 0 || ibSize > 8 * 1024 * 1024) return null;
        int indexCount = ibSize / 2;
        var indices = new uint[indexCount];
        for (int i = 0; i < indexCount; i++)
            indices[i] = br.ReadUInt16();

        // ── Vertex buffer ─────────────────────────────────────────────────
        int vbSize = br.ReadInt32();
        if (vbSize <= 0 || vbSize > 64 * 1024 * 1024) return null;
        if (vbSize % sizeVertex != 0) return null;
        int vertexCount = vbSize / sizeVertex;
        byte[] rawVerts = br.ReadBytes(vbSize);

        // ── Determine field offsets from FVF ──────────────────────────────
        bool hasNormal  = (fvf & FVF_NORMAL)  != 0;
        bool hasDiffuse = (fvf & FVF_DIFFUSE) != 0;
        bool hasTex     = (fvf & (FVF_TEX1 | FVF_TEX2)) != 0;

        // Layout: position(12) | normal?(12) | diffuse?(4) | uv(8)
        int posOff    = 0;
        int normalOff = posOff + 12;
        int diffOff   = normalOff + (hasNormal ? 12 : 0);
        int uvOff     = diffOff  + (hasDiffuse ?  4 : 0);

        // ── Unpack vertices ───────────────────────────────────────────────
        var positions = new float[vertexCount * 3];
        var normals   = new float[vertexCount * 3];
        var texCoords = new float[vertexCount * 2];

        for (int i = 0; i < vertexCount; i++)
        {
            int b = i * sizeVertex;

            positions[i * 3 + 0] = BitConverter.ToSingle(rawVerts, b + posOff);
            positions[i * 3 + 1] = BitConverter.ToSingle(rawVerts, b + posOff + 4);
            positions[i * 3 + 2] = BitConverter.ToSingle(rawVerts, b + posOff + 8);

            if (hasNormal && normalOff + 12 <= sizeVertex)
            {
                normals[i * 3 + 0] = BitConverter.ToSingle(rawVerts, b + normalOff);
                normals[i * 3 + 1] = BitConverter.ToSingle(rawVerts, b + normalOff + 4);
                normals[i * 3 + 2] = BitConverter.ToSingle(rawVerts, b + normalOff + 8);
            }
            else
            {
                normals[i * 3 + 1] = 1f; // default up normal
            }

            if (hasTex && uvOff + 8 <= sizeVertex)
            {
                texCoords[i * 2 + 0] = BitConverter.ToSingle(rawVerts, b + uvOff);
                texCoords[i * 2 + 1] = BitConverter.ToSingle(rawVerts, b + uvOff + 4);
            }
        }

        // ── Sub-meshes ────────────────────────────────────────────────────
        var subMeshes = new MshSubMesh[attCount];
        for (int i = 0; i < attCount; i++)
        {
            subMeshes[i] = new MshSubMesh
            {
                FaceStart        = attRanges[i].FaceStart,
                FaceCount        = attRanges[i].FaceCount,
                VertexStart      = attRanges[i].VertexStart,
                VertexCount      = attRanges[i].VertexCount,
                TextureNameIndex = i,
            };
        }

        return new MshMesh
        {
            Positions    = positions,
            Normals      = normals,
            TexCoords    = texCoords,
            Indices      = indices,
            SubMeshes    = subMeshes,
            TextureNames = texNames,
            SourcePath   = sourcePath,
        };
    }
}
