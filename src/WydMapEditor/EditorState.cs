using OpenTK.Graphics.OpenGL4;
using WydFormats;

namespace WydMapEditor;

// ── Enums públicos ────────────────────────────────────────────────────────────

public enum EditorTool
{
    Select, Area, Move, Rotate, Scale,
    Level, PaintTexture, AttributeMap,
    Object, Collision, Trigger, Light
}

public enum VisualizationMode { Terrain, Wireframe }

public enum BottomTab { Tiles, Textures, Objects, Prefabs }

public enum GizmoAxis { None, X, Y, Z }

// ── EditorState ───────────────────────────────────────────────────────────────

/// <summary>
/// Estado completo do editor: arquivo carregado, ferramenta ativa,
/// configurações de visualização, histórico undo/redo.
/// </summary>
public sealed class EditorState
{
    // ── Caminhos ─────────────────────────────────────────────────────────
    public string EnvFolder          = "";
    public string TrnPath            = "";
    public string DatPath            = "";
    public string MeshListPath       = "";
    public string EnvTextureListPath = "";

    // ── Dados carregados ─────────────────────────────────────────────────
    public TrnFile? Trn { get; private set; }
    public DatFile? Dat { get; private set; }
    public Dictionary<int, string> MeshNameById { get; private set; } = new();
    public Dictionary<int, string> TileNameById { get; private set; } = new();

    // ── Ferramenta ───────────────────────────────────────────────────────
    public EditorTool        ActiveTool   = EditorTool.Level;
    public VisualizationMode VizMode      = VisualizationMode.Terrain;
    public bool              RenderMeshes = true;
    public bool              ShowGrid     = true;

    // ── Parâmetros Level ─────────────────────────────────────────────────
    public float BrushRadius   = 3f;
    public float BrushStrength = 0.8f;
    public float LevelHeight   = 0f;
    public bool  SquareBrush   = false;
    public sbyte HeightMin     = -128;
    public sbyte HeightMax     =  127;

    // ── Parâmetros PaintTexture ──────────────────────────────────────────
    public int  SelectedTileIndex = 0;
    public bool PaintSquare       = true;
    // ── Parâmetros Level extra ───────────────────────────────────────────
    public float RaiseDelta    = 1f;   // delta de altura por frame
    public bool  FlattenMode   = false;

    // ── AttributeMap ─────────────────────────────────────────────────────
    public string              AttributeMapPath     = "";
    public bool                ShowAttributeOverlay = false;
    public byte                SelectedAttribute    = 0;
    public float               AttrBrushRadius      = 1f;
    public AttributeMapFile?   AttributeMap         { get; private set; }

    // ── Seleção de objeto ────────────────────────────────────────────────
    public int SelectedObjectIndex = -1;

    // ── Info cursor ──────────────────────────────────────────────────────
    public int HoverTileX = -1, HoverTileY = -1;

    // ── Undo / Redo ──────────────────────────────────────────────────────
    private readonly Stack<UndoEntry> _undoStack = new();
    private readonly Stack<UndoEntry> _redoStack = new();
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    private readonly record struct UndoEntry(byte Kind, byte[] Data);
    private const byte UndoKindAll     = 0;
    private const byte UndoKindAttrMap = 1;
    private const int UndoLimit = 300;

    // ── Último erro ──────────────────────────────────────────────────────
    public string LastError { get; private set; } = "";
    public bool   IsLoaded  => Trn != null;

    // ── Minimap ──────────────────────────────────────────────────────────
    public int MinimapTexture { get; private set; } = 0;
    private int _minimapGl = 0;

    // ────────────────────────────────────────────────────────────────────────
    //  Carregamento / salvamento
    // ────────────────────────────────────────────────────────────────────────

