using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using WydFormats;

// Resolve ambiguidades entre OpenTK e System
using GlPixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;
using SysBuffer     = System.Buffer;

namespace WydMapEditor;

/// <summary>
/// Renders WYD map objects (buildings, trees, decorations) loaded from .dat records
/// using .msa meshes and .wys textures.
///
/// Coordinate mapping (confirmado via TMObjectContainer.cpp + TMGround.cpp):
///   World X  = DatRecord.PosX              (já em unidades de mundo 0-127, NÃO multiplica por TILE_SIZE)
///   World Y  = DatRecord.Height * HEIGHT_SCALE  (mesma compressão de altura do TerrainRenderer)
///   World Z  = DatRecord.PosY              (idem PosX)
///   Rotation = eixo Y por DatRecord.Angle (radianos, negado para GL right-hand)
///   Mesh geo = vértices já em unidades de mundo => escala 1:1 (sem TILE_SIZE)
///
/// Ref: terrain tile nX → world X = nX * 2 (TILE_SIZE=2), mas objeto PosX é chunk-local (0-127)
///</summary>
public sealed class ObjectRenderer : IDisposable
{
    // ── Constants (must match TerrainRenderer) ───────────────────────────────
    public const float TILE_SIZE    = TerrainRenderer.TILE_SIZE;
    public const float HEIGHT_SCALE = TerrainRenderer.HEIGHT_SCALE;

    // ── GPU mesh handle ───────────────────────────────────────────────────────
    private sealed class GpuMesh : IDisposable
    {
        public int  Vao     { get; init; }
        public int  Vbo     { get; init; }
        public int  Ibo     { get; init; }
        public int  IndexCount { get; init; }
        public int[] SubTextures { get; init; } = Array.Empty<int>(); // GL tex ID per sub-mesh
        public int[] SubIdxStart { get; init; } = Array.Empty<int>(); // byte offset in IBO
        public int[] SubIdxCount { get; init; } = Array.Empty<int>(); // index count per sub-mesh
        // AABB local-space (antes de rotação/escala/translação)
        public Vector3 BoundsMin { get; init; } = Vector3.Zero;
        public Vector3 BoundsMax { get; init; } = Vector3.One;

        public void Dispose()
        {
            if (Vao != 0) GL.DeleteVertexArray(Vao);
            if (Vbo != 0) GL.DeleteBuffer(Vbo);
            if (Ibo != 0) GL.DeleteBuffer(Ibo);
        }
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private int _prog;
    private int _uMVP, _uModel, _uHasTex, _uTex, _uLightDir, _uColor, _uSelected, _uObjectColor;

    private readonly Dictionary<int,    GpuMesh> _gpuMeshes  = new();
    private readonly Dictionary<string, int>     _texCache   = new(); // texName -> GL tex ID
    private readonly HashSet<int>                _failedType  = new();
    private readonly Dictionary<int,    int>     _thumbCache = new(); // objType -> GL tex ID (thumbnail FBO render)

    private List<DatRecord>?       _objects;     // referência direta — reflete edições em tempo real
    private string                 _gameFolder = "";
    private Dictionary<int,string> _meshList   = new();
    private Dictionary<int, Dictionary<int, uint>>? _partColorsByObjectIndex;

    private bool _shadersReady;

    public int DebugRenderedObjectsLastFrame { get; private set; }
    public int DebugDistinctTypesLastFrame   { get; private set; }
    public int DebugGpuMeshesLoaded          => _gpuMeshes.Count;
    public int DebugFailedTypes              => _failedType.Count;

    /// <summary>Índice do objeto selecionado em _objects (-1 = nenhum).</summary>
    public int SelectedObjectIndex = -1;

    /// <summary>Conjunto de índices selecionados pela ferramenta Area (multi-seleção).</summary>
    public HashSet<int> AreaSelectedObjects { get; } = new();

    // ── GLSL shaders (ASCII-only strings — non-ASCII crashes some drivers) ────
    private const string VS = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNorm;
layout(location=2) in vec2 aUV;

uniform mat4 uMVP;
uniform mat4 uModel;

out vec3 vNorm;
out vec2 vUV;
out vec3 vWorldPos;

void main(){
    vec4 wp = uModel * vec4(aPos, 1.0);
    vWorldPos = wp.xyz;
    vNorm = mat3(transpose(inverse(uModel))) * aNorm;
    vUV   = aUV;
    gl_Position = uMVP * vec4(aPos, 1.0);
}";

