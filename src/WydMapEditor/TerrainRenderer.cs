using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using WydFormats;

namespace WydMapEditor;

/// <summary>
/// Renderizador de terreno OpenGL.
/// • Gera mesh 65×65 vértices a partir do TRN (alturas suaves).
/// • Texturas reais via Texture2DArray (128×128 por camada, até 256 tiles).
/// • Tile map 64×64 armazena índice por tile → lookup no fragment shader.
/// • Iluminação Phong simples (ambient + directional) + névoa.
/// • Render-to-texture via FBO → exibido como ImGui Image.
/// </summary>
public sealed class TerrainRenderer : IDisposable
{
    // ── Constantes ──────────────────────────────────────────────────────────
    public const float TILE_SIZE    = 2f;
    public const float HEIGHT_SCALE = 0.25f;
    private const int  GRID         = 64;
    private const int  VERTS        = GRID + 1; // 65×65
    private const int  TEX_SIZE     = 128;      // largura/altura de cada tile
    private const int  MAX_LAYERS   = 256;      // máximo de camadas no array

    // ── OpenGL handles — mesh ───────────────────────────────────────────────
    private int _vao, _vbo, _ebo;
    private int _shaderProgram;
    private int _fbo, _colorTex, _depthRbo;
    private int _vpW, _vpH;

    // ── OpenGL handles — texturas ───────────────────────────────────────────
    private int _texArray;      // Texture2DArray 128×128×256 RGBA8
    private int _tileMapTex;    // Texture2D 64×64 R8 — índice do tile por célula
    private int _attrOverlayTex; // Texture2D 64×64 RGBA8 — overlay do AttributeMap (opcional)

    // ── Uniform locations ───────────────────────────────────────────────────
    private int _uMvp, _uModel, _uLightDir, _uAmbient, _uFogColor, _uFogDist;
    private int _uTexArray, _uTileMap, _uHasTex;
    private int _uAttrOverlay, _uHasAttrOverlay;

    // ── Estado ──────────────────────────────────────────────────────────────
    private TrnFile?          _trn;
    private TileTextureCache? _tileCache;
    private int               _indexCount;
    private bool              _meshDirty    = true;
    private bool              _tileMapDirty = false;
    private bool              _texArrayDirty = true;

    // ── Paleta fallback (sem textura) ────────────────────────────────────────
    private static readonly Vector4[] s_palette = BuildPalette();

    // ── FBO texture pública ─────────────────────────────────────────────────
    public int  ColorTexture => _colorTex;
    public int  Fbo          => _fbo;
    public bool ShowGrid     { get; set; } = true;
    public bool WireFrame    { get; set; } = false;
    public bool ShowAttributeOverlay { get; set; } = false;

    public void SetAttributeOverlayTexture(int texId) => _attrOverlayTex = texId;

    // ────────────────────────────────────────────────────────────────────────
    public TerrainRenderer()
    {
        CompileShaders();
        AllocateMesh();
        CreateFbo(1, 1);
    }

    // ── API pública ─────────────────────────────────────────────────────────
    public void SetTrn(TrnFile trn)
    {
        if (ReferenceEquals(_trn, trn)) return;
        _trn = trn;
        _meshDirty = true;
    }


    public void SetTileCache(TileTextureCache? cache)
    {
        _tileCache     = cache;
        _texArrayDirty = true;
    }

    public void Render(Camera3D cam, int vpW, int vpH)
    {
        if (vpW < 1 || vpH < 1) return;
        ResizeFboIfNeeded(vpW, vpH);

        // Lazy: constrói mesh e texturas quando necessário
        if (_meshDirty)    UploadMesh();
        if (_texArrayDirty) BuildTexArray();
        if (_tileMapDirty)  { BuildTileMap(); _tileMapDirty = false; }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.Viewport(0, 0, vpW, vpH);
        GL.ClearColor(0.13f, 0.13f, 0.15f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);

        if (_trn == null || _indexCount == 0)
        {
            GL.Disable(EnableCap.CullFace);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            return;
        }

        var mvp   = cam.ViewMatrix * cam.ProjectionMatrix((float)vpW / vpH);
        var model = Matrix4.Identity;

        GL.UseProgram(_shaderProgram);
        GL.UniformMatrix4(_uMvp,   false, ref mvp);
        GL.UniformMatrix4(_uModel, false, ref model);
        GL.Uniform3(_uLightDir, -0.6f, -1.0f, -0.5f);
        GL.Uniform4(_uAmbient,  0.35f,  0.35f,  0.38f, 1f);
        GL.Uniform4(_uFogColor, 0.13f,  0.13f,  0.15f, 1f);
        GL.Uniform1(_uFogDist, 350f);

        // Bind Texture2DArray (unit 0)
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2DArray, _texArray);
        GL.Uniform1(_uTexArray, 0);