    public bool TryLoad()
    {
        LastError = "";
        try
        {
            var envFolder   = string.IsNullOrWhiteSpace(EnvFolder) ? Directory.GetCurrentDirectory() : EnvFolder;
            var trnPath     = Resolve(envFolder, TrnPath, "*.trn");
            var datPath     = Resolve(envFolder, DatPath, "*.dat");
            var meshPath    = ResolveExact(envFolder, MeshListPath);
            var envTexPath  = ResolveExact(envFolder, EnvTextureListPath);

            if (string.IsNullOrWhiteSpace(trnPath) || !File.Exists(trnPath))
                throw new FileNotFoundException("TRN não encontrado", trnPath);
            if (string.IsNullOrWhiteSpace(datPath) || !File.Exists(datPath))
                throw new FileNotFoundException("DAT não encontrado", datPath);

            Trn = TrnFile.Load(trnPath);
            Dat = DatFile.Load(datPath);
            string gameRoot = !string.IsNullOrWhiteSpace(envFolder) && Directory.Exists(envFolder)
                ? (Directory.GetParent(envFolder)?.FullName ?? envFolder)
                : Directory.GetCurrentDirectory();
            MeshNameById = MeshListReader.LoadMerged(gameRoot, meshPath);
            TileNameById = LoadTileNameById(envTexPath);

            TrnPath            = trnPath;
            DatPath            = datPath;
            MeshListPath       = meshPath  ?? "";
            EnvTextureListPath = envTexPath ?? "";

            _undoStack.Clear();
            _redoStack.Clear();
            RebuildMinimap();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message +
                (ex is FileNotFoundException fnf && !string.IsNullOrWhiteSpace(fnf.FileName)
                    ? "\n" + fnf.FileName : "");
            return false;
        }
    }

    public bool TrySave()
    {
        if (Trn == null) return false;
        try { Trn.Save(TrnPath); Dat?.Save(DatPath); return true; }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public void Unload()
    {
        Trn = null;
        Dat = null;
        SelectedObjectIndex = -1;
        HoverTileX = HoverTileY = -1;
        _undoStack.Clear();
        _redoStack.Clear();
        LastError = "";
        AttributeMap = null;
        ShowAttributeOverlay = false;

        if (_minimapGl != 0)
        {
            GL.DeleteTexture(_minimapGl);
            _minimapGl = 0;
        }
        MinimapTexture = 0;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Ferramentas de edição de terreno
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Aplica a ferramenta ativa no tile central (tx, ty).</summary>
    public bool ApplyTool(int tx, int ty)
    {
        if (Trn == null || tx < 0 || tx >= 64 || ty < 0 || ty >= 64) return false;
        return ActiveTool switch
        {
            EditorTool.Level        => ApplyLevel(tx, ty),
            EditorTool.PaintTexture => ApplyPaint(tx, ty),
            _ => false
        };
    }

    private bool ApplyLevel(int cx, int cy)
    {
        if (Trn == null) return false;
        bool changed = false;
        int r = (int)Math.Ceiling(BrushRadius);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (SquareBrush) { if (Math.Abs(dx) > BrushRadius || Math.Abs(dy) > BrushRadius) continue; }
                else             { if (dx * dx + dy * dy > BrushRadius * BrushRadius) continue; }
                int ttx = cx + dx, tty = cy + dy;
                if (ttx < 0 || ttx >= 64 || tty < 0 || tty >= 64) continue;
                ref var tile   = ref Trn.Tiles[ttx + tty * 64];
                float cur = tile.Height;
                int newH;
                if (FlattenMode)
                {
                    float target = Math.Clamp(LevelHeight, HeightMin, HeightMax);
                    newH = (int)MathF.Round(cur + (target - cur) * BrushStrength);
                }
                else
                {
                    float w = 1f;
                    if (!SquareBrush && BrushRadius > 0.001f)
                    {
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        w = Math.Clamp(1f - (dist / BrushRadius), 0f, 1f);
                    }
                    float delta = RaiseDelta * BrushStrength * w;
                    newH = (int)MathF.Round(cur + delta);
                }
                newH = Math.Clamp(newH, HeightMin, HeightMax);
                if (tile.Height != (sbyte)newH)
                {
                    tile.Height = (sbyte)newH;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private bool ApplyPaint(int cx, int cy)
    {
        if (Trn == null) return false;
        bool changed = false;
        int r = (int)Math.Ceiling(BrushRadius);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (PaintSquare) { if (Math.Abs(dx) > BrushRadius || Math.Abs(dy) > BrushRadius) continue; }
                else             { if (dx * dx + dy * dy > BrushRadius * BrushRadius) continue; }
                int ttx = cx + dx, tty = cy + dy;
                if (ttx < 0 || ttx >= 64 || tty < 0 || tty >= 64) continue;
                ref var tile = ref Trn.Tiles[ttx + tty * 64];
                if (tile.TileIndex != (byte)SelectedTileIndex)
                { tile.TileIndex = (byte)SelectedTileIndex; changed = true; }
            }
        }
        return changed;
    }

    public void CaptureHeight(int tx, int ty)
    {
        if (Trn == null || tx < 0 || tx >= 64 || ty < 0 || ty >= 64) return;
        LevelHeight = Trn.Tiles[tx + ty * 64].Height;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Undo / Redo
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Alias público — usa-se 1× no início de um stroke de pintura de terreno.</summary>
    public void PushUndoPaint()    => PushUndoAll();
    /// <summary>Alias público — usa-se antes de mover/adicionar/remover objetos.</summary>
    public void PushUndoObjects()  => PushUndoAll();
    public void PushUndoAttributeMap() => PushUndoAttrMap();

    private void PushUndoAll()
    {
        if (Trn == null) return;
        _undoStack.Push(new UndoEntry(UndoKindAll, SerializeAll()));
        if (_undoStack.Count > UndoLimit)
        {
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = UndoLimit - 1; i >= 0; i--) _undoStack.Push(temp[i]);
        }
        _redoStack.Clear();
    }

    private void PushUndoAttrMap()
    {
        if (AttributeMap == null) return;
        var data = new byte[AttributeMap.Data.Length];
        System.Buffer.BlockCopy(AttributeMap.Data, 0, data, 0, data.Length);
        _undoStack.Push(new UndoEntry(UndoKindAttrMap, data));
        if (_undoStack.Count > UndoLimit)
        {
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = UndoLimit - 1; i >= 0; i--) _undoStack.Push(temp[i]);
        }
        _redoStack.Clear();
    }

    public bool Undo()
    {
        if (!CanUndo || Trn == null) return false;
        var entry = _undoStack.Pop();
        _redoStack.Push(entry.Kind switch
        {
            UndoKindAttrMap => new UndoEntry(UndoKindAttrMap, AttributeMap?.Data.ToArray() ?? Array.Empty<byte>()),
            _               => new UndoEntry(UndoKindAll, SerializeAll()),
        });
        if (entry.Kind == UndoKindAttrMap)
        {
            if (AttributeMap != null && entry.Data.Length == AttributeMap.Data.Length)
                System.Buffer.BlockCopy(entry.Data, 0, AttributeMap.Data, 0, entry.Data.Length);
        }
        else
        {
            DeserializeAll(entry.Data);
            RebuildMinimap();
        }
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo || Trn == null) return false;
        var entry = _redoStack.Pop();
        _undoStack.Push(entry.Kind switch
        {
            UndoKindAttrMap => new UndoEntry(UndoKindAttrMap, AttributeMap?.Data.ToArray() ?? Array.Empty<byte>()),
            _               => new UndoEntry(UndoKindAll, SerializeAll()),
        });
        if (entry.Kind == UndoKindAttrMap)
        {
            if (AttributeMap != null && entry.Data.Length == AttributeMap.Data.Length)
                System.Buffer.BlockCopy(entry.Data, 0, AttributeMap.Data, 0, entry.Data.Length);
        }
        else
        {
            DeserializeAll(entry.Data);
            RebuildMinimap();
        }
        return true;
    }