    private const string FS = @"#version 330 core
uniform sampler2D uTex;
uniform int       uHasTex;
uniform vec3      uLightDir;
uniform vec3      uColor;
uniform int       uSelected;
uniform vec4      uObjectColor;  // multiplicador de cor por objeto (1,1,1,1 = sem efeito)

in vec3 vNorm;
in vec2 vUV;
in vec3 vWorldPos;

out vec4 fragColor;

void main(){
    vec3 N = normalize(vNorm);
    vec3 L = normalize(-uLightDir);
    float diff = max(dot(N, L), 0.0);
    float amb  = 0.35;
    float light = amb + diff * 0.65;

    vec4 col;
    if(uHasTex > 0){
        col = texture(uTex, vUV);
        if(col.a < 0.05) discard;
        col.rgb *= light;
    } else {
        col = vec4(uColor * light, 1.0);
    }

    // Aplicar cor override do objeto (equivalente ao D3DMATERIAL9.Diffuse do cliente WYD)
    col.rgb *= uObjectColor.rgb;

    // Highlight de selecao: 1=primario (amarelo vivo), 2=area secundario (laranja suave)
    if(uSelected == 1){
        col.rgb = mix(col.rgb, vec3(1.0, 0.85, 0.1), 0.65);
        col.rgb = clamp(col.rgb + vec3(0.10, 0.07, 0.0), 0.0, 1.0);
    } else if(uSelected == 2){
        col.rgb = mix(col.rgb, vec3(1.0, 0.50, 0.05), 0.38);
    }

    // simple distance fog (same as terrain)
    float fogStart = 60.0;
    float fogEnd   = 160.0;
    float dist = length(vWorldPos);
    float fog  = clamp((dist - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
    col.rgb = mix(col.rgb, vec3(0.55, 0.60, 0.65), fog * 0.7);

    fragColor = col;
}";

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configura o renderer. Aceita a List diretamente por referência — edições nos Records
    /// são refletidas automaticamente no próximo Render() sem precisar reconfigurar.
    /// </summary>
    public void Configure(string gameFolder, Dictionary<int,string> meshList, List<DatRecord> objects, Dictionary<int, Dictionary<int, uint>>? partColorsByObjectIndex)
    {
        bool folderChanged = _gameFolder != gameFolder;

        _gameFolder = gameFolder;
        _objects    = objects;   // referência direta, sem cópia
        _partColorsByObjectIndex = partColorsByObjectIndex;

        if (folderChanged)
        {
            Clear(); // textures may be from wrong folder
        }
        else
        {
            // Remove do cache de falhas qualquer ObjType que agora tem entrada na meshList
            // (ex: mesh recém-convertida que havia falhado antes)
            _failedType.ExceptWith(meshList.Keys);
        }

        _meshList = meshList;
    }

    public int GetSubMeshCount(int objType)
    {
        if (string.IsNullOrEmpty(_gameFolder)) return 0;
        if (_failedType.Contains(objType)) return 0;
        if (_gpuMeshes.TryGetValue(objType, out var gpu)) return gpu.SubIdxCount.Length;
        var built = TryBuildGpuMesh(objType);
        if (built == null) return 0;
        _gpuMeshes[objType] = built;
        return built.SubIdxCount.Length;
    }

    private void EnsureShaders()
    {
        if (_shadersReady || _prog != 0) return;
        try
        {
            int vs = CompileShader(ShaderType.VertexShader,   VS);
            int fs = CompileShader(ShaderType.FragmentShader, FS);
            _prog = GL.CreateProgram();
            GL.AttachShader(_prog, vs);
            GL.AttachShader(_prog, fs);
            GL.LinkProgram(_prog);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            GL.GetProgram(_prog, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0) { GL.DeleteProgram(_prog); _prog = 0; return; }

            _uMVP      = GL.GetUniformLocation(_prog, "uMVP");
            _uModel    = GL.GetUniformLocation(_prog, "uModel");
            _uHasTex   = GL.GetUniformLocation(_prog, "uHasTex");
            _uTex      = GL.GetUniformLocation(_prog, "uTex");
            _uLightDir = GL.GetUniformLocation(_prog, "uLightDir");
            _uColor    = GL.GetUniformLocation(_prog, "uColor");
            _uSelected = GL.GetUniformLocation(_prog, "uSelected");
            _uObjectColor = GL.GetUniformLocation(_prog, "uObjectColor");
            _shadersReady = true;
        }
        catch { /* proceed without objects */ }
    }

    private static int CompileShader(ShaderType type, string src)
    {
        int s = GL.CreateShader(type);
        GL.ShaderSource(s, src);
        GL.CompileShader(s);
        GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetShaderInfoLog(s);
            GL.DeleteShader(s);
            throw new Exception($"Shader compile error: {log}");
        }
        return s;
    }

    // ── Public render call ────────────────────────────────────────────────────

    public void Render(Matrix4 view, Matrix4 proj)
    {
        DebugRenderedObjectsLastFrame = 0;
        DebugDistinctTypesLastFrame   = 0;
        if (_objects == null || _objects.Count == 0) return;
        if (string.IsNullOrEmpty(_gameFolder)) return;

        EnsureShaders();
        if (!_shadersReady) return;

        GL.UseProgram(_prog);

        // Global light direction (same as terrain — from upper-left)
        var lightDir = new Vector3(-0.5f, -1f, -0.3f);
        GL.Uniform3(_uLightDir, lightDir);
        GL.Uniform1(_uTex, 0);

        GL.Enable(EnableCap.DepthTest);

        var seenTypes = new HashSet<int>();
        for (int objIdx = 0; objIdx < _objects.Count; objIdx++)
        {
            var rec     = _objects[objIdx];
            int objType = (int)rec.ObjType;
            seenTypes.Add(objType);

            // Skip special-case / effect types that have no mesh
            if (IsEffectType(objType)) continue;
            if (_failedType.Contains(objType)) continue;

            if (!_gpuMeshes.TryGetValue(objType, out GpuMesh? gpu))
            {
                gpu = TryBuildGpuMesh(objType);
                if (gpu == null) { _failedType.Add(objType); continue; }
                _gpuMeshes[objType] = gpu;
            }

            var model = BuildModelMatrix(rec);
            var mvp   = model * view * proj;

            GL.UniformMatrix4(_uMVP,   false, ref mvp);
            GL.UniformMatrix4(_uModel, false, ref model);
            int selVal = objIdx == SelectedObjectIndex ? 1 : AreaSelectedObjects.Contains(objIdx) ? 2 : 0;
            GL.Uniform1(_uSelected, selVal);

            uint baseRgb = rec.HasColorOverride
                ? (uint)(rec.ColorR | (rec.ColorG << 8) | (rec.ColorB << 16))
                : 0xFFFFFFu;

            // Draw sub-meshes
            GL.BindVertexArray(gpu.Vao);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, gpu.Ibo);

            // Resoler textura override (se houver) — aplica em todos os sub-meshes
            int overrideTex = string.IsNullOrEmpty(rec.TextureOverrideName)
                ? 0
                : ResolveOverrideTex(rec.TextureOverrideName);

            for (int si = 0; si < gpu.SubIdxCount.Length; si++)
            {
                uint rgb = baseRgb;
                if (_partColorsByObjectIndex != null &&
                    _partColorsByObjectIndex.TryGetValue(objIdx, out var partMap) &&
                    partMap.TryGetValue(si, out var partRgb))
                {
                    rgb = partRgb;
                }

                GL.Uniform4(_uObjectColor,
                    (rgb & 0xFF) / 255f,
                    ((rgb >> 8) & 0xFF) / 255f,
                    ((rgb >> 16) & 0xFF) / 255f,
                    1f);

                // Override prevalece sobre textura do mesh
                int texId = overrideTex != 0
                    ? overrideTex
                    : (si < gpu.SubTextures.Length ? gpu.SubTextures[si] : 0);

                if (texId != 0)
                {
                    GL.Uniform1(_uHasTex, 1);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, texId);
                }
                else
                {
                    GL.Uniform1(_uHasTex, 0);
                    // Fallback color based on ObjType category
                    var col = CategoryColor(objType);
                    GL.Uniform3(_uColor, col);
                }

                GL.DrawElements(PrimitiveType.Triangles,
                    gpu.SubIdxCount[si],
                    DrawElementsType.UnsignedInt,
                    gpu.SubIdxStart[si] * sizeof(uint));
            }

            GL.BindVertexArray(0);
            DebugRenderedObjectsLastFrame++;
        }

