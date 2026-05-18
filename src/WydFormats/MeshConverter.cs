using Assimp;
using System.IO.Compression;
using System.Text.Json;

namespace WydFormats;

/// <summary>
/// Resultado de uma conversão de mesh 3D externa para .msa do WYD.
/// </summary>
public sealed class ConversionResult
{
    /// <summary>Caminho absoluto do arquivo .msa gerado.</summary>
    public string MsaPath       { get; init; } = "";
    /// <summary>ObjType atribuído automaticamente.</summary>
    public int    ObjType        { get; init; }
    /// <summary>Nome de exibição amigável.</summary>
    public string DisplayName    { get; init; } = "";
    /// <summary>Caminho relativo para registrar no MeshList.txt (ex: Mesh\CustomMeshes\nome.msa).</summary>
    public string RelativeMsaPath { get; init; } = "";
}

/// <summary>
/// Converte arquivos 3D externos (FBX, GLB, GLTF, OBJ, etc.) para o formato .msa do WYD.
///
/// Texturas embutidas (GLB) são extraídas como .png/.jpg.
/// Texturas externas (FBX) são copiadas para a pasta de destino.
/// O ObjectRenderer já possui fallback para PNG e JPG.
/// </summary>
public static class MeshConverter
{
    public static readonly string[] SupportedExtensions =
    {
        ".fbx", ".glb", ".gltf", ".obj", ".dae", ".3ds", ".blend", ".ply", ".stl", ".smd",
    };

    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return Array.IndexOf(SupportedExtensions, ext) >= 0
            || ext == ".msa" || ext == ".msh";
    }

    /// <summary>
    /// Converte o arquivo para .msa de forma assíncrona (não bloqueia a UI).
    /// Progresso é reportado via <paramref name="progress"/>.
    /// Retorna null em caso de falha.
    /// </summary>
    public static Task<ConversionResult?> ConvertAsync(
        string sourcePath,
        string gameFolder,
        IReadOnlyDictionary<int, string> existingMeshIds,
        IProgress<string>? progress = null)
    {
        return Task.Run(() => ConvertCore(sourcePath, gameFolder, existingMeshIds, progress));
    }

    // ── Conversão principal (roda em thread de background) ──────────────────

    private static ConversionResult? ConvertCore(
        string sourcePath,
        string gameFolder,
        IReadOnlyDictionary<int, string> existingMeshIds,
        IProgress<string>? progress)
    {
        try
        {
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();

            // Se já é .msa, só registra sem converter
            if (ext == ".msa" || ext == ".msh")
                return RegisterExistingMsa(sourcePath, gameFolder, existingMeshIds);

            progress?.Report("Preparando pastas...");

            string customDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? ".";
            if (!string.IsNullOrEmpty(gameFolder) && Directory.Exists(gameFolder))
            {
                string preferred = "";
                if (Directory.Exists(Path.Combine(gameFolder, "Mesh")))
                    preferred = Path.Combine(gameFolder, "Mesh", "CustomMeshes");
                else if (Directory.Exists(Path.Combine(gameFolder, "mesh")))
                    preferred = Path.Combine(gameFolder, "mesh", "CustomMeshes");
                else if (Directory.Exists(Path.Combine(gameFolder, "Env")))
                    preferred = Path.Combine(gameFolder, "Env", "Mesh", "CustomMeshes");
                try
                {
                    if (!string.IsNullOrEmpty(preferred))
                    {
                        Directory.CreateDirectory(preferred);
                        customDir = preferred;
                    }
                }
                catch { }
            }
            Directory.CreateDirectory(customDir);

            string meshBaseName = SanitizeName(Path.GetFileNameWithoutExtension(sourcePath));
            string msaPath      = Path.Combine(customDir, meshBaseName + ".msa");
            Dictionary<int, string>? baseColorByMat = null;
            Dictionary<int, int>? baseColorUvByMat = null;
            if (ext == ".glb")
            {
                baseColorByMat = new Dictionary<int, string>();
                baseColorUvByMat = new Dictionary<int, int>();
                TryExtractGlbImagesAndMaterialMap(sourcePath, customDir, baseColorByMat, baseColorUvByMat);
            }
            else if (ext == ".gltf")
            {
                baseColorByMat = new Dictionary<int, string>();
                baseColorUvByMat = new Dictionary<int, int>();
                TryExtractGltfImagesAndMaterialMap(sourcePath, customDir, baseColorByMat, baseColorUvByMat);
            }

            // relPath: relativo ao gameFolder SOMENTE se o arquivo estiver dentro dele.
            // Caso contrário (ex: Desktop), usa caminho absoluto — Path.Combine(base, absolute)
            // retorna o absolute diretamente no .NET, então o renderer encontra o arquivo.
            string relPath = msaPath; // padrão: caminho absoluto
            if (!string.IsNullOrEmpty(gameFolder))
            {
                string rel = Path.GetRelativePath(gameFolder, msaPath).Replace('/', '\\');
                if (!rel.StartsWith(".."))   // está dentro do gameFolder
                    relPath = rel;
            }
            // Reutiliza ObjType existente se já há uma entrada com o mesmo nome base
            int objType = -1;
            foreach (var kv in existingMeshIds)
            {
                if (string.Equals(
                        Path.GetFileNameWithoutExtension(kv.Value),
                        meshBaseName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    objType = kv.Key;
                    break;
                }
            }
            if (objType == -1) objType = PickFreeObjType(existingMeshIds);

            // Sempre reconverte para garantir compatibilidade com o cliente (limites/UV/texturas)
            if (File.Exists(msaPath))
            {
                try { File.Delete(msaPath); } catch { }
            }

            // ── Carregar com Assimp ──────────────────────────────────────────
            progress?.Report("Lendo arquivo 3D (Assimp)...");

            using var ctx = new AssimpContext();
            Scene? scene;
            try
            {
                scene = ctx.ImportFile(sourcePath,
                    PostProcessSteps.Triangulate              |
                    PostProcessSteps.GenerateNormals           |
                    PostProcessSteps.FlipUVs                   |
                    PostProcessSteps.JoinIdenticalVertices     |
                    PostProcessSteps.OptimizeMeshes            |
                    PostProcessSteps.PreTransformVertices);    // bake all node transforms into vertices
            }
            catch (Exception ex)
            {
                progress?.Report($"Erro ao ler arquivo: {ex.Message}");
                return null;
            }

            if (scene == null || !scene.HasMeshes)
            {
                progress?.Report("Erro: arquivo não contém geometria válida.");
                return null;
            }

            progress?.Report($"Processando {scene.MeshCount} sub-mesh(es)...");

            // ── Extrair texturas embutidas (GLB/GLTF) ────────────────────────
            // embeddedTexMap: key = "*0", "*1", ... → base name sem extensão
            var embeddedTexMap = new Dictionary<string, string>();

            if (scene.HasTextures)
            {
                progress?.Report("Extraindo texturas embutidas...");
                for (int ti = 0; ti < scene.Textures.Count; ti++)
                {
                    var emTex = scene.Textures[ti];
                    string texBase = MakeTexBase(meshBaseName, ti);

                    if (emTex.IsCompressed)
                    {
                        string hint = (emTex.CompressedFormatHint ?? "png").ToLowerInvariant().Trim();
                        string ext2 = hint is "jpg" or "jpeg" ? ".jpg" : hint == "webp" ? ".webp" : ".png";
                        string texPath = Path.Combine(customDir, texBase + ext2);
                        try { File.WriteAllBytes(texPath, emTex.CompressedData); } catch { }
                        if (ti == 0)
                        {
                            string alias = Path.Combine(customDir, meshBaseName + ext2);
                            if (File.Exists(texPath))
                            {
                                try { File.Copy(texPath, alias, overwrite: true); } catch { }
                            }
                        }
                        embeddedTexMap[$"*{ti}"] = texBase;
                    }
                    else
                    {
                        int w = emTex.Width;
                        int h = emTex.Height;
                        if (w > 0 && h > 0 && emTex.NonCompressedData != null && emTex.NonCompressedData.Length >= w * h)
                        {
                            string texPath = Path.Combine(customDir, texBase + ".png");
                            TryWriteEmbeddedPng(texPath, emTex);
                            if (ti == 0)
                            {
                                string alias = Path.Combine(customDir, meshBaseName + ".png");
                                if (File.Exists(texPath))
                                {
                                    try { File.Copy(texPath, alias, overwrite: true); } catch { }
                                }
                            }
                            embeddedTexMap[$"*{ti}"] = texBase;
                        }
                    }
                }
            }

            // ── Construir sub-meshes (uma por material) ──────────────────────
            // Após PreTransformVertices, scene.Meshes já está separado por material.
            var positions = new List<float>();
            var normals   = new List<float>();
            var texCoords = new List<float>();
            var indices   = new List<uint>();
            var subMeshes = new List<MshSubMesh>();
            var texNames  = new List<string>();
            string srcDirMain = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? "";
            bool copiedSiblingBaseTex = false;

            // Computar bounding box para auto-escala
            float bMinY = float.MaxValue, bMaxY = float.MinValue;

            int groupIdx = 0;
            foreach (var aiMesh in scene.Meshes)
            {
                int matIdx      = aiMesh.MaterialIndex;
                int faceStart   = indices.Count  / 3;
                int vertexStart = positions.Count / 3;
                int baseVert    = vertexStart;

                int uvSet = 0;
                if (baseColorUvByMat != null && baseColorUvByMat.TryGetValue(matIdx, out int uvFromMat))
                    uvSet = uvFromMat;

                for (int vi = 0; vi < aiMesh.VertexCount; vi++)
                {
                    var p = aiMesh.Vertices[vi];

                    // ── Conversão de sistema de coordenadas ─────────────────
                    // GLTF é Y-up (right-handed). O ObjectRenderer aplica
                    // CreateRotationX(-90°) antes de renderizar, que espera
                    // que a mesh esteja em Z-up (MSA convention):
                    //   World.X = MSA.X = GLTF.X
                    //   World.Y = MSA.Z = GLTF.Y  (altura preservada)
                    //   World.Z = -MSA.Y = -GLTF.Z
                    float mx = p.X;   // GLTF.X → MSA.X
                    float my = p.Z;   // GLTF.Z → MSA.Y (depth)
                    float mz = p.Y;   // GLTF.Y → MSA.Z (altura/up)
                    positions.Add(mx); positions.Add(my); positions.Add(mz);

                    if (mz < bMinY) bMinY = mz;
                    if (mz > bMaxY) bMaxY = mz;

                    if (aiMesh.HasNormals)
                    {
                        var n = aiMesh.Normals[vi];
                        normals.Add(n.X); normals.Add(n.Z); normals.Add(n.Y);
                    }
                    else { normals.Add(0f); normals.Add(0f); normals.Add(1f); }

                    int pick = -1;
                    if (aiMesh.TextureCoordinateChannelCount > 0)
                    {
                        if (uvSet >= 0 && uvSet < aiMesh.TextureCoordinateChannelCount && aiMesh.HasTextureCoords(uvSet))
                            pick = uvSet;
                        else
                        {
                            for (int c = 0; c < aiMesh.TextureCoordinateChannelCount; c++)
                            {
                                if (aiMesh.HasTextureCoords(c)) { pick = c; break; }
                            }
                        }
                    }
                    if (pick >= 0 && aiMesh.HasTextureCoords(pick))
                    {
                        var uv = aiMesh.TextureCoordinateChannels[pick][vi];
                        texCoords.Add(uv.X); texCoords.Add(uv.Y);
                    }
                    else
                    {
                        texCoords.Add(0f); texCoords.Add(0f);
                    }
                }

                foreach (var face in aiMesh.Faces)
                {
                    if (face.IndexCount != 3) continue;
                    // Swap indices 1↔2 para inverter winding order.
                    // A troca de eixos Y↔Z (det=-1) inverte a orientação dos
                    // triângulos; o cliente WYD (D3D, D3DCULL_CCW) descarta
                    // faces com winding errado, causando "buracos" invisíveis.
                    indices.Add((uint)(face.Indices[0] + baseVert));
                    indices.Add((uint)(face.Indices[2] + baseVert));
                    indices.Add((uint)(face.Indices[1] + baseVert));
                }

                int faceCount   = indices.Count  / 3 - faceStart;
                int vertexCount = positions.Count / 3 - vertexStart;

                // Resolver textura do material
                // 1) Para GLB/GLTF: o parsing customizado (baseColorByMat) é mais confiável
                //    porque mapeia material → texture → image seguindo a spec glTF.
                // 2) Só consulta Assimp se baseColorByMat não resolveu.
                string texName = "";
                if (baseColorByMat != null && baseColorByMat.TryGetValue(matIdx, out var gltfTex))
                {
                    texName = gltfTex;
                }
                if (string.IsNullOrEmpty(texName) && matIdx < scene.MaterialCount)
                {
                    var mat = scene.Materials[matIdx];
                    string rawTex = GetMaterialTexturePath(mat);
                    if (!string.IsNullOrEmpty(rawTex))
                    {
                        if (embeddedTexMap.TryGetValue(rawTex, out string? embName))
                        {
                            texName = embName;
                        }
                        else if (!string.IsNullOrEmpty(rawTex))
                        {
                            texName = ClampTexBaseName(SanitizeName(Path.GetFileNameWithoutExtension(rawTex)));
                            CopyExternalTexture(rawTex, srcDirMain, customDir, texName);
                        }
                    }
                }
                if (string.IsNullOrEmpty(texName) && !copiedSiblingBaseTex)
                {
                    copiedSiblingBaseTex = TryCopyTextureByBaseName(srcDirMain, customDir, meshBaseName);
                    if (copiedSiblingBaseTex) texName = ClampTexBaseName(meshBaseName);
                }
                if (string.IsNullOrEmpty(texName))
                {
                    texName = ClampTexBaseName(meshBaseName);
                }

                subMeshes.Add(new MshSubMesh
                {
                    FaceStart        = faceStart,
                    FaceCount        = faceCount,
                    VertexStart      = vertexStart,
                    VertexCount      = vertexCount,
                    TextureNameIndex = groupIdx,
                });
                texNames.Add(texName);
                groupIdx++;
            }

            // ── Auto-escala: normalizar altura para ~6 unidades WYD ──────────
            // (PreTransformVertices bake inclui escala original em centímetros→metros.
            //  6 unidades WYD ≈ 3 tiles de terreno, tamanho razoável para árvores.)
            float meshHeight = (bMaxY > bMinY) ? (bMaxY - bMinY) : 1f;
            const float TARGET_HEIGHT = 6.0f;
            if (meshHeight > 0.001f && (meshHeight < 1.0f || meshHeight > 30f))
            {
                // Só escala se estiver fora da faixa razoável (1..30 unidades)
                float scale = TARGET_HEIGHT / meshHeight;
                progress?.Report($"Escala automática: {meshHeight:F2} → {TARGET_HEIGHT} unidades (×{scale:F2})");
                for (int i = 0; i < positions.Count; i++)
                    positions[i] *= scale;
            }

            // ── Verificar limite uint16 (índices no .msa são ushort) ─────────
            int totalVerts = positions.Count / 3;
            uint maxIdx = 0;
            for (int i = 0; i < indices.Count; i++)
                if (indices[i] > maxIdx) maxIdx = indices[i];

            if (totalVerts > 65535 || maxIdx >= 65535)
            {
                progress?.Report($"Aviso: {totalVerts} vértices / maxIdx={maxIdx} — ajustando para índice 16-bit (<=65535).");
                TruncateMesh(positions, normals, texCoords, indices, subMeshes);
            }

            if (positions.Count >= 3)
            {
                float minZ = float.MaxValue;
                for (int i = 2; i < positions.Count; i += 3)
                    if (positions[i] < minZ) minZ = positions[i];
                if (minZ != 0f && minZ != float.MaxValue)
                {
                    for (int i = 2; i < positions.Count; i += 3)
                        positions[i] -= minZ;
                }
            }

            // ── Salvar .msa ──────────────────────────────────────────────────
            progress?.Report("Escrevendo arquivo .msa...");

            var mesh = new MshMesh
            {
                Positions    = positions.ToArray(),
                Normals      = normals.ToArray(),
                TexCoords    = texCoords.ToArray(),
                Indices      = indices.ToArray(),
                SubMeshes    = subMeshes.ToArray(),
                TextureNames = texNames.ToArray(),
                SourcePath   = msaPath,
            };

            MshWriter.Save(mesh, msaPath);

            progress?.Report($"Pronto! ObjType: {objType}");
            return new ConversionResult
            {
                MsaPath          = msaPath,
                ObjType          = objType,
                DisplayName      = meshBaseName,
                RelativeMsaPath  = relPath,
            };
        }
        catch (Exception ex)
        {
            progress?.Report($"Erro inesperado: {ex.Message}");
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ConversionResult? RegisterExistingMsa(
        string msaPath,
        string gameFolder,
        IReadOnlyDictionary<int, string> existingMeshIds)
    {
        string meshBaseName = Path.GetFileNameWithoutExtension(msaPath);

        // Verificar se já está registrado pelo nome base
        foreach (var kv in existingMeshIds)
        {
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(kv.Value),
                    meshBaseName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new ConversionResult
                {
                    MsaPath          = msaPath,
                    ObjType          = kv.Key,
                    DisplayName      = meshBaseName,
                    RelativeMsaPath  = kv.Value,
                };
            }
        }

        int    objType = PickFreeObjType(existingMeshIds);
        string rel = msaPath;
        if (!string.IsNullOrEmpty(gameFolder))
        {
            string rel2 = Path.GetRelativePath(gameFolder, msaPath)
                .Replace('/', Path.DirectorySeparatorChar);
            if (!rel2.StartsWith("..")) rel = rel2;
        }
        return new ConversionResult
        {
            MsaPath          = msaPath,
            ObjType          = objType,
            DisplayName      = meshBaseName,
            RelativeMsaPath  = rel,
        };
    }

    /// <summary>
    /// Copia uma textura externa para a pasta de destino.
    /// Aceita PNG, JPG, JPEG e TGA — salva com a extensão original.
    /// </summary>
    private static void CopyExternalTexture(string rawTex, string srcDir,
                                             string dstDir, string texBase)
    {
        texBase = ClampTexBaseName(texBase);
        if (string.IsNullOrEmpty(texBase)) return;

        // Extensões suportadas (ObjectRenderer tenta png, tga, jpg, jpeg)
        string[] exts = { ".png", ".jpg", ".jpeg", ".tga" };
        string rawBase = Path.GetFileNameWithoutExtension(rawTex);

        foreach (var ext in exts)
        {
            // Tenta o caminho como especificado no material
            string[] candidates =
            {
                Path.Combine(srcDir, rawTex),
                Path.Combine(srcDir, rawBase + ext),
                Path.Combine(srcDir, "textures", rawBase + ext),
                Path.Combine(srcDir, "Textures", rawBase + ext),
            };
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                string dst = Path.Combine(dstDir, texBase + Path.GetExtension(candidate));
                if (!File.Exists(dst))
                    File.Copy(candidate, dst, overwrite: false);
                return;
            }
        }
    }

    private static bool TryCopyTextureByBaseName(string srcDir, string dstDir, string baseName)
    {
        if (string.IsNullOrEmpty(srcDir) || string.IsNullOrEmpty(dstDir) || string.IsNullOrEmpty(baseName))
            return false;
        if (!Directory.Exists(srcDir)) return false;
        Directory.CreateDirectory(dstDir);

        baseName = ClampTexBaseName(baseName);
        if (string.IsNullOrEmpty(baseName)) return false;

        string[] exts = { ".wys", ".png", ".jpg", ".jpeg", ".tga", ".WYS", ".PNG", ".JPG", ".JPEG", ".TGA" };
        foreach (var ext in exts)
        {
            string src = Path.Combine(srcDir, baseName + ext);
            if (!File.Exists(src)) continue;
            string dst = Path.Combine(dstDir, baseName + Path.GetExtension(src));
            if (!File.Exists(dst))
            {
                try { File.Copy(src, dst, overwrite: false); } catch { }
            }
            return true;
        }
        return false;
    }

    private static string ClampTexBaseName(string name)
    {
        name = Path.GetFileNameWithoutExtension(name ?? "");
        name = name.Trim().Replace(' ', '_');
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.Length > 10) name = name[..10];
        return name;
    }

    private static string MakeTexBase(string meshBaseName, int index)
    {
        string prefix = ClampTexBaseName(meshBaseName);
        if (prefix.Length > 7) prefix = prefix[..7];
        int idx = index % 100;
        return $"{prefix}_{idx:00}";
    }

    private static string GetMaterialTexturePath(Material mat)
    {
        try
        {
            if (mat.HasTextureDiffuse)
                return mat.TextureDiffuse.FilePath ?? "";
        }
        catch { }

        try
        {
            var mt = mat.GetType();
            var has = mt.GetProperty("HasTextureBaseColor");
            if (has?.PropertyType == typeof(bool) && (bool)(has.GetValue(mat) ?? false))
            {
                var tex = mt.GetProperty("TextureBaseColor")?.GetValue(mat);
                var fp = tex?.GetType().GetProperty("FilePath")?.GetValue(tex) as string;
                if (!string.IsNullOrEmpty(fp)) return fp;
            }
        }
        catch { }

        return "";
    }

    private static int PickFreeObjType(IReadOnlyDictionary<int, string> existing)
    {
        var used = new HashSet<int>(existing.Keys);
        for (int id = 2800; id <= 3047; id++)
            if (!used.Contains(id)) return id;

        for (int id = 1; id <= 2799; id++)
            if (!used.Contains(id)) return id;

        int max = 0;
        foreach (var k in used)
            if (k > max) max = k;
        return max + 1;
    }

    private static bool TryWriteEmbeddedPng(string path, EmbeddedTexture emTex)
    {
        try
        {
            int w = emTex.Width;
            int h = emTex.Height;
            if (w <= 0 || h <= 0) return false;
            if (emTex.NonCompressedData == null) return false;
            if (emTex.NonCompressedData.Length < w * h) return false;

            byte[] raw = new byte[h * (1 + w * 4)];
            int di = 0;
            for (int y = 0; y < h; y++)
            {
                raw[di++] = 0;
                int rowBase = y * w;
                for (int x = 0; x < w; x++)
                {
                    var t = emTex.NonCompressedData[rowBase + x];
                    raw[di++] = t.R;
                    raw[di++] = t.G;
                    raw[di++] = t.B;
                    raw[di++] = t.A;
                }
            }

            byte[] comp;
            using (var ms = new MemoryStream())
            {
                using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    zs.Write(raw, 0, raw.Length);
                comp = ms.ToArray();
            }

            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);

            bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            Span<byte> ihdr = stackalloc byte[13];
            WriteBE32(ihdr, 0, (uint)w);
            WriteBE32(ihdr, 4, (uint)h);
            ihdr[8]  = 8;
            ihdr[9]  = 6;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = 0;
            WritePngChunk(bw, "IHDR", ihdr.ToArray());
            WritePngChunk(bw, "IDAT", comp);
            WritePngChunk(bw, "IEND", Array.Empty<byte>());

            return true;
        }
        catch { return false; }
    }

    private static void WritePngChunk(BinaryWriter bw, string type, byte[] data)
    {
        WriteBE32(bw, (uint)data.Length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        bw.Write(typeBytes);
        if (data.Length != 0) bw.Write(data);

        uint crc = 0xFFFFFFFFu;
        crc = Crc32Update(crc, typeBytes);
        if (data.Length != 0) crc = Crc32Update(crc, data);
        crc ^= 0xFFFFFFFFu;
        WriteBE32(bw, crc);
    }

    private static void WriteBE32(BinaryWriter bw, uint v)
    {
        bw.Write((byte)(v >> 24));
        bw.Write((byte)(v >> 16));
        bw.Write((byte)(v >> 8));
        bw.Write((byte)(v));
    }

    private static void WriteBE32(Span<byte> buf, int offset, uint v)
    {
        buf[offset + 0] = (byte)(v >> 24);
        buf[offset + 1] = (byte)(v >> 16);
        buf[offset + 2] = (byte)(v >> 8);
        buf[offset + 3] = (byte)(v);
    }

    private static uint Crc32Update(uint crc, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : (crc >> 1);
        }
        return crc;
    }

    private static bool TryExtractGlbImagesAndMaterialMap(
        string glbPath,
        string outDir,
        Dictionary<int, string> baseColorByMat,
        Dictionary<int, int> baseColorUvByMat)
    {
        try
        {
            if (!File.Exists(glbPath)) return false;
            string meshBaseName = SanitizeName(Path.GetFileNameWithoutExtension(glbPath));
            byte[] all = File.ReadAllBytes(glbPath);
            if (all.Length < 20) return false;

            uint magic = BitConverter.ToUInt32(all, 0);
            if (magic != 0x46546C67u) return false;
            uint version = BitConverter.ToUInt32(all, 4);
            if (version != 2u) return false;

            int off = 12;
            byte[]? jsonBytes = null;
            byte[]? binBytes = null;

            while (off + 8 <= all.Length)
            {
                int chunkLen = BitConverter.ToInt32(all, off);
                uint chunkType = BitConverter.ToUInt32(all, off + 4);
                off += 8;
                if (chunkLen < 0 || off + chunkLen > all.Length) break;
                if (chunkType == 0x4E4F534Au)
                {
                    jsonBytes = new byte[chunkLen];
                    Buffer.BlockCopy(all, off, jsonBytes, 0, chunkLen);
                }
                else if (chunkType == 0x004E4942u)
                {
                    binBytes = new byte[chunkLen];
                    Buffer.BlockCopy(all, off, binBytes, 0, chunkLen);
                }
                off += chunkLen;
            }

            if (jsonBytes == null || binBytes == null) return false;
            string json = System.Text.Encoding.UTF8.GetString(jsonBytes).TrimEnd('\0', ' ', '\r', '\n', '\t');

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array) return false;
            if (!root.TryGetProperty("bufferViews", out var views) || views.ValueKind != JsonValueKind.Array) return false;
            if (!root.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Array) return false;

            var imageBaseByIndex = new Dictionary<int, string>();
            Directory.CreateDirectory(outDir);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int imgIndex = 0;
            foreach (var img in images.EnumerateArray())
            {
                string mime = "";
                if (img.TryGetProperty("mimeType", out var mtEl) && mtEl.ValueKind == JsonValueKind.String)
                    mime = mtEl.GetString() ?? "";
                string ext = mime switch
                {
                    "image/jpeg" => ".jpg",
                    "image/png"  => ".png",
                    "image/webp" => ".webp",
                    "image/bmp"  => ".bmp",
                    "image/gif"  => ".gif",
                    _ => ".png" // default: save raw bytes with .png extension; many viewers handle it
                };

                string baseName = MakeTexBase(meshBaseName, imgIndex);

                byte[]? imgBytes = null;
                if (img.TryGetProperty("bufferView", out var bvEl) && bvEl.ValueKind == JsonValueKind.Number)
                {
                    int bv = bvEl.GetInt32();
                    if (bv >= 0 && bv < views.GetArrayLength())
                    {
                        var view = views[bv];
                        if (view.TryGetProperty("byteLength", out var blEl) && blEl.ValueKind == JsonValueKind.Number)
                        {
                            int byteLen = blEl.GetInt32();
                            int byteOff = 0;
                            if (view.TryGetProperty("byteOffset", out var boEl) && boEl.ValueKind == JsonValueKind.Number)
                                byteOff = boEl.GetInt32();
                            if (byteLen > 0 && byteOff >= 0 && byteOff + byteLen <= binBytes.Length)
                            {
                                imgBytes = new byte[byteLen];
                                Buffer.BlockCopy(binBytes, byteOff, imgBytes, 0, byteLen);
                            }
                        }
                    }
                }
                if (imgBytes == null && img.TryGetProperty("uri", out var uriEl) && uriEl.ValueKind == JsonValueKind.String)
                {
                    string uri = uriEl.GetString() ?? "";
                    const string b64p = "data:";
                    int comma = uri.IndexOf(',');
                    if (uri.StartsWith(b64p, StringComparison.OrdinalIgnoreCase) && comma > 0)
                    {
                        string b64 = uri[(comma + 1)..];
                        imgBytes = Convert.FromBase64String(b64);
                    }
                }

                if (imgBytes != null && imgBytes.Length > 0)
                {
                    // Detectar extensão real pelos magic bytes se MIME era desconhecido
                    if (mime != "image/jpeg" && mime != "image/png" && mime != "image/webp")
                        ext = DetectImageExtension(imgBytes, ext);

                    string outPath = Path.Combine(outDir, baseName + ext);
                    try { File.WriteAllBytes(outPath, imgBytes); } catch { }
                    imageBaseByIndex[imgIndex] = baseName;

                    if (imgIndex == 0)
                    {
                        string alias = Path.Combine(outDir, meshBaseName + ext);
                        if (File.Exists(outPath))
                        {
                            try { File.Copy(outPath, alias, overwrite: true); } catch { }
                        }
                    }
                }

                imgIndex++;
            }

            if (root.TryGetProperty("materials", out var mats) && mats.ValueKind == JsonValueKind.Array)
            {
                int mi = 0;
                foreach (var mat in mats.EnumerateArray())
                {
                    int texIdx = -1;
                    int texCoord = 0;
                    if (mat.TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.ValueKind == JsonValueKind.Object)
                    {
                        if (pbr.TryGetProperty("baseColorTexture", out var bct) && bct.ValueKind == JsonValueKind.Object)
                        {
                            if (bct.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number)
                                texIdx = idxEl.GetInt32();
                            if (bct.TryGetProperty("texCoord", out var tcEl) && tcEl.ValueKind == JsonValueKind.Number)
                                texCoord = tcEl.GetInt32();
                        }
                    }

                    // Fallback: se não achou baseColorTexture, tenta diffuseTexture (KHR_materials_pbrSpecularGlossiness)
                    if (texIdx < 0)
                    {
                        if (mat.TryGetProperty("extensions", out var exts) && exts.ValueKind == JsonValueKind.Object)
                        {
                            if (exts.TryGetProperty("KHR_materials_pbrSpecularGlossiness", out var sg) && sg.ValueKind == JsonValueKind.Object)
                            {
                                if (sg.TryGetProperty("diffuseTexture", out var dt) && dt.ValueKind == JsonValueKind.Object)
                                {
                                    if (dt.TryGetProperty("index", out var idxEl2) && idxEl2.ValueKind == JsonValueKind.Number)
                                        texIdx = idxEl2.GetInt32();
                                }
                            }
                        }
                    }

                    if (texIdx >= 0 && texIdx < textures.GetArrayLength())
                    {
                        var tex = textures[texIdx];
                        if (tex.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.Number)
                        {
                            int imgSrc = srcEl.GetInt32();
                            if (imageBaseByIndex.TryGetValue(imgSrc, out var baseName))
                                baseColorByMat[mi] = baseName;
                            baseColorUvByMat[mi] = texCoord;
                        }
                    }

                    mi++;
                }
            }

            return imageBaseByIndex.Count > 0;
        }
        catch { return false; }
    }

    private static bool TryExtractGltfImagesAndMaterialMap(
        string gltfPath,
        string outDir,
        Dictionary<int, string> baseColorByMat,
        Dictionary<int, int> baseColorUvByMat)
    {
        try
        {
            if (!File.Exists(gltfPath)) return false;
            string meshBaseName = SanitizeName(Path.GetFileNameWithoutExtension(gltfPath));
            string json = File.ReadAllText(gltfPath);
            string gltfDir = Path.GetDirectoryName(Path.GetFullPath(gltfPath)) ?? "";

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array) return false;
            if (!root.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Array) return false;

            var imageBaseByIndex = new Dictionary<int, string>();
            Directory.CreateDirectory(outDir);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int imgIndex = 0;
            foreach (var img in images.EnumerateArray())
            {
                string uri = "";
                if (img.TryGetProperty("uri", out var uriEl) && uriEl.ValueKind == JsonValueKind.String)
                    uri = uriEl.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(uri)) { imgIndex++; continue; }

                string ext = Path.GetExtension(uri).ToLowerInvariant();
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = uri.IndexOf(',');
                    if (comma <= 0) { imgIndex++; continue; }
                    string meta = uri[..comma];
                    if (meta.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase)) ext = ".jpg";
                    else if (meta.Contains("image/png", StringComparison.OrdinalIgnoreCase)) ext = ".png";
                    else if (meta.Contains("image/webp", StringComparison.OrdinalIgnoreCase)) ext = ".webp";
                    else ext = ".png"; // default
                }
                if (ext == ".jpeg") ext = ".jpg";
                if (string.IsNullOrEmpty(ext)) ext = ".png";

                string baseName = MakeTexBase(meshBaseName, imgIndex);
                if (!used.Add(baseName))
                {
                    for (int k = 1; k < 100; k++)
                    {
                        string suf = k.ToString("D2");
                        string head = baseName.Length > 8 ? baseName[..8] : baseName;
                        string cand = (head + suf);
                        if (cand.Length > 10) cand = cand[..10];
                        if (used.Add(cand)) { baseName = cand; break; }
                    }
                }

                byte[]? imgBytes = null;
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = uri.IndexOf(',');
                    string b64 = uri[(comma + 1)..];
                    imgBytes = Convert.FromBase64String(b64);
                }
                else
                {
                    string srcPath = Path.GetFullPath(Path.Combine(gltfDir, uri.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(srcPath))
                        imgBytes = File.ReadAllBytes(srcPath);
                }

                if (imgBytes != null && imgBytes.Length > 0)
                {
                    // Detectar extensão real pelos magic bytes
                    ext = DetectImageExtension(imgBytes, ext);

                    string outPath = Path.Combine(outDir, baseName + ext);
                    try { File.WriteAllBytes(outPath, imgBytes); } catch { }
                    imageBaseByIndex[imgIndex] = baseName;

                    if (imgIndex == 0)
                    {
                        string alias = Path.Combine(outDir, meshBaseName + ext);
                        if (File.Exists(outPath))
                        {
                            try { File.Copy(outPath, alias, overwrite: true); } catch { }
                        }
                    }
                }

                imgIndex++;
            }

            if (root.TryGetProperty("materials", out var mats) && mats.ValueKind == JsonValueKind.Array)
            {
                int mi = 0;
                foreach (var mat in mats.EnumerateArray())
                {
                    int texIdx = -1;
                    int texCoord = 0;
                    if (mat.TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.ValueKind == JsonValueKind.Object)
                    {
                        if (pbr.TryGetProperty("baseColorTexture", out var bct) && bct.ValueKind == JsonValueKind.Object)
                        {
                            if (bct.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number)
                                texIdx = idxEl.GetInt32();
                            if (bct.TryGetProperty("texCoord", out var tcEl) && tcEl.ValueKind == JsonValueKind.Number)
                                texCoord = tcEl.GetInt32();
                        }
                    }

                    // Fallback: se não achou baseColorTexture, tenta diffuseTexture (KHR_materials_pbrSpecularGlossiness)
                    if (texIdx < 0)
                    {
                        if (mat.TryGetProperty("extensions", out var exts) && exts.ValueKind == JsonValueKind.Object)
                        {
                            if (exts.TryGetProperty("KHR_materials_pbrSpecularGlossiness", out var sg) && sg.ValueKind == JsonValueKind.Object)
                            {
                                if (sg.TryGetProperty("diffuseTexture", out var dt) && dt.ValueKind == JsonValueKind.Object)
                                {
                                    if (dt.TryGetProperty("index", out var idxEl2) && idxEl2.ValueKind == JsonValueKind.Number)
                                        texIdx = idxEl2.GetInt32();
                                }
                            }
                        }
                    }

                    if (texIdx >= 0 && texIdx < textures.GetArrayLength())
                    {
                        var tex = textures[texIdx];
                        if (tex.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.Number)
                        {
                            int imgSrc = srcEl.GetInt32();
                            if (imageBaseByIndex.TryGetValue(imgSrc, out var baseName))
                                baseColorByMat[mi] = baseName;
                            baseColorUvByMat[mi] = texCoord;
                        }
                    }

                    mi++;
                }
            }

            return imageBaseByIndex.Count > 0;
        }
        catch { return false; }
    }

    /// <summary>Detecta a extensão real de uma imagem a partir dos magic bytes.</summary>
    private static string DetectImageExtension(byte[] data, string fallback)
    {
        if (data.Length >= 8)
        {
            // PNG: 89 50 4E 47
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return ".png";
            // JPEG: FF D8 FF
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return ".jpg";
            // WebP: RIFF....WEBP
            if (data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
                && data.Length >= 12 && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
                return ".webp";
            // BMP: BM
            if (data[0] == (byte)'B' && data[1] == (byte)'M')
                return ".bmp";
            // GIF: GIF8
            if (data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8')
                return ".gif";
        }
        return fallback;
    }

    /// <summary>Remove caracteres inválidos de nomes de arquivo. Versão pública para uso externo.</summary>
    public static string SanitizeNamePublic(string name) => SanitizeName(name);

    private static string SanitizeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private static void TruncateMesh(
        List<float> positions, List<float> normals, List<float> texCoords,
        List<uint>  indices,   List<MshSubMesh> subMeshes)
    {
        const int cutVerts = 65535;
        int vertCount = positions.Count / 3;
        int limit = Math.Min(vertCount, cutVerts);
        if (limit <= 0) return;

        int faceTotal = indices.Count / 3;
        var newIndices = new List<uint>(indices.Count);
        var newSubs = new List<MshSubMesh>(subMeshes.Count);

        for (int si = 0; si < subMeshes.Count; si++)
        {
            var sub = subMeshes[si];
            int faceStart = Math.Max(0, sub.FaceStart);
            int faceEnd = Math.Min(faceTotal, sub.FaceStart + sub.FaceCount);

            int newFaceStart = newIndices.Count / 3;
            uint minIdx = uint.MaxValue;
            uint maxIdx = 0;
            for (int f = faceStart; f < faceEnd; f++)
            {
                int bi = f * 3;
                if (bi + 2 >= indices.Count) break;
                uint i0 = indices[bi + 0];
                uint i1 = indices[bi + 1];
                uint i2 = indices[bi + 2];
                if (i0 >= (uint)limit || i1 >= (uint)limit || i2 >= (uint)limit) continue;
                newIndices.Add(i0);
                newIndices.Add(i1);
                newIndices.Add(i2);
                if (i0 < minIdx) minIdx = i0;
                if (i1 < minIdx) minIdx = i1;
                if (i2 < minIdx) minIdx = i2;
                if (i0 > maxIdx) maxIdx = i0;
                if (i1 > maxIdx) maxIdx = i1;
                if (i2 > maxIdx) maxIdx = i2;
            }
            int newFaceCount = newIndices.Count / 3 - newFaceStart;

            int vStart = 0;
            int vCount = 0;
            if (newFaceCount > 0 && minIdx != uint.MaxValue)
            {
                vStart = (int)minIdx;
                vCount = (int)(maxIdx - minIdx + 1);
                if (vStart < 0) vStart = 0;
                if (vStart >= limit) vCount = 0;
                else vCount = Math.Min(vCount, limit - vStart);
            }

            newSubs.Add(new MshSubMesh
            {
                FaceStart        = newFaceStart,
                FaceCount        = newFaceCount,
                VertexStart      = vStart,
                VertexCount      = vCount,
                TextureNameIndex = sub.TextureNameIndex,
            });
        }

        if (vertCount > limit)
        {
            positions.RemoveRange(limit * 3, positions.Count - limit * 3);
            normals  .RemoveRange(limit * 3, normals  .Count - limit * 3);
            texCoords.RemoveRange(limit * 2, texCoords.Count - limit * 2);
        }

        indices.Clear();
        indices.AddRange(newIndices);
        subMeshes.Clear();
        subMeshes.AddRange(newSubs);
    }

    /// <summary>
    /// Adiciona uma linha ao MeshList.txt.
    /// Cria o arquivo se não existir; evita duplicatas.
    /// </summary>
    public static void AppendToMeshList(string meshListPath, int objType, string relPath)
    {
        string line = $"{objType} {relPath.Replace('/', '\\').Replace(Path.DirectorySeparatorChar, '\\')}";

        if (!File.Exists(meshListPath))
        {
            File.WriteAllText(meshListPath, line);
            return;
        }

        var lines = File.ReadAllLines(meshListPath).ToList();
        bool replaced = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0) continue;

            int sep = -1;
            for (int j = 0; j < trimmed.Length; j++)
            {
                if (trimmed[j] == ' ' || trimmed[j] == '\t') { sep = j; break; }
            }
            if (sep <= 0) continue;

            string idxStr = trimmed[..sep].Trim();
            if (!int.TryParse(idxStr, out int idx)) continue;
            if (idx != objType) continue;

            lines[i] = line;
            replaced = true;
            break;
        }

        if (!replaced)
            lines.Add(line);

        File.WriteAllText(meshListPath, string.Join(Environment.NewLine, lines));
    }

    public static void ReplaceObjTypeInMeshList(string meshListPath, int oldObjType, int newObjType, string relPath)
    {
        string newLine = $"{newObjType} {relPath.Replace('/', '\\').Replace(Path.DirectorySeparatorChar, '\\')}";

        if (!File.Exists(meshListPath))
        {
            File.WriteAllText(meshListPath, newLine);
            return;
        }

        var lines = File.ReadAllLines(meshListPath).ToList();
        bool foundOld = false;
        bool foundNew = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0) continue;

            int sep = -1;
            for (int j = 0; j < trimmed.Length; j++)
            {
                if (trimmed[j] == ' ' || trimmed[j] == '\t') { sep = j; break; }
            }
            if (sep <= 0) continue;

            string idxStr = trimmed[..sep].Trim();
            if (!int.TryParse(idxStr, out int idx)) continue;

            if (idx == oldObjType)
            {
                lines[i] = newLine;
                foundOld = true;
                foundNew = true;
                continue;
            }

            if (idx == newObjType)
            {
                if (!foundNew) lines[i] = newLine;
                else lines[i] = "";
                foundNew = true;
            }
        }

        if (!foundNew)
            lines.Add(newLine);

        File.WriteAllText(meshListPath, string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l))));
    }

    public static void RemoveObjTypeFromMeshList(string meshListPath, int objType)
    {
        if (!File.Exists(meshListPath)) return;

        var lines = File.ReadAllLines(meshListPath).ToList();
        bool changed = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0) continue;

            int sep = -1;
            for (int j = 0; j < trimmed.Length; j++)
            {
                if (trimmed[j] == ' ' || trimmed[j] == '\t') { sep = j; break; }
            }
            if (sep <= 0) continue;

            string idxStr = trimmed[..sep].Trim();
            if (!int.TryParse(idxStr, out int idx)) continue;
            if (idx != objType) continue;

            lines[i] = "";
            changed = true;
        }

        if (!changed) return;
        File.WriteAllText(meshListPath, string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l))));
    }
}