    // ── Serialização completa (tiles + objetos) ──────────────────────────────

    private byte[] SerializeAll()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ── Tiles ────────────────────────────────────────────────────────────
        int tileCount = Trn?.Tiles.Length ?? 0;
        bw.Write(tileCount);
        for (int i = 0; i < tileCount; i++)
        {
            var t = Trn!.Tiles[i];
            bw.Write((byte)t.Height);
            bw.Write(t.B0);  bw.Write(t.B1);  bw.Write(t.B2);
            bw.Write(t.B3);  bw.Write(t.B4);  bw.Write(t.B5);  bw.Write(t.B6);
            bw.Write(t.B7);  bw.Write(t.B8);  bw.Write(t.B9);  bw.Write(t.B10);
        }

        // ── Objetos (DatRecords) ─────────────────────────────────────────────
        int recCount = Dat?.Records.Count ?? 0;
        bw.Write(recCount);
        for (int i = 0; i < recCount; i++)
        {
            var r = Dat!.Records[i];
            bw.Write(r.ObjType);
            bw.Write(r.PosX);
            bw.Write(r.PosY);
            bw.Write(r.Height);
            bw.Write(r.Angle);
            bw.Write(r.TextureSetIndex);
            bw.Write(r.MaskIndex);
            bw.Write(r.HasScale ? (byte)1 : (byte)0);
            bw.Write(r.ScaleH);
            bw.Write(r.ScaleV);
        }