        DebugDistinctTypesLastFrame = seenTypes.Count;
        GL.UseProgram(0);
    }

    // ── GPU mesh builder ──────────────────────────────────────────────────────

    private GpuMesh? TryBuildGpuMesh(int objType)
    {
        if (!_meshList.TryGetValue(objType, out string? relPath))
        {
            
            return null;
        }

        // Path.Combine retorna o segundo arg intacto quando é absoluto.
        // Path.GetFullPath resolve eventuais ".." residuais.
        string absPath = Path.GetFullPath(Path.Combine(_gameFolder, relPath));
        

        // If path has no extension, add .msa (MeshList.txt sometimes omits extension)
        if (string.IsNullOrEmpty(Path.GetExtension(absPath)))
            absPath += ".msa";

        // Fallback: se não existe, tenta com pasta-pai do gameFolder
        // (cobre o caso em que o relPath foi gravado relativo à pasta Env e não à raiz do cliente)
        if (!File.Exists(absPath))
        {
            string? parentFolder = Directory.GetParent(_gameFolder)?.FullName;
            if (parentFolder != null)
            {
                string alt = Path.GetFullPath(Path.Combine(parentFolder, relPath));
                if (string.IsNullOrEmpty(Path.GetExtension(alt))) alt += ".msa";
                if (File.Exists(alt))
                {
                    
                    absPath = alt;
                }
            }
        }

        // TMMesh.LoadMsa() tries "foo_off.msa" first, then "foo.msa"
        string meshDir   = Path.GetDirectoryName(absPath) ?? _gameFolder;
        string nameNoExt = Path.GetFileNameWithoutExtension(absPath);
        string offPath   = Path.Combine(meshDir, nameNoExt + "_off.msa");

        if (File.Exists(offPath))
        {
            absPath = offPath;
            
        }
        else if (!File.Exists(absPath))
        {
            
            return null;
        }

        
        MshMesh? mesh = MshLoader.Load(absPath);
        if (mesh == null || mesh.Positions.Length == 0)
        {
            
            return null;
        }
        

        // Nome base do .msa para usar como fallback de textura (mesmo que TMMesh.cpp faz)
        string meshBaseName = Path.GetFileNameWithoutExtension(absPath);

        try { return UploadMesh(mesh, absPath, meshBaseName); }
        catch { return null; }
    }