        // Bind tile map (unit 1)
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _tileMapTex);
        GL.Uniform1(_uTileMap, 1);

        bool hasTex = _texArray != 0 && _tileMapTex != 0;
        GL.Uniform1(_uHasTex, hasTex ? 1 : 0);

        bool hasAttrOv = ShowAttributeOverlay && _attrOverlayTex != 0;
        GL.Uniform1(_uHasAttrOverlay, hasAttrOv ? 1 : 0);
        if (hasAttrOv)
        {
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, _attrOverlayTex);
            GL.Uniform1(_uAttrOverlay, 2);
        }

        GL.BindVertexArray(_vao);

        if (WireFrame) GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
        if (WireFrame) GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

        GL.BindVertexArray(0);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        GL.UseProgram(0);
        GL.Disable(EnableCap.CullFace);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public static Vector4 TileColor(int tileIndex) => s_palette[tileIndex & 255];

    // ── Texture2DArray ───────────────────────────────────────────────────────
    private void BuildTexArray()
    {
        _texArrayDirty = false;
        if (_tileCache == null) return;

        // Deletar array anterior
        if (_texArray != 0) { GL.DeleteTexture(_texArray); _texArray = 0; }

        _texArray = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, _texArray);

        // Aloca o array inteiro de uma vez (todos os layers com zeros)
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8,
            TEX_SIZE, TEX_SIZE, MAX_LAYERS, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

        // Sobe pixel data de cada tile que o cache tem
        for (int i = 0; i < MAX_LAYERS; i++)
        {
            var res = _tileCache.GetPixels(i);
            if (res == null) continue;

            var (rgba, w, h) = res.Value;

            // Escalar para 128×128 se necessário
            byte[] data = (w == TEX_SIZE && h == TEX_SIZE)
                ? rgba
                : WysLoader.ScaleTo(rgba, w, h, TEX_SIZE, TEX_SIZE);

            GL.TexSubImage3D(TextureTarget.Texture2DArray, 0,
                0, 0, i,              // xoffset, yoffset, layer
                TEX_SIZE, TEX_SIZE, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.Repeat);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2DArray);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    // ── Tile map 64×64 ──────────────────────────────────────────────────────
    private void BuildTileMap()
    {
        if (_trn == null) return;

        // Um byte por tile: índice 0..255
        var data = new byte[GRID * GRID];
        for (int ty = 0; ty < GRID; ty++)
            for (int tx = 0; tx < GRID; tx++)
                data[ty * GRID + tx] = _trn.Tiles[tx + ty * GRID].TileIndex;

        if (_tileMapTex == 0)
        {
            _tileMapTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _tileMapTex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
        }
        else
            GL.BindTexture(TextureTarget.Texture2D, _tileMapTex);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
            GRID, GRID, 0, PixelFormat.Red, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    // ── Mesh ────────────────────────────────────────────────────────────────
    private void AllocateMesh()
    {
        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);

        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        _ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);

        // Stride: pos(3f) + normal(3f) + color(4f) = 10 floats = 40 bytes
        const int stride = 10 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        GL.BindVertexArray(0);
    }

    private void UploadMesh()
    {
        _meshDirty = false;
        if (_trn == null) return;

        int nVerts = VERTS * VERTS;
        var verts  = new float[nVerts * 10];
        var idxBuf = new List<uint>(GRID * GRID * 6);

        // ── Alturas suaves nos vértices de canto ────────────────────────────
        var vertH = new float[VERTS * VERTS];
        for (int vy = 0; vy < VERTS; vy++)
        {
            for (int vx = 0; vx < VERTS; vx++)
            {
                float sum = 0f; int cnt = 0;
                for (int dy = -1; dy <= 0; dy++)
                {
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        int tx = vx + dx, ty = vy + dy;
                        if (tx >= 0 && tx < GRID && ty >= 0 && ty < GRID)
                        {
                            sum += _trn.Tiles[tx + ty * GRID].Height * HEIGHT_SCALE;
                            cnt++;
                        }
                    }
                }
                vertH[vx + vy * VERTS] = cnt > 0 ? sum / cnt : 0f;
            }
        }

        // ── Preenche vértices ─────────────────────────────────────────────
        for (int vy = 0; vy < VERTS; vy++)
        {
            for (int vx = 0; vx < VERTS; vx++)
            {
                int vi = (vx + vy * VERTS) * 10;
                float px = vx * TILE_SIZE;
                float py = vertH[vx + vy * VERTS];
                float pz = vy * TILE_SIZE;

                float l = vx > 0    ? vertH[(vx-1) + vy*VERTS]  : py;
                float r = vx < GRID ? vertH[(vx+1) + vy*VERTS]  : py;
                float u = vy > 0    ? vertH[vx + (vy-1)*VERTS]  : py;
                float d = vy < GRID ? vertH[vx + (vy+1)*VERTS]  : py;
                var n = new Vector3(l - r, 2f * TILE_SIZE, u - d);
                n.Normalize();

                // Cor do vertice: usa cor do TRN (dwColor). Alpha sempre 1f.
                int tx = Math.Clamp(vx, 0, GRID-1);
                int ty = Math.Clamp(vy, 0, GRID-1);
                var tileInfo = _trn.Tiles[tx + ty * GRID];
                Vector4 tc;
                float cr = tileInfo.B9 / 255f;
                float cg = tileInfo.B8 / 255f;
                float cb = tileInfo.B7 / 255f;
                tc = new Vector4(cr, cg, cb, 1f);

                verts[vi + 0] = px;   verts[vi + 1] = py;   verts[vi + 2] = pz;
                verts[vi + 3] = n.X;  verts[vi + 4] = n.Y;  verts[vi + 5] = n.Z;
                verts[vi + 6] = tc.X; verts[vi + 7] = tc.Y; verts[vi + 8] = tc.Z; verts[vi + 9] = 1f;

            }
        }

        // ── Índices ──────────────────────────────────────────────────────
        for (int ty = 0; ty < GRID; ty++)
        {
            for (int tx = 0; tx < GRID; tx++)
            {
                uint tl = (uint)(tx     + ty     * VERTS);
                uint tr = (uint)(tx + 1 + ty     * VERTS);
                uint bl = (uint)(tx     + (ty+1) * VERTS);
                uint br = (uint)(tx + 1 + (ty+1) * VERTS);
                idxBuf.Add(tl); idxBuf.Add(bl); idxBuf.Add(tr);
                idxBuf.Add(tr); idxBuf.Add(bl); idxBuf.Add(br);
            }
        }

        _indexCount = idxBuf.Count;
        var indices = idxBuf.ToArray();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.DynamicDraw);
        GL.BindVertexArray(0);

        // Reconstruir tile map quando mesh é recriada (novo mapa)
        BuildTileMap();
    }

    // ── FBO ─────────────────────────────────────────────────────────────────
    private void CreateFbo(int w, int h)
    {
        _vpW = w; _vpH = h;

        if (_fbo != 0)
        {
            GL.DeleteFramebuffer(_fbo);
            GL.DeleteTexture(_colorTex);
            GL.DeleteRenderbuffer(_depthRbo);
        }

        _fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        _colorTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _colorTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _colorTex, 0);

        _depthRbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, w, h);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _depthRbo);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void ResizeFboIfNeeded(int w, int h)
    {
        if (w != _vpW || h != _vpH) CreateFbo(w, h);
    }

    // ── Shaders ─────────────────────────────────────────────────────────────
    private void CompileShaders()
    {
        const string VERT = @"
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNorm;
layout(location=2) in vec4 aColor;

uniform mat4 uMVP;
uniform mat4 uModel;

out vec3 vNorm;
out vec4 vColor;
out vec3 vWorldPos;

void main(){
    vec4 wp  = uModel * vec4(aPos, 1.0);
    vWorldPos = wp.xyz;
    vNorm     = normalize(mat3(transpose(inverse(uModel))) * aNorm);
    vColor    = aColor;
    gl_Position = uMVP * vec4(aPos, 1.0);
}";

        // MAP_SIZE = GRID * TILE_SIZE = 64 * 2 = 128
        const string FRAG = @"
#version 330 core
in vec3 vNorm;
in vec4 vColor;
in vec3 vWorldPos;

uniform sampler2DArray uTexArray;
uniform sampler2D      uTileMap;
uniform int            uHasTex;
uniform sampler2D      uAttrOverlay;
uniform int            uHasAttrOverlay;

uniform vec3  uLightDir;
uniform vec4  uAmbient;
uniform vec4  uFogColor;
uniform float uFogDist;

out vec4 FragColor;

const float TILE_SIZE = 2.0;
const float MAP_SIZE  = 128.0;

void main(){
    vec4 col;

    if (uHasTex > 0) {
        vec2 tmUv = clamp(vWorldPos.xz / MAP_SIZE, 0.0, 1.0);
        float rawIdx = texture(uTileMap, tmUv).r;
        float tileLayer = round(rawIdx * 255.0);
        vec2 texUv = fract(vWorldPos.xz / TILE_SIZE);
        col = texture(uTexArray, vec3(texUv, tileLayer));
        if (col.a < 0.01) col = vColor;
    } else {
        col = vColor;
    }

    if (uHasAttrOverlay > 0) {
        vec2 tmUv = clamp(vWorldPos.xz / MAP_SIZE, 0.0, 1.0);
        vec4 ov = texture(uAttrOverlay, tmUv);
        float a = clamp(ov.a * 0.70, 0.0, 0.85);
        col.rgb = mix(col.rgb, ov.rgb, a);
    }

    float diff = max(dot(vNorm, normalize(-uLightDir)), 0.0);
    vec3  lit  = uAmbient.rgb + (1.0 - uAmbient.rgb) * diff;
    col = vec4(col.rgb * lit, col.a);

    float dist = length(vWorldPos);
    float fog  = clamp(dist / uFogDist, 0.0, 1.0);
    fog = fog * fog;
    col = mix(col, uFogColor, fog);

    FragColor = col;
}";


        _shaderProgram = CreateProgram(VERT, FRAG);
        _uMvp      = GL.GetUniformLocation(_shaderProgram, "uMVP");
        _uModel    = GL.GetUniformLocation(_shaderProgram, "uModel");
        _uLightDir = GL.GetUniformLocation(_shaderProgram, "uLightDir");
        _uAmbient  = GL.GetUniformLocation(_shaderProgram, "uAmbient");
        _uFogColor = GL.GetUniformLocation(_shaderProgram, "uFogColor");
        _uFogDist  = GL.GetUniformLocation(_shaderProgram, "uFogDist");
        _uTexArray = GL.GetUniformLocation(_shaderProgram, "uTexArray");
        _uTileMap  = GL.GetUniformLocation(_shaderProgram, "uTileMap");
        _uHasTex   = GL.GetUniformLocation(_shaderProgram, "uHasTex");
        _uAttrOverlay    = GL.GetUniformLocation(_shaderProgram, "uAttrOverlay");
        _uHasAttrOverlay = GL.GetUniformLocation(_shaderProgram, "uHasAttrOverlay");
    }

    private static int CreateProgram(string vert, string frag)
    {
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vert);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) throw new Exception("Vertex shader: " + GL.GetShaderInfoLog(vs));

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, frag);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out ok);
        if (ok == 0) throw new Exception("Fragment shader: " + GL.GetShaderInfoLog(fs));

        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vs);
        GL.AttachShader(prog, fs);
        GL.LinkProgram(prog);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out ok);
        if (ok == 0) throw new Exception("Link shader: " + GL.GetProgramInfoLog(prog));

        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
        return prog;
    }

    // ── Paleta de cores fallback ─────────────────────────────────────────────
    private static Vector4[] BuildPalette()
    {
        var p = new Vector4[256];
        for (int i = 0; i < 256; i++)
        {
            uint x = (uint)i;
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            float r = 0.3f + (x & 0x7Fu) / 255f * 0.7f;
            float g = 0.3f + ((x >> 8) & 0x7Fu) / 255f * 0.7f;
            float b = 0.3f + ((x >> 16) & 0x7Fu) / 255f * 0.7f;

            if (i == 0) { r = 0.15f; g = 0.55f; b = 0.22f; }
            if (i == 1) { r = 0.22f; g = 0.48f; b = 0.18f; }
            if (i == 2) { r = 0.75f; g = 0.68f; b = 0.45f; }
            if (i == 3) { r = 0.55f; g = 0.50f; b = 0.35f; }
            if (i == 4) { r = 0.20f; g = 0.35f; b = 0.65f; }
            if (i == 5) { r = 0.25f; g = 0.40f; b = 0.70f; }

            p[i] = new Vector4(r, g, b, 1f);
        }
        return p;
    }

    // ── IDisposable ─────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_vao  != 0) { GL.DeleteVertexArray(_vao); _vao = 0; }
        if (_vbo  != 0) { GL.DeleteBuffer(_vbo); _vbo = 0; }
        if (_ebo  != 0) { GL.DeleteBuffer(_ebo); _ebo = 0; }
        if (_shaderProgram != 0) { GL.DeleteProgram(_shaderProgram); _shaderProgram = 0; }
        if (_fbo  != 0) { GL.DeleteFramebuffer(_fbo); GL.DeleteTexture(_colorTex); GL.DeleteRenderbuffer(_depthRbo); _fbo = 0; }
        if (_texArray   != 0) { GL.DeleteTexture(_texArray);   _texArray   = 0; }
        if (_tileMapTex != 0) { GL.DeleteTexture(_tileMapTex); _tileMapTex = 0; }
    }

    /// <summary>Força re-geração da mesh (usar após edição do terreno).</summary>
    public void MarkDirty() => _meshDirty = true;

    /// <summary>Força re-upload do Texture2DArray (usar quando novas texturas forem carregadas).</summary>
    public void MarkTextureDirty() => _texArrayDirty = true;

    /// <summary>Força re-upload apenas do TileMap 64×64 (após pintar textura — muito mais rápido que MarkDirty).</summary>
    public void MarkTileMapDirty() => _tileMapDirty = true;
}