        return ms.ToArray();
    }

    private void DeserializeAll(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        // ── Tiles ────────────────────────────────────────────────────────────
        int tileCount = br.ReadInt32();
        for (int i = 0; i < tileCount && i < (Trn?.Tiles.Length ?? 0); i++)
        {
            ref var t = ref Trn!.Tiles[i];
            t.Height = (sbyte)br.ReadByte();
            t.B0 = br.ReadByte(); t.B1 = br.ReadByte(); t.B2 = br.ReadByte();
            t.B3 = br.ReadByte(); t.B4 = br.ReadByte(); t.B5 = br.ReadByte(); t.B6 = br.ReadByte();
            t.B7 = br.ReadByte(); t.B8 = br.ReadByte(); t.B9 = br.ReadByte(); t.B10 = br.ReadByte();
        }

        // ── Objetos ──────────────────────────────────────────────────────────
        if (ms.Position + 4 > ms.Length) return;
        int recCount = br.ReadInt32();
        Dat?.Records.Clear();
        for (int i = 0; i < recCount && ms.Position + 37 <= ms.Length; i++)
        {
            var r = new DatRecord
            {
                ObjType         = br.ReadUInt32(),
                PosX            = br.ReadSingle(),
                PosY            = br.ReadSingle(),
                Height          = br.ReadSingle(),
                Angle           = br.ReadSingle(),
                TextureSetIndex = br.ReadInt32(),
                MaskIndex       = br.ReadInt32(),
                HasScale        = br.ReadByte() != 0,
                ScaleH          = br.ReadSingle(),
                ScaleV          = br.ReadSingle(),
            };
            Dat?.Records.Add(r);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Minimap (textura OpenGL 64×64)
    // ────────────────────────────────────────────────────────────────────────

    public void RebuildMinimap()
    {
        if (Trn == null) return;
        const int S = 64;
        var pixels = new byte[S * S * 4];

        for (int ty = 0; ty < S; ty++)
        {
            for (int tx = 0; tx < S; tx++)
            {
                var  tile = Trn.Tiles[tx + ty * S];
                var  c    = TerrainRenderer.TileColor(tile.TileIndex);
                int  o    = (ty * S + tx) * 4;
                pixels[o]   = (byte)(c.X * 255);
                pixels[o+1] = (byte)(c.Y * 255);
                pixels[o+2] = (byte)(c.Z * 255);
                pixels[o+3] = 255;
            }
        }

        if (_minimapGl == 0)
        {
            _minimapGl = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _minimapGl);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, _minimapGl);
        }

        // Use GCHandle para fixar o array ao invés de 'unsafe'
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            IntPtr ptr = handle.AddrOfPinnedObject();
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                S, S, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
        finally
        {
            handle.Free();
        }

        GL.BindTexture(TextureTarget.Texture2D, 0);
        MinimapTexture = _minimapGl;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Helpers privados
    // ────────────────────────────────────────────────────────────────────────

    private static Dictionary<int, string> LoadTileNameById(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new();
        if (Path.GetExtension(path).ToLowerInvariant() == ".bin")
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
        return TextLists.LoadIdToName(path);
    }

    private static string? ResolveExact(string folder, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Path.IsPathRooted(path) ? path : Path.Combine(folder, path);
    }

    private static string? Resolve(string folder, string path, string glob)
    {
        if (!string.IsNullOrWhiteSpace(path))
            return Path.IsPathRooted(path) ? path : Path.Combine(folder, path);
        var files = Directory.GetFiles(folder, glob);
        return files.Length > 0 ? files[0] : null;
    }

    /// <summary>Tenta carregar o AttributeMap.dat do caminho especificado.</summary>
    public bool LoadAttributeMap(string path)
    {
        try
        {
            AttributeMap     = AttributeMapFile.Load(path);
            AttributeMapPath = path;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public static string[] ScanFields(string envFolder)
    {
        if (string.IsNullOrWhiteSpace(envFolder) || !Directory.Exists(envFolder))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(envFolder, "Field*.trn")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(s => s != null)
            .OrderBy(s => s)
            .Select(s => s!)
            .ToArray();
    }
}