    private GpuMesh UploadMesh(MshMesh mesh, string absPath, string meshBaseName = "")
    {
        // Build interleaved VBO: [pos.xyz | norm.xyz | uv.xy] = 8 floats = 32 bytes
        int vCount  = mesh.Positions.Length / 3;
        var vboData = new float[vCount * 8];

        // Calcular AABB local-space
        float bMinX = float.MaxValue, bMinY = float.MaxValue, bMinZ = float.MaxValue;
        float bMaxX = float.MinValue, bMaxY = float.MinValue, bMaxZ = float.MinValue;

        for (int i = 0; i < vCount; i++)
        {
            int vi = i * 8;
            int pi = i * 3;
            int ti = i * 2;
            float px = mesh.Positions[pi + 0];
            float py = mesh.Positions[pi + 1];
            float pz = mesh.Positions[pi + 2];
            vboData[vi + 0] = px;
            vboData[vi + 1] = py;
            vboData[vi + 2] = pz;
            vboData[vi + 3] = mesh.Normals[pi + 0];
            vboData[vi + 4] = mesh.Normals[pi + 1];
            vboData[vi + 5] = mesh.Normals[pi + 2];
            vboData[vi + 6] = mesh.TexCoords[ti + 0];
            vboData[vi + 7] = mesh.TexCoords[ti + 1];

            if (px < bMinX) bMinX = px; if (px > bMaxX) bMaxX = px;
            if (py < bMinY) bMinY = py; if (py > bMaxY) bMaxY = py;
            if (pz < bMinZ) bMinZ = pz; if (pz > bMaxZ) bMaxZ = pz;
        }

        // Garante AABB válida mesmo com mesh vazia
        if (vCount == 0) { bMinX = bMinY = bMinZ = -0.5f; bMaxX = bMaxY = bMaxZ = 0.5f; }

        // Convert uint[] indices to int[] for GL
        var iboData = new uint[mesh.Indices.Length];
        Array.Copy(mesh.Indices, iboData, mesh.Indices.Length);

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ibo = GL.GenBuffer();

        GL.BindVertexArray(vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vboData.Length * sizeof(float), vboData,
                      BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, iboData.Length * sizeof(uint), iboData,
                      BufferUsageHint.StaticDraw);

        const int stride = 8 * sizeof(float);
        // attrib 0: position (vec3 at offset 0)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        // attrib 1: normal (vec3 at offset 12)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 12);
        // attrib 2: uv (vec2 at offset 24)
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 24);

        GL.BindVertexArray(0);

        // ── Per-sub-mesh texture and draw ranges ──
        int subCount = mesh.SubMeshes.Length;
        var subTextures = new int[subCount];
        var subIdxStart = new int[subCount];
        var subIdxCount = new int[subCount];

        // Determine mesh folder for texture lookup
        string meshDir = Path.GetDirectoryName(absPath) ?? _gameFolder;

        for (int si = 0; si < subCount; si++)
        {
            var sub = mesh.SubMeshes[si];
            subIdxStart[si] = sub.FaceStart * 3;
            subIdxCount[si] = sub.FaceCount * 3;

            string texName = si < mesh.TextureNames.Length ? mesh.TextureNames[si] : "";
            int texId = LoadMeshTexture(texName, meshDir);

            // Fallback igual ao TMMesh.cpp: se a textura do campo MSA falhou,
            // tenta usar o nome do arquivo .msa como textura (ex: wall43 → wall43.wys)
            if (texId == 0 && !string.IsNullOrEmpty(meshBaseName))
                texId = LoadMeshTexture(meshBaseName, meshDir);

            subTextures[si] = texId;
        }

        return new GpuMesh
        {
            Vao          = vao,
            Vbo          = vbo,
            Ibo          = ibo,
            IndexCount   = iboData.Length,
            SubTextures  = subTextures,
            SubIdxStart  = subIdxStart,
            SubIdxCount  = subIdxCount,
            BoundsMin    = new Vector3(bMinX, bMinY, bMinZ),
            BoundsMax    = new Vector3(bMaxX, bMaxY, bMaxZ),
        };
    }

    // ── Texture loading ────────────────────────────────────────────────────────

    private int LoadMeshTexture(string texName, string meshDir)
    {
        if (string.IsNullOrEmpty(texName)) return 0;
        if (_texCache.TryGetValue(texName, out int cached)) return cached;

        // Diretórios de busca: pasta do mesh, Mesh/CustomMeshes/, Mesh/, mesh/, Effect/, effect/
        var searchDirs = new[]
        {
            meshDir,
            Path.Combine(_gameFolder, "Mesh", "CustomMeshes"),
            Path.Combine(_gameFolder, "mesh", "CustomMeshes"),
            Path.Combine(_gameFolder, "Mesh"),
            Path.Combine(_gameFolder, "mesh"),
            Path.Combine(_gameFolder, "Effect"),
            Path.Combine(_gameFolder, "effect"),
        };

        // Variações de nome para busca case-insensitive (Windows já é, mas cobre outros casos)
        var nameVariants = new[] { texName, texName.ToLower(), texName.ToUpper() };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var variant in nameVariants)
            {
                // Tentar .wys primeiro (formato nativo WYD)
                string wysPath = Path.Combine(dir, variant + ".wys");
                if (File.Exists(wysPath))
                {
                    int tex = TryLoadWys(wysPath);
                    _texCache[texName] = tex;
                    return tex;
                }
            }

            // Fallback para .png / .jpg / .tga
            foreach (var variant in nameVariants)
            {
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp", ".gif",
                                            ".PNG", ".JPG", ".JPEG", ".TGA", ".WEBP", ".BMP", ".GIF" })
                {
                    string p = Path.Combine(dir, variant + ext);
                    if (File.Exists(p))
                    {
                        int tex = TryLoadBitmapTex(p);
                        _texCache[texName] = tex;
                        return tex;
                    }
                }
            }
        }

        _texCache[texName] = 0; // marca como falhou
        return 0;
    }

    private static int TryLoadWys(string path)
    {
        try
        {
            var res = WysLoader.Decode(path);
            if (res == null) return 0;
            var (pixels, w, h) = res.Value;
            return UploadTexture(pixels, w, h, flipY: true);
        }
        catch { return 0; }
    }

    private static int TryLoadBitmapTex(string path)
    {
        try
        {
            using var bmp   = new System.Drawing.Bitmap(path);
            using var bmp32 = bmp.Clone(
                new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bd = bmp32.LockBits(
                new System.Drawing.Rectangle(0, 0, bmp32.Width, bmp32.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(bd.Stride) * bmp32.Height;
            var data = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, data, 0, bytes);
            bmp32.UnlockBits(bd);
            // BGRA -> RGBA
            for (int i = 0; i < data.Length; i += 4)
            { byte b = data[i]; data[i] = data[i + 2]; data[i + 2] = b; }
            return UploadTexture(data, bmp32.Width, bmp32.Height, flipY: true);
        }
        catch { return 0; }
    }

    private static int UploadTexture(byte[] data, int w, int h, bool flipY)
    {
        if (flipY) FlipY(data, w, h);
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            w, h, 0, GlPixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.Repeat);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    private static void FlipY(byte[] data, int w, int h)
    {
        int row = w * 4;
        var tmp = new byte[row];
        for (int y = 0; y < h / 2; y++)
        {
            int t = y * row, b = (h - 1 - y) * row;
            SysBuffer.BlockCopy(data, t, tmp,    0, row);
            SysBuffer.BlockCopy(data, b, data,   t, row);
            SysBuffer.BlockCopy(tmp,  0, data,   b, row);
        }
    }

    // ── Thumbnail rendering (FBO off-screen) ──────────────────────────────────

    /// <summary>
    /// Renders a <paramref name="size"/>×<paramref name="size"/> thumbnail of the given ObjType
    /// into an off-screen FBO and returns the resulting GL texture ID (suitable for ImGui.Image).
    /// The texture is cached — subsequent calls for the same objType return immediately.
    /// Returns 0 if the mesh could not be loaded or shaders are not ready.
    /// Must be called while an OpenGL context is current (safe to call during ImGui frame build).
    /// </summary>
    public int RenderThumbnail(int objType, int size = 128)
    {
        if (_thumbCache.TryGetValue(objType, out int cached)) return cached;

        EnsureShaders();
        if (!_shadersReady)      { _thumbCache[objType] = 0; return 0; }
        if (IsEffectType(objType)) { _thumbCache[objType] = 0; return 0; }

        // Ensure mesh is loaded (reuse already-built GpuMesh if present)
        if (!_gpuMeshes.TryGetValue(objType, out GpuMesh? gpu))
        {
            gpu = TryBuildGpuMesh(objType);
            if (gpu == null) { _failedType.Add(objType); _thumbCache[objType] = 0; return 0; }
            _gpuMeshes[objType] = gpu;
        }

        // ── Create color texture + FBO ──
        int colorTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, colorTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            size, size, 0, GlPixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        int fbo   = GL.GenFramebuffer();
        int depth = GL.GenRenderbuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, size, size);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, depth);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, colorTex, 0);

        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.DeleteFramebuffer(fbo);
            GL.DeleteRenderbuffer(depth);
            GL.DeleteTexture(colorTex);
            _thumbCache[objType] = 0;
            return 0;
        }

        // ── Save GL state ──
        int[] savedVp = new int[4];
        GL.GetInteger(GetPName.Viewport, savedVp);
        bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);

        // ── Render to FBO ──
        GL.Viewport(0, 0, size, size);
        GL.ClearColor(0.10f, 0.12f, 0.18f, 1.0f); // dark blue-grey background
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);

        // Camera: 3/4 angle above-right-front, looking at mesh's local AABB center
        var center  = (gpu.BoundsMin + gpu.BoundsMax) * 0.5f;
        var extents = (gpu.BoundsMax - gpu.BoundsMin) * 0.5f;
        float radius = extents.Length;
        if (radius < 0.001f) radius = 1f;

        var camDir = Vector3.Normalize(new Vector3(1.2f, 1.0f, 1.5f));
        var camPos = center + camDir * (radius * 2.8f);

        var view = Matrix4.LookAt(camPos, center, Vector3.UnitY);
        var proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(45f),
            1.0f,              // square viewport
            radius * 0.01f,
            radius * 30f);

        GL.UseProgram(_prog);
        var model = Matrix4.Identity;
        var mvp   = model * view * proj;
        GL.UniformMatrix4(_uMVP,   false, ref mvp);
        GL.UniformMatrix4(_uModel, false, ref model);
        GL.Uniform3(_uLightDir, new Vector3(-0.5f, -1f, -0.3f));
        GL.Uniform1(_uTex,      0);
        GL.Uniform1(_uSelected, 0);
        GL.Uniform4(_uObjectColor, 1f, 1f, 1f, 1f); // thumbnails sem override de cor

        GL.BindVertexArray(gpu.Vao);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, gpu.Ibo);

        for (int si = 0; si < gpu.SubIdxCount.Length; si++)
        {
            int texId = si < gpu.SubTextures.Length ? gpu.SubTextures[si] : 0;
            if (texId != 0)
            {
                GL.Uniform1(_uHasTex, 1);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, texId);
            }
            else
            {
                GL.Uniform1(_uHasTex, 0);
                GL.Uniform3(_uColor, CategoryColor(objType));
            }
            GL.DrawElements(PrimitiveType.Triangles,
                gpu.SubIdxCount[si],
                DrawElementsType.UnsignedInt,
                gpu.SubIdxStart[si] * sizeof(uint));
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);

        // ── Restore GL state ──
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(savedVp[0], savedVp[1], savedVp[2], savedVp[3]);
        if (!depthWasEnabled) GL.Disable(EnableCap.DepthTest);
        GL.ClearColor(0f, 0f, 0f, 1f);

        // Destroy FBO + depth renderbuffer — keep color texture for ImGui rendering
        GL.DeleteFramebuffer(fbo);
        GL.DeleteRenderbuffer(depth);

        _thumbCache[objType] = colorTex;
        return colorTex;
    }

    // ── Picking público ────────────────────────────────────────────────────────

    /// <summary>
    /// Ray-AABB picking. Devolve o índice do objeto mais próximo interceptado pelo raio,
    /// ou -1 se nenhum foi atingido.
    /// </summary>
    public int TryPickObject(Vector3 rayOrigin, Vector3 rayDir)
    {
        if (_objects == null) return -1;

        float bestT   = float.MaxValue;
        int   bestIdx = -1;

        for (int i = 0; i < _objects.Count; i++)
        {
            var rec     = _objects[i];
            int objType = (int)rec.ObjType;
            if (IsEffectType(objType)) continue;
            if (_failedType.Contains(objType)) continue;
            if (!_gpuMeshes.TryGetValue(objType, out var gpu)) continue;

            var model = BuildModelMatrix(rec);
            var (wMin, wMax) = TransformAABB(gpu.BoundsMin, gpu.BoundsMax, model);

            if (RayIntersectsAABB(rayOrigin, rayDir, wMin, wMax, out float t) && t < bestT)
            {
                bestT   = t;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    /// <summary>
    /// Picking por retângulo de tela: projeta a AABB de cada objeto para 2D e
    /// verifica se clickPx está dentro do retângulo projetado. Retorna o índice
    /// do objeto mais próximo da câmera cujo retângulo contém o clique, ou -1.
    /// clickPx e vpSize devem estar em coordenadas relativas ao viewport (0..vpSize).
    /// </summary>
    public int TryPickScreenBounds(Matrix4 viewProj, Vector2 clickPx, Vector2 vpSize)
    {
        if (_objects == null) return -1;

        float bestDepth = float.MaxValue;
        int   bestIdx   = -1;

        for (int i = 0; i < _objects.Count; i++)
        {
            var rec     = _objects[i];
            int objType = (int)rec.ObjType;
            if (IsEffectType(objType)) continue;

            // AABB world-space — usa bounds da mesh se disponível, caso contrário esfera genérica
            Vector3 wMin, wMax;
            if (_gpuMeshes.TryGetValue(objType, out var gpu))
            {
                var model = BuildModelMatrix(rec);
                (wMin, wMax) = TransformAABB(gpu.BoundsMin, gpu.BoundsMax, model);
                // padding mínimo
                var pad3 = new Vector3(1f, 1f, 1f);
                wMin -= pad3; wMax += pad3;
            }
            else
            {
                // Sem mesh carregada: caixa genérica compacta ao redor do centro
                float wx = rec.PosX, wy = rec.Height * HEIGHT_SCALE, wz = rec.PosY;
                wMin = new Vector3(wx - 3f, wy,       wz - 3f);
                wMax = new Vector3(wx + 3f, wy + 6f,  wz + 3f);
            }

            // Projeta os 8 cantos e calcula bounding rect 2D
            float sMinX = float.MaxValue, sMinY = float.MaxValue;
            float sMaxX = float.MinValue, sMaxY = float.MinValue;
            float depth = float.MaxValue;
            bool  anyVis = false;

            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new(wMin.X, wMin.Y, wMin.Z), new(wMax.X, wMin.Y, wMin.Z),
                new(wMin.X, wMax.Y, wMin.Z), new(wMax.X, wMax.Y, wMin.Z),
                new(wMin.X, wMin.Y, wMax.Z), new(wMax.X, wMin.Y, wMax.Z),
                new(wMin.X, wMax.Y, wMax.Z), new(wMax.X, wMax.Y, wMax.Z),
            };

            foreach (var c in corners)
            {
                var clip = new Vector4(c.X, c.Y, c.Z, 1f) * viewProj;
                if (clip.W <= 0.001f) continue;
                float sx = ( clip.X / clip.W * 0.5f + 0.5f) * vpSize.X;
                float sy = (-clip.Y / clip.W * 0.5f + 0.5f) * vpSize.Y;
                if (sx < sMinX) sMinX = sx; if (sx > sMaxX) sMaxX = sx;
                if (sy < sMinY) sMinY = sy; if (sy > sMaxY) sMaxY = sy;
                float d = clip.Z / clip.W; if (d < depth) depth = d;
                anyVis = true;
            }

            if (!anyVis || depth >= bestDepth) continue;

            // Padding mínimo: 8px para facilitar seleção de objetos pequenos
            float padX = Math.Max(8f, (sMaxX - sMinX) * 0.05f);
            float padY = Math.Max(8f, (sMaxY - sMinY) * 0.05f);

            if (clickPx.X >= sMinX - padX && clickPx.X <= sMaxX + padX &&
                clickPx.Y >= sMinY - padY && clickPx.Y <= sMaxY + padY)
            {
                bestDepth = depth;
                bestIdx   = i;
            }
        }

        return bestIdx;
    }

    /// <summary>
    /// Devolve o índice do objeto cujo centro projetado na tela é mais próximo
    /// de clickPx, se dentro do raio maxRadiusPx. Útil como fallback de picking.
    /// </summary>
    public int TryPickNearest(Matrix4 viewProj, Vector2 clickPx, Vector2 vpSize, float maxRadiusPx = 48f)
    {
        if (_objects == null) return -1;

        float bestDist = maxRadiusPx * maxRadiusPx;
        int   bestIdx  = -1;

        for (int i = 0; i < _objects.Count; i++)
        {
            var rec     = _objects[i];
            int objType = (int)rec.ObjType;
            if (IsEffectType(objType)) continue;

            // Centro do objeto em world-space
            float wx = rec.PosX;
            float wy = rec.Height * HEIGHT_SCALE;
            float wz = rec.PosY;
            var center = new Vector4(wx, wy, wz, 1f) * viewProj;
            if (center.W <= 0.001f) continue;

            float sx = ( center.X / center.W * 0.5f + 0.5f) * vpSize.X;
            float sy = (-center.Y / center.W * 0.5f + 0.5f) * vpSize.Y;

            float dx = sx - clickPx.X, dy = sy - clickPx.Y;
            float distSq = dx * dx + dy * dy;
            if (distSq < bestDist)
            {
                bestDist = distSq;
                bestIdx  = i;
            }
        }

        return bestIdx;
    }

    /// <summary>
    /// Devolve a AABB world-space do objeto no índice dado.
    /// Retorna false se o índice for inválido ou a mesh não estiver carregada.
    /// </summary>
    public bool TryGetWorldBounds(int objIdx, out Vector3 wMin, out Vector3 wMax)
    {
        wMin = wMax = Vector3.Zero;
        if (_objects == null || objIdx < 0 || objIdx >= _objects.Count) return false;
        var rec     = _objects[objIdx];
        int objType = (int)rec.ObjType;
        if (!_gpuMeshes.TryGetValue(objType, out var gpu)) return false;
        var model = BuildModelMatrix(rec);
        (wMin, wMax) = TransformAABB(gpu.BoundsMin, gpu.BoundsMax, model);
        return true;
    }

    // ── Helpers de picking ────────────────────────────────────────────────────

    /// <summary>Constrói a model matrix idêntica à usada em Render().</summary>
    private static Matrix4 BuildModelMatrix(in DatRecord rec)
    {
        float sh = rec.HasScale ? rec.ScaleH : 1f;
        float sv = rec.HasScale ? rec.ScaleV : 1f;
        float wx = rec.PosX;
        float wy = rec.Height * HEIGHT_SCALE;
        float wz = rec.PosY;
        var rotPitch = Matrix4.CreateRotationX(-MathF.PI / 2f);
        var rotYaw   = Matrix4.CreateRotationY(-rec.Angle);
        return rotPitch * rotYaw
             * Matrix4.CreateScale(sh, sv, sh)
             * Matrix4.CreateTranslation(wx, wy, wz);
    }

    /// <summary>
    /// Transforma uma AABB local em AABB world-space via model matrix.
    /// Transforma os 8 cantos e calcula min/max.
    /// </summary>
    private static (Vector3 Min, Vector3 Max) TransformAABB(Vector3 bMin, Vector3 bMax, Matrix4 model)
    {
        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(bMin.X, bMin.Y, bMin.Z), new(bMax.X, bMin.Y, bMin.Z),
            new(bMin.X, bMax.Y, bMin.Z), new(bMax.X, bMax.Y, bMin.Z),
            new(bMin.X, bMin.Y, bMax.Z), new(bMax.X, bMin.Y, bMax.Z),
            new(bMin.X, bMax.Y, bMax.Z), new(bMax.X, bMax.Y, bMax.Z),
        };
        var wMin = new Vector3(float.MaxValue);
        var wMax = new Vector3(float.MinValue);
        foreach (var c in corners)
        {
            var w = new Vector4(c.X, c.Y, c.Z, 1f) * model;
            var p = w.Xyz / w.W;
            wMin = Vector3.ComponentMin(wMin, p);
            wMax = Vector3.ComponentMax(wMax, p);
        }
        return (wMin, wMax);
    }

    /// <summary>
    /// Teste de intersecção raio vs AABB (slab method).
    /// Devolve true e o valor t se houver hit com t >= 0.
    /// </summary>
    private static bool RayIntersectsAABB(Vector3 origin, Vector3 dir,
                                          Vector3 bMin,   Vector3 bMax, out float tMin)
    {
        tMin = 0f;
        float tMax = float.MaxValue;

        for (int axis = 0; axis < 3; axis++)
        {
            float o  = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d  = axis == 0 ? dir.X    : axis == 1 ? dir.Y    : dir.Z;
            float mn = axis == 0 ? bMin.X   : axis == 1 ? bMin.Y   : bMin.Z;
            float mx = axis == 0 ? bMax.X   : axis == 1 ? bMax.Y   : bMax.Z;

            if (Math.Abs(d) < 1e-8f)
            {
                if (o < mn || o > mx) return false;
            }
            else
            {
                float t1 = (mn - o) / d;
                float t2 = (mx - o) / d;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Math.Max(tMin, t1);
                tMax = Math.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
        }

        return tMin >= 0f;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Chamado após alterar cores de objetos para sinalizar que a GPU precisa ser atualizada.
    /// Na implementação atual o shader lê os uniforms frame-a-frame, então não há buffer para invalidar.
    /// O método existe para compatibilidade com chamadas do MainWindow.</summary>
    public void MarkColorsDirty() { /* uniforms são reenviados a cada frame — noop */ }

    /// <summary>
    /// Resolve o GL texture ID para um nome de textura override.
    /// Usa o mesmo _texCache e as mesmas buscas de LoadMeshTexture.
    /// </summary>
    private int ResolveOverrideTex(string texName)
    {
        if (string.IsNullOrEmpty(texName)) return 0;
        // Tenta do cache primeiro
        if (_texCache.TryGetValue("_override_" + texName, out int cached)) return cached;
        // Busca nas pastas padrão do gameFolder
        var searchDirs = new[]
        {
            _gameFolder,
            Path.Combine(_gameFolder, "Mesh"),
            Path.Combine(_gameFolder, "mesh"),
            Path.Combine(_gameFolder, "Env"),
            Path.Combine(_gameFolder, "env"),
            Path.Combine(_gameFolder, "Effect"),
            Path.Combine(_gameFolder, "effect"),
            Path.Combine(_gameFolder, "Texture"),
            Path.Combine(_gameFolder, "texture"),
        };
        // Tenta o pai do gameFolder também (EnvFolder)
        string? parent = Directory.GetParent(_gameFolder)?.FullName;
        if (parent != null)
        {
            searchDirs = searchDirs.Append(parent)
                .Append(Path.Combine(parent, "Env"))
                .Append(Path.Combine(parent, "env"))
                .Append(Path.Combine(parent, "Texture"))
                .ToArray();
        }

        int texId = 0;
        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            string wysPath = Path.Combine(dir, texName + ".wys");
            if (File.Exists(wysPath)) { texId = TryLoadWys(wysPath); if (texId != 0) break; }
            foreach (var ext in new[] { ".png", ".jpg", ".tga", ".bmp" })
            {
                string p = Path.Combine(dir, texName + ext);
                if (File.Exists(p)) { texId = TryLoadBitmapTex(p); if (texId != 0) break; }
            }
            if (texId != 0) break;
        }
        _texCache["_override_" + texName] = texId;
        return texId;
    }

    /// <summary>Types that have no static mesh (effects, particles, entities).</summary>
    private static bool IsEffectType(int t)
        => t == 1 || t == 2 || t == 3 || t == 4 || t == 5 || t == 6 || t == 7 || t == 12
        || (t >= 311 && t <= 322)
        // Nota: 343 (bf0101.msa) e 344 (fs0101.msa) têm mesh estático — NÃO são efeitos
        || (t >= 501 && t <= 510)
        || t == 121;

    /// <summary>
    /// Solid fallback colour used when no texture is available.
    /// Groups objects visually by type.
    /// </summary>
    private static Vector3 CategoryColor(int t)
    {
        if (t >= 331 && t <= 342 || t >= 351 && t <= 378) return new Vector3(0.2f, 0.6f, 0.2f); // trees - green
        if (t >= 251 && t <= 254) return new Vector3(0.7f, 0.5f, 0.3f); // houses - brown
        if (t >= 100 && t <= 200) return new Vector3(0.6f, 0.6f, 0.7f); // walls/structures - grey
        return new Vector3(0.65f, 0.60f, 0.55f); // generic
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void Clear()
    {
        foreach (var m in _gpuMeshes.Values) m.Dispose();
        _gpuMeshes.Clear();
        foreach (var t in _texCache.Values)
            if (t != 0) GL.DeleteTexture(t);
        _texCache.Clear();
        foreach (var t in _thumbCache.Values)
            if (t != 0) GL.DeleteTexture(t);
        _thumbCache.Clear();
        _failedType.Clear();
    }

    public void Dispose()
    {
        Clear();
        if (_prog != 0) { GL.DeleteProgram(_prog); _prog = 0; }
        _shadersReady = false;
    }

    // ── Stats ──────────────────────────────────────────────────────────────────
    public int MeshCount    => _gpuMeshes.Count;
    public int TextureCount => _texCache.Count(kv => kv.Value != 0);
    public int ObjectCount  => _objects?.Count ?? 0;
}
