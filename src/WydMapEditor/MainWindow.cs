using System.Numerics;
using System.Linq;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using WydFormats;

// Resolve ambiguidade entre System.Windows.Forms.Keys e OpenTK.Keys
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace WydMapEditor;

public sealed class MainWindow : GameWindow
{
    // ── Core ────────────────────────────────────────────────────────────────
    private ImGuiController?  _imgui;
    private readonly EditorState       _state    = new();
    private TerrainRenderer?  _terrain;
    private readonly Camera3D _cam             = new();
    private readonly TileTextureCache  _tileCache   = new();
    private readonly ObjectRenderer    _objRenderer = new();
    private TerrainRenderer? _browserPreviewTerrain;
    private readonly ObjectRenderer _browserPreviewObjRenderer = new();
    private readonly Camera3D _browserPreviewCam = new();
    private string _browserPreviewField = "";
    private Task<TrnFile?>? _browserPreviewTrnTask;
    private string _browserPreviewTaskField = "";
    private TrnFile? _browserPreviewTrn;
    private Task<DatFile?>? _browserPreviewDatTask;
    private string _browserPreviewDatTaskField = "";
    private DatFile? _browserPreviewDat;
    private Dictionary<int, string>? _browserPreviewMeshList;
    private string _browserPreviewMeshListRoot = "";
    private string _browserPreviewMeshListPath = "";
    private DateTime _browserPreviewMeshListMtimeUtc;
    private string _browserPreviewCommonMeshListPath = "";
    private DateTime _browserPreviewCommonMeshListMtimeUtc;
    private const int BrowserPreviewRenderSize = 420;
    private const float BrowserPreviewDisplaySize = 300f;
    private bool _browserPreviewShowMeshes = true;
    private bool _browserPreviewDragging;
    private Vector2 _browserPreviewLastMouse;
    private bool _requestOpenNewMapModal;
    private bool _newMapModalOpen;


    // ── Layout ──────────────────────────────────────────────────────────────
    private const float TOOLBAR_H  = 64f;
    private const float TABBAR_H   = 26f;
    private const float LEFT_W     = 220f;
    private const float RIGHT_W    = 240f;
    private const float BOTTOM_H   = 220f;
    private const float STATUS_H   = 22f;

    // ── Dialogs (async) ─────────────────────────────────────────────────────
    private string _clientRoot = "";
    private string _serverRoot = "";
    private string _serverHeightmapPath = "";
    private string _serverAttributeMapPath = "";
    private bool   _saveAttributeMapOnSave = true;
    private bool   _updateClientAttributeMapOnSave = true;
    private bool   _updateServerAttributeMapOnSave = true;
    private bool   _patchServerHeightmapOnSave = true;
    private int    _lastAttrMapSavedClientCount;
    private bool   _lastAttrMapSavedServer;
    private bool   _lastAttrMapSavedWorkingCopy;
    private int    _lastScaleConvertedCount;
    private int    _lastScaleDroppedCount;
    private Task<string?>?  _pickClientTask;
    private Task<string?>?  _pickServerTask;
    private Task<string?>?  _pickHeightmapTask;
    private Task<string?>?  _pickServerAttrTask;
    private Task<string?>?  _pickEnvTask;
    private Task<string?>?  _pickTrnTask;
    private Task<string?>?  _pickDatTask;
    private Task<string?>?  _pickMeshTask;
    private Task<string?>?  _pickEnvTexTask;
    private Task<string?>?  _pickExportFolderTask;
    private Task<string[]>? _scanTask;
    private string _exportFieldPending = "";

    // ── Lista de mapas ─────────────────────────────────────────────────────
    private string[] _availableFields = Array.Empty<string>();
    private string   _selectedField   = "";
    private string   _mapFilter       = "";

    private string _newMapFieldName = "";
    private string _newMapMapName = "";
    private bool   _newMapMapNameTouched;
    private int    _newMapEnvX;
    private int    _newMapEnvY;
    private bool   _newMapOverwrite;
    private bool   _newMapAutoSlot = true;
    private bool   _newMapCreateMinimap = true;
    private bool   _newMapPatchServerHeightmap = true;
    private string[] _newMapFreeSlots = Array.Empty<string>();
    private int    _newMapFreeSlotIndex = 0;
    private bool   _showDeleteMapModal;
    private string _deleteMapField = "";
    private string _deleteMapConfirm = "";
    private bool   _deleteMapAlsoMinimap = true;

    // ── Abas de mapas abertos ───────────────────────────────────────────────
    private readonly List<string> _openTabs = new() { "Mapa" };
    private int _activeTab = 0;

    // ── Estado viewport ─────────────────────────────────────────────────────
    private Vector2 _vpPos, _vpSize;
    private bool    _vpDraggingOrbit;
    private bool    _vpDraggingPan;
    private bool    _vpPainting;
    private Vector2 _vpLastMouse;
    private float   _hoverLocalX = -1f;
    private float   _hoverLocalY = -1f;

    // ── Drag de objeto (ferramenta Move) ────────────────────────────────────
    private bool          _isDraggingObj;
    private int           _draggedObjIdx   = -1;
    private float         _dragPlaneY;          // Y do plano de arrasto (altura do objeto)
    private OpenTK.Mathematics.Vector3 _dragStartWorld;    // ponto 3D inicial do arrasto
    private float         _dragStartPosX;       // PosX original antes do drag
    private float         _dragStartPosY;       // PosY original antes do drag
    private float         _dragStartHeight;     // Height original antes do drag vertical
    private Vector2       _freeDragOriginMouse; // posição do mouse quando drag ativou (após threshold)
    // Seleção pendente: objeto clicado que ainda não excedeu threshold de movimento
    private int           _pendingDragObj      = -1;
    private Vector2       _pendingDragMouseStart;
    private const float   DragThresholdPx      = 6f; // pixels mínimos antes de iniciar drag

    private bool    _levelStrokeActive;
    private bool    _attrStrokeActive;

    private bool    _isRotatingObj;
    private int     _rotObjIdx = -1;
    private float   _rotStartAngle;
    private Vector2 _rotMouseStart;

    private bool    _isScalingObj;
    private int     _scaleObjIdx = -1;
    private float   _scaleStartH;
    private float   _scaleStartV;
    private Vector2 _scaleMouseStart;

    // ── Eventos de input capturados via callbacks GLFW (garantido, não filtrado pelo ImGui) ──
    private bool  _pendingClick;   // true apenas no frame em que o botão esquerdo foi pressionado
    private float _pendingScroll;  // acumulado de scroll desde o último frame

    // ── Scene dirty flag para otimização de renderização ───────────────────────
    private bool _sceneDirty = true; // força renderização no primeiro frame

    // ── Map browser ─────────────────────────────────────────────────────────
    private Vector2 _browserOffset  = Vector2.Zero;
    private float   _browserZoom    = 1.0f;
    private bool    _browserDragging;
    private Vector2 _browserLastMouse;

    // ── UI state ────────────────────────────────────────────────────────────
    private BottomTab _bottomTab  = BottomTab.Tiles;
    private Vector2   _tileScroll = Vector2.Zero;
    private Vector2   _hierScroll = Vector2.Zero;
    private bool      _hierShowObj      = true;
    private bool      _hierShowBuilding = true;
    private bool      _hierShowDeco     = true;
    private bool      _hierShowMonster  = true;
    private bool      _hierShowNPC      = true;
    private bool      _hierShowLayer    = true;
    private bool      _hierShowPlayer   = true;

    private bool _texOnlyNew = true;
    private string _texFilter = "";
    private string _texScanEnv = "";
    private List<string> _texAll = new();
    private List<string> _texNew = new();
    private int _texSelected = -1;
    private int _texAssignTileId = 0;
    private Task<string?>? _pickTextureTask;
    private readonly TexturePreviewCache _texPreview = new();
    private bool _showTexturesModal;
    private bool _texConvertForce128 = true;
    private bool _texConvertOverwrite = false;

    private string? _convPickedModel;
    private string[]? _convPickedFiles;

    private string _statusText = "Pronto.";
    private bool   _showDemo;

    // ── AttributeMap overlay ────────────────────────────────────────────────
    private int  _attrOverlayTex = 0;
    private bool _attrOverlayDirty;

    // ── Gizmo de movimento ──────────────────────────────────────────────────
    private GizmoAxis _hoverAxis = GizmoAxis.None;

    // ── Adicionar objeto ────────────────────────────────────────────────────
    private int    _addObjType    = 0;
    private string _objFilter     = "";          // filtro de busca no popup de adição
    private string _objListFilter = "";          // filtro na lista de objetos do mapa
    private int    _objCategory   = 0;           // 0=Todos 1=Árvores 2=Edificios 3=Custom
    private Task<string?>?              _pickObjFileTask;   // file picker para adicionar objeto existente
    private Task<string?>?              _pickConvFileTask;  // file picker para o botão "Converter 3D"
    private Task<string[]?>?            _pickConvFilesTask; // file picker (multi) para modo manual do conversor
    private Task<ConversionResult?>?    _conversionTask;    // tarefa de conversão assíncrona
    private IProgress<string>?          _convProgress;      // progresso da conversão
    private string                      _convStatus = "";   // última mensagem de progresso
    private bool                        _convDone   = false;
    private int                         _convUiMode = 0;    // 0=Automático 1=Manual

    private bool _hasObjClipboard;
    private DatRecord _objClipboard;
    private float _objClipboardHeightOffset;

    private int _pendingDeleteMeshType = -1;
    private bool _pendingDeleteMeshRemoveInstances = true;

    // ── Notificação toast (ex: "Objeto adicionado") ─────────────────────────
    private string _notifText  = "";
    private double _notifUntil = 0;   // ImGui.GetTime() até quando mostrar

    // ── Paint stroke (evita push de undo a cada frame) ──────────────────────
    private bool _paintStrokeActive = false;

    // ── Ferramenta Trigger: máscara de atributo selecionada (padrão PvP=64) ─
    private byte _triggerAttrMask = 64;

    // ── Ferramenta Light: cor RGB para pintura de vertex color ───────────────
    private uint _lightBrushColor = 0xFFFFFFu;   // branco por padrão
    private float _lightR = 1f, _lightG = 1f, _lightB = 1f;
    private string _lightTexOverride = "";        // textura selecionada para pintar objetos
    private int    _lightTexSelected = -1;        // índice na lista _texAll
    private string _lightTexFilter   = "";        // filtro de busca de textura
    private bool   _lightPaintObjects = false;    // se true, pincel também tinta objetos no raio
    private bool   _lightPaintByPart = false;     // se true, pinta apenas a parte (submesh) selecionada
    private int    _lightPartIndex = -1;          // -1 = objeto inteiro; >=0 = submesh index
    private readonly Dictionary<int, Dictionary<int, uint>> _objPartColors = new(); // objIndex -> (partIndex -> RGB)

    // ── Ferramenta Area: retângulo de seleção ───────────────────────────────
    private bool    _areaSelDragging = false;
    private Vector2 _areaSelStart    = Vector2.Zero;
    private Vector2 _areaSelEnd      = Vector2.Zero;

    // ── Multi-drag/rotate: posições e ângulos iniciais para seleção por área ─
    private readonly Dictionary<int, (float X, float Y, float H)> _areaDragStarts = new();
    private readonly Dictionary<int, float> _areaRotStarts = new();

    // ── Splash / logo de fundo ──────────────────────────────────────────────
    private int _splashTex = 0;

    // ── Cores ImGui (tema FoxMap — azul escuro / cyberpunk) ─────────────────
    private static readonly uint COL_PANEL     = ImGuiColor(0.04f, 0.04f, 0.09f);
    private static readonly uint COL_TOOLBAR   = ImGuiColor(0.03f, 0.03f, 0.07f);
    private static readonly uint COL_HEADER    = ImGuiColor(0.06f, 0.18f, 0.36f);
    private static readonly uint COL_SEP       = ImGuiColor(0.10f, 0.20f, 0.35f);
    private static readonly uint COL_ACTIVE    = ImGuiColor(0.00f, 0.47f, 0.85f);

    // ── Ctor ────────────────────────────────────────────────────────────────
    public MainWindow(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws) { }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    protected override void OnLoad()
    {
        try
        {
            base.OnLoad();
            GL.ClearColor(0.04f, 0.04f, 0.09f, 1f);
            _imgui   = new ImGuiController(this, Size.X, Size.Y);
            _terrain = new TerrainRenderer();
            _browserPreviewTerrain = new TerrainRenderer { ShowGrid = false, WireFrame = false };
            _browserPreviewCam.Target = new OpenTK.Mathematics.Vector3(64f, 0f, 64f);
            _browserPreviewCam.Yaw = 225f;
            _browserPreviewCam.Pitch = 72f;
            _browserPreviewCam.Distance = 320f;
            _browserPreviewCam.Fov = 35f;
            ApplyImGuiStyle();
            _splashTex = TryLoadSplash();
        }






        catch (Exception ex)
        {
            var logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(logPath,
                $"[OnLoad crash]\r\n{ex}\r\n");
            throw;
        }
    }

    protected override void OnUnload()
    {
        base.OnUnload();
        _terrain?.Dispose();
        _browserPreviewTerrain?.Dispose();
        _tileCache.Dispose();
        _texPreview.Dispose();
        _objRenderer.Dispose();
        _browserPreviewObjRenderer.Dispose();
        if (_attrOverlayTex != 0) { GL.DeleteTexture(_attrOverlayTex); _attrOverlayTex = 0; }
    }

    // Todas as features estão desbloqueadas (editor open source)
    private static bool IsPremiumNow => true;

    private static bool IsFreeTool(EditorTool tool) => true;

    private static bool CanUseTool(EditorTool tool) => true;

    private void EnforceToolAccess() { } // sem restrições

    // ────────────────────────────────────────────────────────────────────────
    //  Toolbar
    // ────────────────────────────────────────────────────────────────────────

    private void DrawToolbar(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, COL_TOOLBAR);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 0));
        ImGui.Begin("##toolbar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse);

        var draw = ImGui.GetWindowDrawList();
        uint neonBlue = ImGui.ColorConvertFloat4ToU32(new Vector4(0.00f, 0.45f, 1.00f, 1.00f));
        uint sepColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.25f, 0.55f, 0.50f));
        // Linha de destaque azul no TOPO
        draw.AddLine(pos, pos + new Vector2(size.X, 0), neonBlue, 2f);
        // Linha separadora sutil na BASE
        draw.AddLine(pos + new Vector2(0, size.Y - 1), pos + new Vector2(size.X, size.Y - 1), sepColor);

        // FramePadding aumentado para botões mais altos, alinhados verticalmente ao centro
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,  new Vector2(10, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(2, 0));
        // Centraliza verticalmente na toolbar (toolbar=64, botão≈34 → margem≈15)
        ImGui.SetCursorPosY((size.Y - ImGui.GetFrameHeightWithSpacing()) * 0.5f + 1f);

        // ── Arquivo ──────────────────────────────────────────────────────────
        if (TbFileBtn("Open",   false))                PickClientRoot();
        ImGui.SameLine();
        if (TbFileBtn("Save",   !_state.IsLoaded))     TrySave();
        TbDivider();

        if (TbFileBtn("Maps",   false))
        {
            _activeTab = 0;
            _statusText = "Aba Mapa aberta. Duplo-clique para abrir um field. Botao + cria um novo mapa.";
        }
        TbDivider();

        if (TbFileBtn("Import", false))
        {
            _bottomTab = BottomTab.Textures;
            if (_pickTextureTask == null)
            {
                _pickTextureTask = Dialogs.PickFileAsync(
                    "Texturas|*.wys;*.png;*.tga;*.jpg;*.jpeg;*.bmp|Todos|*.*",
                    _state.EnvFolder,
                    "Selecionar textura para copiar para o Env");
            }
        }
        ImGui.SameLine();
        if (TbFileBtn("Export", !_state.IsLoaded || string.IsNullOrWhiteSpace(_selectedField)))
        {
            if (_pickExportFolderTask == null)
            {
                _exportFieldPending = _selectedField;
                _pickExportFolderTask = Dialogs.PickFolderAsync(_state.EnvFolder, "Selecione a pasta de destino para exportar o mapa");
            }
        }
        TbDivider();

        // ── Histórico ─────────────────────────────────────────────────────────
        if (TbArrowBtn(false, !_state.CanUndo))  DoUndo();
        ImGui.SameLine(0, 2);
        if (TbArrowBtn(true,  !_state.CanRedo))  DoRedo();
        TbDivider();

        // ── Ferramentas de transformação ──────────────────────────────────────
        TbToolBtn("Select",    EditorTool.Select);   ImGui.SameLine();
        TbToolBtn("Area",      EditorTool.Area);     ImGui.SameLine();
        TbToolBtn("Move",      EditorTool.Move);     ImGui.SameLine();
        TbToolBtn("Rotate",    EditorTool.Rotate);   ImGui.SameLine();
        TbToolBtnRestricted("Scale", EditorTool.Scale);
        TbDivider();

        // ── Ferramentas de edição ─────────────────────────────────────────────
        TbToolBtnRestricted("Terrain",   EditorTool.Level);        ImGui.SameLine();
        TbToolBtnRestricted("Paint",     EditorTool.PaintTexture); ImGui.SameLine();
        TbToolBtnRestricted("AttrMap",   EditorTool.AttributeMap); ImGui.SameLine();
        TbToolBtnRestricted("Object",    EditorTool.Object);       ImGui.SameLine();
        TbToolBtnRestricted("Collision", EditorTool.Collision);    ImGui.SameLine();
        TbToolBtnRestricted("Trigger",   EditorTool.Trigger);      ImGui.SameLine();
        TbToolBtnRestricted("Light",     EditorTool.Light);
        TbDivider();

        // ── Utilitários ───────────────────────────────────────────────────────
        if (TbFileBtn("Settings", false)) _showDemo = !_showDemo;
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.00f, 0.38f, 0.75f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.00f, 0.55f, 1.00f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.00f, 0.70f, 1.00f, 1f));
        if (ImGui.Button("Play##play"))
        {
            _statusText = "Play não implementado.";
        }
        ImGui.PopStyleColor(3);

        // ── Converter 3D → WYD ───────────────────────────────────────────────
        TbDivider();
        {
            bool busyConv = _conversionTask != null && !_conversionTask.IsCompleted;

                // Polling: arquivo escolhido pelo picker de conversão → lança conversão
                if (_pickConvFileTask?.IsCompleted == true)
                {
                    string? picked = SafeGet(_pickConvFileTask!);
                    _pickConvFileTask = null;
                    if (!string.IsNullOrEmpty(picked))
                    {
                        _convPickedModel = picked;
                        _convPickedFiles = null;
                        _convUiMode = 0;
                        _convStatus = $"Selecionado: {Path.GetFileName(picked)}";
                    }
                }
                if (_pickConvFilesTask?.IsCompleted == true)
                {
                    var picked = _pickConvFilesTask.Result;
                    _pickConvFilesTask = null;
                    if (picked != null && picked.Length > 0)
                    {
                        _convPickedFiles = picked;
                        _convUiMode = 1;
                        _convPickedModel = null;
                        foreach (var f in picked)
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext is ".glb" or ".gltf" or ".fbx" or ".obj" or ".dae" or ".3ds")
                            {
                                _convPickedModel = f;
                                break;
                            }
                        }
                        _convStatus = _convPickedModel != null
                            ? $"Arquivos selecionados: {picked.Length}  | Modelo: {Path.GetFileName(_convPickedModel)}"
                            : $"Arquivos selecionados: {picked.Length}  | Modelo: (nao encontrado)";
                    }
                }
                if (_scanTask?.IsCompleted == true)
                {
                    var r = SafeGet(_scanTask); _scanTask = null;
                    _availableFields = r ?? Array.Empty<string>();
                    _statusText = _availableFields.Length > 0
                        ? $"{_availableFields.Length} mapas encontrados."
                        : "Nenhum mapa encontrado na pasta Env.";
                    if (_availableFields.Length > 0 && string.IsNullOrWhiteSpace(_selectedField))
                        SelectField(_availableFields[0]);
                }

                // Polling: conversão concluída → processa resultado
                if (_conversionTask?.IsCompleted == true && !_convDone)
                {
                    _convDone = true;
                    var convResult = _conversionTask.GetAwaiter().GetResult();
                    _conversionTask = null;
                    OnConversionComplete(convResult);
                }

                if (busyConv)
                {
                    // Botão desabilitado + spinner de progresso
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.45f);
                    ImGui.Button("Converter 3D##tbconv");
                    ImGui.PopStyleVar();
                    ImGui.SameLine(0, 6);
                    // Spinner simples: '|' '/' '-' '\'
                    int tick = (int)(ImGui.GetTime() * 6) % 4;
                    string[] spin = { "|", "/", "-", "\\" };
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"{spin[tick]} {_convStatus}");
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.55f, 0.28f, 0.00f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.80f, 0.40f, 0.00f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(1.00f, 0.55f, 0.00f, 1f));
                    if (ImGui.Button("Converter 3D##tbconv"))
                    {
                        ImGui.OpenPopup("Converter 3D##modal");
                    }
                    ImGui.PopStyleColor(3);
                }

                ImGui.SetNextWindowSize(new Vector2(520, 360), ImGuiCond.Once);
                bool convModalOpen = true;
                if (ImGui.BeginPopupModal("Converter 3D##modal", ref convModalOpen, ImGuiWindowFlags.NoResize))
                {
                    ImGui.TextDisabled("Escolha o modo de conversao:");
                    ImGui.Separator();

                    if (ImGui.RadioButton("Conversao automatica", _convUiMode == 0)) _convUiMode = 0;
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Modo manual", _convUiMode == 1)) _convUiMode = 1;

                    ImGui.Separator();

                    if (_convUiMode == 0)
                    {
                        ImGui.TextWrapped("Automatico: selecione UM modelo 3D e o programa tenta pegar as texturas, gerar .msa/.wys e registrar no MeshList/MeshTextureList.bin (cliente original).");
                        ImGui.Spacing();

                        bool disablePick = busyConv || _pickConvFileTask != null;
                        if (disablePick) ImGui.BeginDisabled();
                        if (ImGui.Button("Selecionar modelo 3D..."))
                        {
                            _pickConvFileTask = Dialogs.PickFileAsync(
                                "Modelos 3D|*.glb;*.gltf;*.fbx;*.obj;*.dae;*.3ds|Todos|*.*",
                                _state.EnvFolder ?? _clientRoot,
                                "Selecionar modelo 3D para converter para WYD (.msa)");
                        }
                        if (disablePick) ImGui.EndDisabled();

                        ImGui.Spacing();
                        ImGui.TextDisabled("Suporta: GLB/GLTF/FBX/OBJ/DAE/3DS");

                        if (!string.IsNullOrWhiteSpace(_convPickedModel))
                        {
                            ImGui.Separator();
                            ImGui.TextDisabled("Selecionado:");
                            ImGui.Text(Path.GetFileName(_convPickedModel));
                            ImGui.TextDisabled(_convPickedModel);

                            bool canConv = !busyConv && File.Exists(_convPickedModel);
                            if (!canConv) ImGui.BeginDisabled();
                            if (ImGui.Button("Converter agora##convauto", new Vector2(-1, 0)))
                            {
                                StartConversion(_convPickedModel);
                                ImGui.CloseCurrentPopup();
                            }
                            if (!canConv) ImGui.EndDisabled();
                        }
                    }
                    else
                    {
                        ImGui.TextWrapped("Manual: selecione TODOS os arquivos do modelo (ex: .gltf + .bin + texturas + .mtl). O programa copia para uma pasta temporaria mantendo a estrutura e converte de la.");
                        ImGui.Spacing();
                        ImGui.TextWrapped("Dica: os nomes/paths precisam bater com o que o modelo referencia (principalmente GLTF/OBJ).");
                        ImGui.Spacing();

                        bool disablePick = busyConv || _pickConvFilesTask != null;
                        if (disablePick) ImGui.BeginDisabled();
                        if (ImGui.Button("Selecionar arquivos (manual)..."))
                        {
                            _pickConvFilesTask = Dialogs.PickFilesAsync(
                                "Arquivos suportados|*.glb;*.gltf;*.bin;*.fbx;*.obj;*.mtl;*.dae;*.3ds;*.png;*.jpg;*.jpeg;*.tga;*.bmp|Todos|*.*",
                                _state.EnvFolder ?? _clientRoot,
                                "Selecionar todos os arquivos do modelo (manual)");
                        }
                        if (disablePick) ImGui.EndDisabled();

                        ImGui.Spacing();
                        ImGui.TextDisabled("Suporta: modelo + arquivos referenciados (bin/mtl/texturas)");

                        if (_convPickedFiles != null && _convPickedFiles.Length > 0)
                        {
                            ImGui.Separator();
                            ImGui.TextDisabled($"Arquivos selecionados: {_convPickedFiles.Length}");
                            ImGui.BeginChild("##convfiles", new Vector2(0, 90), true);
                            for (int i = 0; i < _convPickedFiles.Length; i++)
                            {
                                string f = _convPickedFiles[i];
                                string fn = Path.GetFileName(f);
                                bool isModel = _convPickedModel != null && string.Equals(f, _convPickedModel, StringComparison.OrdinalIgnoreCase);
                                if (isModel) ImGui.TextColored(new Vector4(0.55f, 0.85f, 1f, 1f), "MODELO: " + fn);
                                else ImGui.TextDisabled(fn);
                            }
                            ImGui.EndChild();

                            bool canConv = !busyConv && _convPickedModel != null && File.Exists(_convPickedModel);
                            if (!canConv) ImGui.BeginDisabled();
                            if (ImGui.Button("Converter manual agora##convman", new Vector2(-1, 0)))
                            {
                                string staged = StageManualFiles(_convPickedFiles);
                                if (!string.IsNullOrWhiteSpace(staged) && File.Exists(staged))
                                {
                                    StartConversion(staged);
                                    ImGui.CloseCurrentPopup();
                                }
                                else
                                {
                                    _convStatus = "Falha no modo manual: nao achei o arquivo principal do modelo dentro da selecao.";
                                }
                            }
                            if (!canConv) ImGui.EndDisabled();
                        }
                    }

                    ImGui.Spacing();
                    if (!string.IsNullOrWhiteSpace(_convStatus))
                        ImGui.TextDisabled(_convStatus);

                    ImGui.Separator();
                    if (ImGui.Button("Fechar"))
                        ImGui.CloseCurrentPopup();

                    ImGui.EndPopup();
                }
        }

        ImGui.PopStyleVar(3);  // FramePadding + ItemSpacing + WindowPadding
        ImGui.PopStyleColor(); // WindowBg
        ImGui.End();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        _imgui?.WindowResized(e.Width, e.Height);
    }

    // ── Callbacks GLFW — capturados ANTES do ImGui processar ─────────────────

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButton.Left)
            _pendingClick = true;  // será consumido em DrawUi()
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _pendingScroll += e.OffsetY;   // acumula (pode receber múltiplos eventos por frame)
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        if (_imgui == null || _terrain == null) return;

        _imgui.Update(this, (float)args.Time);
        PumpAsync();
        HandleKeyboardShortcuts();
        DrawUi();

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _imgui.Render();
        SwapBuffers();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Layout principal
    // ────────────────────────────────────────────────────────────────────────

    private void DrawUi()
    {
        var io  = ImGui.GetIO();
        float W = io.DisplaySize.X;
        float H = io.DisplaySize.Y;

        EnforceToolAccess();

        float contentY = TOOLBAR_H + TABBAR_H;
        float vpH      = Math.Max(10f, H - contentY - BOTTOM_H - STATUS_H);
        float vpW      = Math.Max(10f, W - LEFT_W - RIGHT_W);

        DrawToolbar(     new Vector2(0,      0),        new Vector2(W,      TOOLBAR_H));
        DrawTabBar(      new Vector2(LEFT_W, TOOLBAR_H), new Vector2(vpW,   TABBAR_H));
        DrawHierarchy(   new Vector2(0,      TOOLBAR_H), new Vector2(LEFT_W, H - TOOLBAR_H - STATUS_H));
        DrawViewport(    new Vector2(LEFT_W, contentY),  new Vector2(vpW,   vpH));
        DrawBottomPanel( new Vector2(LEFT_W, contentY + vpH), new Vector2(vpW, BOTTOM_H));
        DrawRightPanel(  new Vector2(W - RIGHT_W, TOOLBAR_H), new Vector2(RIGHT_W, H - TOOLBAR_H - STATUS_H));
        DrawStatusBar(   new Vector2(0, H - STATUS_H),  new Vector2(W,      STATUS_H));
        DrawPopupHost();

        if (_showDemo) ImGui.ShowDemoWindow(ref _showDemo);

        // Limpa eventos de clique/scroll após todos os widgets processarem neste frame
        _pendingClick  = false;
        _pendingScroll = 0f;
    }

        /* draw.AddLine(pos, pos + new Vector2(size.X, 0), neonBlue, 2f);
        // Linha separadora sutil na BASE
        draw.AddLine(pos + new Vector2(0, size.Y - 1), pos + new Vector2(size.X, size.Y - 1), sepColor);

        // FramePadding aumentado para botões mais altos, alinhados verticalmente ao centro
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,  new Vector2(10, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,   new Vector2(2, 0));
        // Centraliza verticalmente na toolbar (toolbar=64, botão≈34 → margem≈15)
        ImGui.SetCursorPosY((size.Y - ImGui.GetFrameHeightWithSpacing()) * 0.5f + 1f);

        // ── Arquivo ──────────────────────────────────────────────────────────
        if (TbFileBtn("Open",   false))                PickClientRoot();
        ImGui.SameLine();
        if (TbFileBtn("Save",   !_state.IsLoaded))     TrySave();
        TbDivider();

        if (TbFileBtn("Maps",   false))
        {
            _activeTab = 0;
            _statusText = "Aba Mapa aberta. Duplo-clique para abrir um field. Botao + cria um novo mapa.";
        }
        TbDivider();

        if (TbFileBtn("Import", false))
        {
            _bottomTab = BottomTab.Textures;
            if (_pickTextureTask == null)
            {
                _pickTextureTask = Dialogs.PickFileAsync(
                    "Texturas|*.wys;*.png;*.tga;*.jpg;*.jpeg;*.bmp|Todos|*.*",
                    _state.EnvFolder,
                    "Selecionar textura para copiar para o Env");
            }
        }
        ImGui.SameLine();
        if (TbFileBtn("Export", !_state.IsLoaded || string.IsNullOrWhiteSpace(_selectedField)))
        {
            if (_pickExportFolderTask == null)
            {
                _exportFieldPending = _selectedField;
                _pickExportFolderTask = Dialogs.PickFolderAsync(_state.EnvFolder, "Selecione a pasta de destino para exportar o mapa");
            }
        }
        TbDivider();

        // ── Histórico ─────────────────────────────────────────────────────────
        if (TbArrowBtn(false, !_state.CanUndo))  DoUndo();
        ImGui.SameLine(0, 2);
        if (TbArrowBtn(true,  !_state.CanRedo))  DoRedo();
        TbDivider();

        // ── Ferramentas de transformação ──────────────────────────────────────
        TbToolBtn("Select",    EditorTool.Select);   ImGui.SameLine();
        TbToolBtn("Area",      EditorTool.Area);     ImGui.SameLine();
        TbToolBtn("Move",      EditorTool.Move);     ImGui.SameLine();
        TbToolBtn("Rotate",    EditorTool.Rotate);   ImGui.SameLine();
        TbToolBtnRestricted("Scale", EditorTool.Scale);
        TbDivider();

        // ── Ferramentas de edição ─────────────────────────────────────────────
        TbToolBtnRestricted("Terrain",   EditorTool.Level);        ImGui.SameLine();
        TbToolBtnRestricted("Paint",     EditorTool.PaintTexture); ImGui.SameLine();
        TbToolBtnRestricted("AttrMap",   EditorTool.AttributeMap); ImGui.SameLine();
        TbToolBtnRestricted("Object",    EditorTool.Object);       ImGui.SameLine();
        TbToolBtnRestricted("Collision", EditorTool.Collision);    ImGui.SameLine();
        TbToolBtnRestricted("Trigger",   EditorTool.Trigger);      ImGui.SameLine();
        TbToolBtnRestricted("Light",     EditorTool.Light);
        TbDivider();

        // ── Utilitários ───────────────────────────────────────────────────────
        if (TbFileBtn("Settings", false)) _showDemo = !_showDemo;
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.00f, 0.38f, 0.75f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.00f, 0.55f, 1.00f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.00f, 0.70f, 1.00f, 1f));
        if (ImGui.Button("  Play  ##tbplay"))
        {
            if (_state.IsLoaded) TrySave();
            ShowNotif("Play nao configurado (ainda).", 3.5f);
            _statusText = "Play nao configurado.";
        }
        ImGui.PopStyleColor(3);

        // ── Converter 3D → WYD ───────────────────────────────────────────────
        TbDivider();
        {
            bool busyConv = _conversionTask != null && !_conversionTask.IsCompleted;

            // Polling: arquivo escolhido pelo picker de conversão → lança conversão
            if (_pickConvFileTask?.IsCompleted == true)
            {
                string? picked = SafeGet(_pickConvFileTask!);
                _pickConvFileTask = null;
                if (!string.IsNullOrEmpty(picked))
                {
                    _convPickedModel = picked;
                    _convPickedFiles = null;
                    _convUiMode = 0;
                    _convStatus = $"Selecionado: {Path.GetFileName(picked)}";
                }
            }
            if (_pickConvFilesTask?.IsCompleted == true)
            {
                var picked = _pickConvFilesTask.Result;
                _pickConvFilesTask = null;
                if (picked != null && picked.Length > 0)
                {
                    _convPickedFiles = picked;
                    _convUiMode = 1;
                    _convPickedModel = null;
                    foreach (var f in picked)
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext is ".glb" or ".gltf" or ".fbx" or ".obj" or ".dae" or ".3ds")
                        {
                            _convPickedModel = f;
                            break;
                        }
                    }
                    _convStatus = _convPickedModel != null
                        ? $"Arquivos selecionados: {picked.Length}  | Modelo: {Path.GetFileName(_convPickedModel)}"
                        : $"Arquivos selecionados: {picked.Length}  | Modelo: (nao encontrado)";
                }
            }
            if (_scanTask?.IsCompleted == true)
            {
                var r = SafeGet(_scanTask); _scanTask = null;
                _availableFields = r ?? Array.Empty<string>();
                _statusText = _availableFields.Length > 0
                    ? $"{_availableFields.Length} mapas encontrados."
                    : "Nenhum mapa encontrado na pasta Env.";
                if (_availableFields.Length > 0 && string.IsNullOrWhiteSpace(_selectedField))
                    SelectField(_availableFields[0]);
            }

            // Polling: conversão concluída → processa resultado
            if (_conversionTask?.IsCompleted == true && !_convDone)
            {
                _convDone = true;
                var convResult = _conversionTask.GetAwaiter().GetResult();
                _conversionTask = null;
                OnConversionComplete(convResult);
            }

            if (busyConv)
            {
                // Botão desabilitado + spinner de progresso
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.45f);
                ImGui.Button("Converter 3D##tbconv");
                ImGui.PopStyleVar();
                ImGui.SameLine(0, 6);
                // Spinner simples: '|' '/' '-' '\'
                int tick = (int)(ImGui.GetTime() * 6) % 4;
                string[] spin = { "|", "/", "-", "\\" };
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"{spin[tick]} {_convStatus}");
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.55f, 0.28f, 0.00f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.80f, 0.40f, 0.00f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(1.00f, 0.55f, 0.00f, 1f));
                if (ImGui.Button("Converter 3D##tbconv"))
                {
                    ImGui.OpenPopup("Converter 3D##modal");
                }
                ImGui.PopStyleColor(3);
            }

            ImGui.SetNextWindowSize(new Vector2(520, 360), ImGuiCond.Once);
            bool convModalOpen = true;
            if (ImGui.BeginPopupModal("Converter 3D##modal", ref convModalOpen, ImGuiWindowFlags.NoResize))
            {
                ImGui.TextDisabled("Escolha o modo de conversao:");
                ImGui.Separator();

                if (ImGui.RadioButton("Conversao automatica", _convUiMode == 0)) _convUiMode = 0;
                ImGui.SameLine();
                if (ImGui.RadioButton("Modo manual", _convUiMode == 1)) _convUiMode = 1;

                ImGui.Separator();

                if (_convUiMode == 0)
                {
                    ImGui.TextWrapped("Automatico: selecione UM modelo 3D e o programa tenta pegar as texturas, gerar .msa/.wys e registrar no MeshList/MeshTextureList.bin (cliente original).");
                    ImGui.Spacing();

                    bool disablePick = busyConv || _pickConvFileTask != null;
                    if (disablePick) ImGui.BeginDisabled();
                    if (ImGui.Button("Selecionar modelo 3D..."))
                    {
                        _pickConvFileTask = Dialogs.PickFileAsync(
                            "Modelos 3D|*.glb;*.gltf;*.fbx;*.obj;*.dae;*.3ds|Todos|*.*",
                            _state.EnvFolder ?? _clientRoot,
                            "Selecionar modelo 3D para converter para WYD (.msa)");
                    }
                    if (disablePick) ImGui.EndDisabled();

                    ImGui.Spacing();
                    ImGui.TextDisabled("Suporta: GLB/GLTF/FBX/OBJ/DAE/3DS");

                    if (!string.IsNullOrWhiteSpace(_convPickedModel))
                    {
                        ImGui.Separator();
                        ImGui.TextDisabled("Selecionado:");
                        ImGui.Text(Path.GetFileName(_convPickedModel));
                        ImGui.TextDisabled(_convPickedModel);

                        bool canConv = !busyConv && File.Exists(_convPickedModel);
                        if (!canConv) ImGui.BeginDisabled();
                        if (ImGui.Button("Converter agora##convauto", new Vector2(-1, 0)))
                        {
                            StartConversion(_convPickedModel);
                            ImGui.CloseCurrentPopup();
                        }
                        if (!canConv) ImGui.EndDisabled();
                    }
                }
                else
                {
                    ImGui.TextWrapped("Manual: selecione TODOS os arquivos do modelo (ex: .gltf + .bin + texturas + .mtl). O programa copia para uma pasta temporaria mantendo a estrutura e converte de la.");
                    ImGui.Spacing();
                    ImGui.TextWrapped("Dica: os nomes/paths precisam bater com o que o modelo referencia (principalmente GLTF/OBJ).");
                    ImGui.Spacing();

                    bool disablePick = busyConv || _pickConvFilesTask != null;
                    if (disablePick) ImGui.BeginDisabled();
                    if (ImGui.Button("Selecionar arquivos (manual)..."))
                    {
                        _pickConvFilesTask = Dialogs.PickFilesAsync(
                            "Arquivos suportados|*.glb;*.gltf;*.bin;*.fbx;*.obj;*.mtl;*.dae;*.3ds;*.png;*.jpg;*.jpeg;*.tga;*.bmp|Todos|*.*",
                            _state.EnvFolder ?? _clientRoot,
                            "Selecionar todos os arquivos do modelo (manual)");
                    }
                    if (disablePick) ImGui.EndDisabled();

                    ImGui.Spacing();
                    ImGui.TextDisabled("Suporta: modelo + arquivos referenciados (bin/mtl/texturas)");

                    if (_convPickedFiles != null && _convPickedFiles.Length > 0)
                    {
                        ImGui.Separator();
                        ImGui.TextDisabled($"Arquivos selecionados: {_convPickedFiles.Length}");
                        ImGui.BeginChild("##convfiles", new Vector2(0, 90), true);
                        for (int i = 0; i < _convPickedFiles.Length; i++)
                        {
                            string f = _convPickedFiles[i];
                            string fn = Path.GetFileName(f);
                            bool isModel = _convPickedModel != null && string.Equals(f, _convPickedModel, StringComparison.OrdinalIgnoreCase);
                            if (isModel) ImGui.TextColored(new Vector4(0.55f, 0.85f, 1f, 1f), "MODELO: " + fn);
                            else ImGui.TextDisabled(fn);
                        }
                        ImGui.EndChild();

                        bool canConv = !busyConv && _convPickedModel != null && File.Exists(_convPickedModel);
                        if (!canConv) ImGui.BeginDisabled();
                        if (ImGui.Button("Converter manual agora##convman", new Vector2(-1, 0)))
                        {
                            string staged = StageManualFiles(_convPickedFiles);
                            if (!string.IsNullOrWhiteSpace(staged) && File.Exists(staged))
                            {
                                StartConversion(staged);
                                ImGui.CloseCurrentPopup();
                            }
                            else
                            {
                                _convStatus = "Falha no modo manual: nao achei o arquivo principal do modelo dentro da selecao.";
                            }
                        }
                        if (!canConv) ImGui.EndDisabled();
                    }
                }

                ImGui.Spacing();
                if (!string.IsNullOrWhiteSpace(_convStatus))
                    ImGui.TextDisabled(_convStatus);

                ImGui.Separator();
                if (ImGui.Button("Fechar"))
                    ImGui.CloseCurrentPopup();

                ImGui.EndPopup();
            }
        }

        ImGui.PopStyleVar(3);  // FramePadding + ItemSpacing + WindowPadding
        ImGui.PopStyleColor(); // WindowBg
        ImGui.End();
    } */

    private bool TbFileBtn(string label, bool disabled)
    {
        if (disabled) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.35f);
        bool clicked = ImGui.Button(label + "##tbf") && !disabled;
        if (disabled) ImGui.PopStyleVar();
        return clicked;
    }

    private bool TbArrowBtn(bool isRedo, bool disabled)
    {
        float btnH = ImGui.GetFrameHeight();
        float btnW = btnH + 6f;
        var   pos  = ImGui.GetCursorScreenPos();

        if (disabled) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.35f);
        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.06f, 0.10f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.00f, 0.35f, 0.72f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.00f, 0.50f, 1.00f, 1f));
        bool clicked = ImGui.Button("##" + (isRedo ? "redo" : "undo"), new Vector2(btnW, btnH)) && !disabled;
        ImGui.PopStyleColor(3);
        if (disabled) ImGui.PopStyleVar();

        var draw = ImGui.GetWindowDrawList();
        float cx = pos.X + btnW * 0.5f;
        float cy = pos.Y + btnH * 0.5f;
        float arrowAlpha = disabled ? 0.35f : 1.0f;
        uint  arrowCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.80f, 1.00f, arrowAlpha));
        const float AW = 7f;
        const float AH = 6f;
        const float SW = 2.5f;

        if (!isRedo)
        {
            draw.AddTriangleFilled(
                new Vector2(cx - AW, cy),
                new Vector2(cx,      cy - AH),
                new Vector2(cx,      cy + AH), arrowCol);
            draw.AddRectFilled(
                new Vector2(cx,      cy - SW),
                new Vector2(cx + AW, cy + SW), arrowCol);
        }
        else
        {
            draw.AddTriangleFilled(
                new Vector2(cx + AW, cy),
                new Vector2(cx,      cy - AH),
                new Vector2(cx,      cy + AH), arrowCol);
            draw.AddRectFilled(
                new Vector2(cx - AW, cy - SW),
                new Vector2(cx,      cy + SW), arrowCol);
        }

        return clicked;
    }

    private void TbToolBtn(string label, EditorTool tool)
    {
        bool active = (_state.ActiveTool == tool);
        var  pos    = ImGui.GetCursorScreenPos();

        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.00f, 0.28f, 0.60f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.00f, 0.40f, 0.80f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.00f, 0.55f, 1.00f, 1f));
        }
        if (ImGui.Button(label + "##tbt"))
        {
            if (tool is not (EditorTool.Select or EditorTool.Area or EditorTool.Move or EditorTool.Rotate or EditorTool.Scale))
            {
                _objRenderer.AreaSelectedObjects.Clear();
                _sceneDirty = true;
            }
            _state.ActiveTool = tool;
        }
        if (active)
        {
            ImGui.PopStyleColor(3);
            // Barra azul na base do botão para indicar ferramenta ativa
            var draw = ImGui.GetWindowDrawList();
            var sz   = ImGui.GetItemRectSize();
            draw.AddRectFilled(
                new Vector2(pos.X + 2, pos.Y + sz.Y - 3),
                new Vector2(pos.X + sz.X - 2, pos.Y + sz.Y),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.00f, 0.65f, 1.00f, 1f)),
                2f);
        }
    }

    private static void TbDivider()
    {
        ImGui.SameLine(0, 8);
        // Linha vertical via DrawList — mais clean que o "|" de texto
        var draw   = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        float h = ImGui.GetFrameHeight();
        draw.AddLine(
            new Vector2(cursor.X + 1, cursor.Y + 4),
            new Vector2(cursor.X + 1, cursor.Y + h - 4),
            0x44AACCFF, 1.2f);
        ImGui.Dummy(new Vector2(3, h));
        ImGui.SameLine(0, 8);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Tab bar de mapas
    // ────────────────────────────────────────────────────────────────────────

    private void DrawTabBar(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, COL_PANEL);
        ImGui.Begin("##tabbar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 2));
        ImGui.SetCursorPosY(4f);

        for (int i = 0; i < _openTabs.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool active = (i == _activeTab);
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.00f, 0.40f, 0.80f, 1f));
            if (ImGui.Button(_openTabs[i] + "##tab" + i)) _activeTab = i;
            if (active) ImGui.PopStyleColor();
        }
        ImGui.SameLine();
        if (ImGui.Button("  +  "))
        {
            OpenNewMap();
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        ImGui.End();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Hierarquia (painel esquerdo)
    // ────────────────────────────────────────────────────────────────────────

    private void DrawHierarchy(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, COL_PANEL);
        ImGui.Begin("##hier", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

        // Caminhos de configuração (compactos, dobráveis)
        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        if (ImGui.CollapsingHeader("Configuracao"))
        {
            DrawConfigSection();
        }

        ImGui.Separator();

        // Hierarquia do mapa
        string mapLabel = _state.IsLoaded
            ? (string.IsNullOrWhiteSpace(_selectedField) ? "Mapa" : _selectedField)
            : "Sem mapa";

        if (ImGui.TreeNodeEx(mapLabel, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Framed))
        {
            if (_state.Dat != null)
            {
                int total = _state.Dat.Records.Count;
                if (ImGui.TreeNodeEx($"Objetos ({total})##obj", _hierShowObj ? ImGuiTreeNodeFlags.DefaultOpen : 0))
                {
                    _hierShowObj = true;
                    TreeLeaf("Buildings");
                    TreeLeaf("Decorations");
                    TreeLeaf("Monsters");
                    TreeLeaf("NPCs");
                    TreeLeaf("Layers");
                    TreeLeaf("Players");
                    ImGui.TreePop();
                }
            }
            else
            {
                TreeLeaf("(carregue um mapa)");
            }

            TreeLeaf("Region");
            TreeLeaf("Area");
            ImGui.TreePop();
        }

        DrawDeleteMapModal();

        ImGui.PopStyleColor();
        ImGui.End();
    }

    private void DrawConfigSection()
    {
        SmallLabel("Cliente:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-30f);
        ImGui.InputText("##cl", ref _clientRoot, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("...##cl")) PickClientRoot();

        string resolvedClient = ResolveClientRoot();
        if (string.IsNullOrWhiteSpace(resolvedClient))
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.55f, 1f), "Root do cliente invalido (precisa ter pasta Env).");
        else
            ImGui.TextDisabled($"Root resolvido: {resolvedClient}");

        if (!string.IsNullOrWhiteSpace(_clientRoot) && ImGui.SmallButton("Auto-configurar"))
            AutoConfigure(_clientRoot);

        SmallLabel("Servidor:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-30f);
        ImGui.InputText("##srv", ref _serverRoot, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("...##srv"))
        {
            if (_pickServerTask == null)
                _pickServerTask = Dialogs.PickFolderAsync(_serverRoot, "Selecione a pasta do servidor (onde fica o heightmap.dat)");
        }

        SmallLabel("Heightmap:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-30f);
        ImGui.InputText("##hmap", ref _serverHeightmapPath, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("...##hmap"))
        {
            if (_pickHeightmapTask == null)
                _pickHeightmapTask = Dialogs.PickFileAsync(
                    "Heightmap|heightmap.dat;*.dat|Todos|*.*",
                    PickStartDir(_serverRoot),
                    "Selecione o heightmap.dat do servidor");
        }

        if (string.IsNullOrWhiteSpace(_serverHeightmapPath) && !string.IsNullOrWhiteSpace(_serverRoot))
        {
            string guess = Path.Combine(_serverRoot, "heightmap.dat");
            if (File.Exists(guess)) _serverHeightmapPath = guess;
        }

        SmallLabel("AttrMap:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-30f);
        ImGui.InputText("##srvattr", ref _serverAttributeMapPath, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("...##srvattr"))
        {
            if (_pickServerAttrTask == null)
                _pickServerAttrTask = Dialogs.PickFileAsync(
                    "AttributeMap|AttributeMap.dat;*.dat|Todos|*.*",
                    PickStartDir(_serverRoot),
                    "Selecione onde salvar o AttributeMap.dat do servidor");
        }

        if (string.IsNullOrWhiteSpace(_serverAttributeMapPath))
        {
            string? dir = null;
            if (!string.IsNullOrWhiteSpace(_serverHeightmapPath) && File.Exists(_serverHeightmapPath))
                dir = Path.GetDirectoryName(_serverHeightmapPath);
            else if (!string.IsNullOrWhiteSpace(_serverRoot) && Directory.Exists(_serverRoot))
                dir = _serverRoot;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                string guess = Path.Combine(dir!, "AttributeMap.dat");
                _serverAttributeMapPath = guess;
            }
        }

        bool saveAttrOnSave = _saveAttributeMapOnSave;
        if (ImGui.Checkbox("Ao salvar mapa, salvar AttributeMap", ref saveAttrOnSave))
            _saveAttributeMapOnSave = saveAttrOnSave;

        bool updClient = _updateClientAttributeMapOnSave;
        if (ImGui.Checkbox("Ao salvar, atualizar AttributeMap do cliente", ref updClient))
            _updateClientAttributeMapOnSave = updClient;

        bool updSrv = _updateServerAttributeMapOnSave;
        if (ImGui.Checkbox("Ao salvar, salvar AttributeMap no servidor", ref updSrv))
            _updateServerAttributeMapOnSave = updSrv;

        bool patchH = _patchServerHeightmapOnSave;
        if (ImGui.Checkbox("Ao salvar, atualizar heightmap do servidor", ref patchH))
            _patchServerHeightmapOnSave = patchH;

        SmallLabel("Env:   ");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-30f);
        ImGui.InputText("##env", ref _state.EnvFolder, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("...##env"))
        {
            if (_pickEnvTask == null)
                _pickEnvTask = Dialogs.PickFolderAsync(_state.EnvFolder, "Selecione a pasta Env");
        }

        if (!string.IsNullOrWhiteSpace(_state.EnvFolder) && Directory.Exists(_state.EnvFolder))
        {
            SmallLabel("Mapa:  ");
            ImGui.SameLine();
            DrawMapCombo();

            if (ImGui.Button("Carregar mapa", new Vector2(-1, 0)))
                LoadCurrentMap();

            bool canPatch = _state.IsLoaded && _state.Trn != null && File.Exists(_serverHeightmapPath);
            if (!canPatch) ImGui.BeginDisabled();
            if (ImGui.Button("Atualizar heightmap (server)", new Vector2(-1, 0)))
                PatchServerHeightmapFromCurrent();
            if (!canPatch) ImGui.EndDisabled();

            bool canDel = !string.IsNullOrWhiteSpace(_selectedField);
            if (!canDel) ImGui.BeginDisabled();
            if (ImGui.Button("Excluir mapa...", new Vector2(-1, 0)))
            {
                _deleteMapField = _selectedField;
                _deleteMapConfirm = "";
                _deleteMapAlsoMinimap = true;
                _showDeleteMapModal = true;
                ImGui.OpenPopup("Excluir mapa##modal");
            }
            if (!canDel) ImGui.EndDisabled();
        }

        if (!string.IsNullOrWhiteSpace(_state.LastError))
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), _state.LastError);
    }

    private string PickStartDir(string preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred)) return preferred;
        if (!string.IsNullOrWhiteSpace(_serverRoot) && Directory.Exists(_serverRoot)) return _serverRoot;
        if (!string.IsNullOrWhiteSpace(_clientRoot) && Directory.Exists(_clientRoot)) return _clientRoot;
        if (!string.IsNullOrWhiteSpace(_state.EnvFolder) && Directory.Exists(_state.EnvFolder)) return _state.EnvFolder;
        return Directory.GetCurrentDirectory();
    }

    private void DrawMapCombo()
    {
        if (_availableFields.Length == 0 && _scanTask == null)
            StartFieldScan();

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##fcombo", _selectedField))
        {
            ImGui.InputText("##ffilter", ref _mapFilter, 64);
            ImGui.Separator();
            foreach (var f in _availableFields)
            {
                if (!string.IsNullOrWhiteSpace(_mapFilter) &&
                    !f.Contains(_mapFilter, StringComparison.OrdinalIgnoreCase)) continue;
                bool sel = f == _selectedField;
                if (ImGui.Selectable(f, sel)) SelectField(f);
                if (sel) ImGui.SetItemDefaultFocus();
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                { SelectField(f); LoadCurrentMap(); ImGui.CloseCurrentPopup(); }
            }
            ImGui.EndCombo();
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Viewport 3D
    // ────────────────────────────────────────────────────────────────────────

    private void DrawViewport(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.03f, 0.03f, 0.07f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##vp", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);

        // Usa posição/tamanho reais da janela após Begin() para garantir que o clip rect
        // do splash/browser coincide exatamente com o que o ImGui está renderizando.
        // (SetNextWindowPos/Size são aplicados pelo ImGui no próximo Begin; ler de volta
        //  garante consistência mesmo no primeiro frame ou após resize.)
        var actualVpPos  = ImGui.GetWindowPos();
        var actualVpSize = ImGui.GetWindowSize();
        _vpPos  = actualVpPos;
        _vpSize = actualVpSize;

        int vpW = Math.Max(1, (int)actualVpSize.X);
        int vpH = Math.Max(1, (int)actualVpSize.Y);

        // Aba "Mapa" (índice 0) → navegador de mapas
        if (_activeTab == 0)
        {
            DrawMapBrowser(actualVpPos, actualVpSize);
        }
        else if (_state.IsLoaded && _terrain != null)
        {
            // Atualiza mesh se TRN mudou
            if (_state.Trn != null) _terrain.SetTrn(_state.Trn);
            _terrain.ShowGrid   = _state.ShowGrid;
            _terrain.WireFrame  = _state.VizMode == VisualizationMode.Wireframe;
            bool showAttrOverlay = _state.ShowAttributeOverlay ||
                                   _state.ActiveTool == EditorTool.AttributeMap ||
                                   _state.ActiveTool == EditorTool.Collision ||
                                   _state.ActiveTool == EditorTool.Trigger;
            // Reconstrói overlay se está marcado para exibir mas a textura GL foi perdida
            if (showAttrOverlay && _attrOverlayTex == 0 && _state.AttributeMap != null) RebuildAttrOverlay();
            // Rebuild incremental durante stroke de Collision/Trigger (feedback em tempo real)
            if (_attrOverlayDirty && _vpPainting &&
                (_state.ActiveTool == EditorTool.Collision || _state.ActiveTool == EditorTool.Trigger ||
                 _state.ActiveTool == EditorTool.AttributeMap))
            {
                RebuildAttrOverlay();
                _attrOverlayDirty = false;
            }
            _terrain.ShowAttributeOverlay = showAttrOverlay;
            _terrain.SetAttributeOverlayTexture(_attrOverlayTex);

            // Otimização: só renderiza 3D quando scene está dirty
            if (_sceneDirty)
            {
                _terrain.Render(_cam, vpW, vpH);

                // Renderizar objetos 3D no mesmo FBO do terreno
                if (_state.RenderMeshes && _state.Dat != null && _terrain.Fbo != 0)
                {
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, _terrain.Fbo);
                    GL.Viewport(0, 0, vpW, vpH);
                    var view = _cam.ViewMatrix;
                    var proj = _cam.ProjectionMatrix((float)vpW / vpH);
                    _objRenderer.Render(view, proj);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                }

                _sceneDirty = false;
            }

            // Exibe FBO como imagem ImGui (flip Y: UV (0,1)→(1,0))
            ImGui.Image(
                new IntPtr(_terrain.ColorTexture),
                new Vector2(vpW, vpH),
                new Vector2(0, 1), new Vector2(1, 0));

            // Interação mouse: usa rect check explícito (mais confiável que IsItemHovered sobre FBO)
            bool hovered3D = ImGui.IsMouseHoveringRect(actualVpPos, actualVpPos + actualVpSize, false);
            HandleViewportMouse(hovered3D);

            // Cursor de pincel no viewport (quando ferramenta ativa)
            DrawBrushCursor(actualVpPos, actualVpSize);

            // AABB wireframe do objeto selecionado
            DrawSelectionOverlay(actualVpPos, actualVpSize);

            // Gizmo de movimento (setas X/Y/Z) — só na ferramenta Move/Select
            if (_state.ActiveTool == EditorTool.Move || _state.ActiveTool == EditorTool.Select)
                DrawMoveGizmo(actualVpPos, actualVpSize);

            // Overlay de informação (canto superior esquerdo do viewport)
            DrawViewportOverlay(actualVpPos);

            // Tabs de visualização (canto superior direito do viewport, dentro)
            DrawViewModeButtons(actualVpPos, actualVpSize);

            // Retângulo de seleção da ferramenta Area
            if (_state.ActiveTool == EditorTool.Area && _areaSelDragging && _areaSelEnd != Vector2.Zero)
            {
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(_areaSelStart, _areaSelEnd, 0x330099FF);
                draw.AddRect(_areaSelStart, _areaSelEnd, 0xCC0099FF, 0f, ImDrawFlags.None, 1.5f);
            }
        }
        else
        {
            // Placeholder quando sem mapa
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(actualVpPos, actualVpPos + actualVpSize, 0xFF0A0A18);
            var msg = "Nenhum mapa carregado\nAbra um projeto pelo painel esquerdo";
            var ts = ImGui.CalcTextSize(msg);
            draw.AddText(actualVpPos + (actualVpSize - ts) * 0.5f, 0xFF888888, msg);
            ImGui.InvisibleButton("##vpholder", actualVpSize);
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        ImGui.End();
    }

    private void DrawViewportOverlay(Vector2 vpPos)
    {
        var draw = ImGui.GetWindowDrawList();
        var trn  = _state.Trn;
        if (trn == null) return;

        string mapName = string.IsNullOrWhiteSpace(_selectedField) ? trn.MapName : _selectedField;
        int tx = _state.HoverTileX, ty = _state.HoverTileY;
        bool hasTile = tx >= 0 && ty >= 0 && tx < 64 && ty < 64;

        // Monta texto do overlay linha a linha
        string line1 = mapName;
        string line2, line3, line4, line5, line6;

        if (hasTile)
        {
            var tile = trn.Tiles[tx + ty * 64];
            uint col = tile.Color;
            string colHex = $"{(col >> 16) & 0xFF:X2}{(col >> 8) & 0xFF:X2}{col & 0xFF:X2}";
            if (_hoverLocalX >= 0f && _hoverLocalY >= 0f)
            {
                int gx = trn.EnvPosX * 128 + (int)_hoverLocalX;
                int gy = trn.EnvPosY * 128 + (int)_hoverLocalY;
                line2 = $"X:{gx}  Y:{gy}";
            }
            else line2 = "";
            line3 = $"Tile: {tx},{ty}   idx={tile.TileIndex}";
            line4 = $"Altura: {tile.Height}";
            line5 = $"Back: {tile.BackTileIndex}   Cor: #{colHex}";

            // Linha 5: ferramenta + dica
            string paintSel = _state.TileNameById.TryGetValue(_state.SelectedTileIndex, out var pn)
                ? $"tile={_state.SelectedTileIndex} [{pn}]"
                : $"tile={_state.SelectedTileIndex}";
            line6 = _state.ActiveTool switch
            {
                EditorTool.Level        => "Terrain | Esq=elevar  Shift=abaixar  Ctrl=nivelar",
                EditorTool.PaintTexture => $"Paint | pintando {paintSel} (arrastar p/ pintar)",
                EditorTool.Select       => "Select+Move | clique p/ sel, arraste p/ mover",
                EditorTool.Move         => "Move | clique p/ sel, arraste p/ mover",
                EditorTool.AttributeMap => $"AttrMap | zona={_state.SelectedAttribute} [{GetAttrName(_state.SelectedAttribute)}]",
                _                       => $"Tool: {_state.ActiveTool}"
            };
        }
        else
        {
            line2 = "";
            line3 = "";
            line4 = "";
            line5 = _state.ActiveTool switch
            {
                EditorTool.Level        => "Tool: Terrain — clique e arraste para editar",
                EditorTool.PaintTexture => "Tool: Paint — clique e arraste para pintar",
                _                       => $"Tool: {_state.ActiveTool}"
            };
            line6 = "";
        }

        // Dimensões do bloco de overlay
        float lineH  = ImGui.GetTextLineHeight();
        int   lines  = hasTile ? 6 : 2;
        float boxW   = 300f;
        float boxH   = lines * lineH + 10f;

        var p = vpPos + new Vector2(10, 10);
        draw.AddRectFilled(p - new Vector2(4, 4), p + new Vector2(boxW, boxH), 0xBB0A0A0E, 4f);
        draw.AddRect(      p - new Vector2(4, 4), p + new Vector2(boxW, boxH), 0x33FFFFFF,  4f);

        // Cabeçalho do mapa em branco, resto em cinza claro
        draw.AddText(p,                          0xFFFFFFFF, line1);
        if (hasTile)
        {
            draw.AddText(p + new Vector2(0, lineH * 1), 0xFFCCCCCC, line2);
            draw.AddText(p + new Vector2(0, lineH * 2), 0xFFCCCCCC, line3);
            draw.AddText(p + new Vector2(0, lineH * 3), 0xFFCCCCCC, line4);
            draw.AddText(p + new Vector2(0, lineH * 4), 0xFFCCCCCC, line5);
            draw.AddText(p + new Vector2(0, lineH * 5), 0xFF88CC88, line6);
        }
        else
        {
            draw.AddText(p + new Vector2(0, lineH * 1), 0xFF88CC88, line5);
        }

        // ── Toast de notificação (objeto adicionado, etc.) ────────────────────
        if (_notifText.Length > 0 && ImGui.GetTime() < _notifUntil)
        {
            float alpha = (float)Math.Min(1.0, (_notifUntil - ImGui.GetTime()) * 2.5); // fade nos últimos 0.4s
            uint  bgCol = (uint)(0x00183060 | ((int)(alpha * 0xE0) << 24));
            uint  txCol = (uint)(0x00AAFFAA | ((int)(alpha * 0xFF) << 24));
            uint  brCol = (uint)(0x0033BB66 | ((int)(alpha * 0xCC) << 24));

            float nW  = 360f;
            float nH  = lineH + 12f;
            float nX  = vpPos.X + 10f;
            float nY  = vpPos.Y + boxH + 24f; // logo abaixo do overlay normal

            draw.AddRectFilled(new Vector2(nX, nY), new Vector2(nX + nW, nY + nH), bgCol, 5f);
            draw.AddRect(      new Vector2(nX, nY), new Vector2(nX + nW, nY + nH), brCol, 5f);
            draw.AddText(new Vector2(nX + 8, nY + 6), txCol, _notifText);
        }
    }

    /// <summary>Exibe uma notificação toast no viewport por <paramref name="seconds"/> segundos.</summary>
    private void ShowNotif(string text, float seconds = 3.5f)
    {
        _notifText  = text;
        _notifUntil = ImGui.GetTime() + seconds;
    }

    /// <summary>
    /// Projeta o círculo do pincel no viewport 2D usando pontos do tile hover.
    /// Desenha pontos ao redor do centro do hover para simular círculo no terreno.
    /// </summary>
    private void DrawBrushCursor(Vector2 vpPos, Vector2 vpSz)
    {
        int tx = _state.HoverTileX, ty = _state.HoverTileY;
        if (tx < 0 || ty < 0) return;
        if (_state.ActiveTool != EditorTool.Level &&
            _state.ActiveTool != EditorTool.PaintTexture &&
            _state.ActiveTool != EditorTool.AttributeMap &&
            _state.ActiveTool != EditorTool.Light &&
            _state.ActiveTool != EditorTool.Collision &&
            _state.ActiveTool != EditorTool.Trigger) return;

        var draw  = ImGui.GetWindowDrawList();
        var proj  = _cam.ProjectionMatrix(vpSz.X / vpSz.Y);
        var view  = _cam.ViewMatrix;
        var vp    = view * proj;  // row-vector: pos * (view * proj) = world → clip (igual ao ObjectRenderer)

        float radius =
            _state.ActiveTool == EditorTool.AttributeMap ? _state.AttrBrushRadius : _state.BrushRadius;
        bool isSquare =
            _state.ActiveTool == EditorTool.AttributeMap ||
            (_state.ActiveTool == EditorTool.Level && _state.SquareBrush) ||
            (_state.ActiveTool == EditorTool.PaintTexture && _state.PaintSquare);
        uint colEdge =
            _state.ActiveTool == EditorTool.AttributeMap
                ? AttributeMapFile.AttrToColor(_state.SelectedAttribute)
            : _state.ActiveTool == EditorTool.Collision
                ? 0xFF2266FF
            : _state.ActiveTool == EditorTool.Trigger
                ? 0xFF22CC66
            : _state.ActiveTool == EditorTool.Light
                ? (uint)(0xFF000000 | (_lightBrushColor & 0xFFFFFF) | 0xFF000000)
            : (_state.ActiveTool == EditorTool.Level ? 0xFF44FFFF : 0xFF44AAFF);

        // Centro do tile hover em world-space (tiles espaçados por TILE_SIZE=2)
        float cx = (tx + 0.5f) * 2f;
        float cz = (ty + 0.5f) * 2f;
        // Altura real do terreno no tile central (mesma escala do TerrainRenderer)
        float cy = _state.Trn != null
            ? _state.Trn.Tiles[tx + ty * 64].Height * TerrainRenderer.HEIGHT_SCALE
            : 0f;

        if (_state.ActiveTool == EditorTool.AttributeMap)
        {
            int br = Math.Clamp((int)MathF.Round(radius), 0, 10);
            float minX = (tx - br) * 2f;
            float minZ = (ty - br) * 2f;
            float maxX = (tx + br + 1) * 2f;
            float maxZ = (ty + br + 1) * 2f;

            var corners = new (float x, float z)[] { (minX, minZ), (maxX, minZ), (maxX, maxZ), (minX, maxZ) };
            Vector2? prev = null;
            for (int i = 0; i <= 4; i++)
            {
                var (wx, wz) = corners[i % 4];
                if (WorldToScreen(new OpenTK.Mathematics.Vector3(wx, cy, wz), vp, vpPos, vpSz, out var s))
                {
                    if (prev.HasValue) draw.AddLine(prev.Value, s, colEdge, 1.5f);
                    prev = s;
                }
                else prev = null;
            }
        }
        else if (isSquare)
        {
            var corners = new (float x, float z)[]
            {
                (cx - radius * 2f, cz - radius * 2f),
                (cx + radius * 2f, cz - radius * 2f),
                (cx + radius * 2f, cz + radius * 2f),
                (cx - radius * 2f, cz + radius * 2f),
            };
            Vector2? prev = null;
            for (int i = 0; i <= 4; i++)
            {
                var (wx, wz) = corners[i % 4];
                if (WorldToScreen(new OpenTK.Mathematics.Vector3(wx, cy, wz), vp, vpPos, vpSz, out var s))
                {
                    if (prev.HasValue) draw.AddLine(prev.Value, s, colEdge, 1.5f);
                    prev = s;
                }
                else prev = null;
            }
        }
        else
        {
            // Círculo com 32 segmentos
            const int SEG = 32;
            Vector2? prev = null;
            for (int i = 0; i <= SEG; i++)
            {
                float angle = i * MathF.PI * 2f / SEG;
                float wx = cx + MathF.Cos(angle) * radius * 2f;
                float wz = cz + MathF.Sin(angle) * radius * 2f;
                if (WorldToScreen(new OpenTK.Mathematics.Vector3(wx, cy, wz), vp, vpPos, vpSz, out var s))
                {
                    if (prev.HasValue) draw.AddLine(prev.Value, s, colEdge, 1.5f);
                    prev = s;
                }
                else prev = null;
            }
        }

        // Cruz no centro
        if (WorldToScreen(new OpenTK.Mathematics.Vector3(cx, cy, cz), vp, vpPos, vpSz, out var center))
        {
            draw.AddLine(center + new Vector2(-5, 0), center + new Vector2(5, 0), 0xFFFFFFFF, 1f);
            draw.AddLine(center + new Vector2(0, -5), center + new Vector2(0, 5), 0xFFFFFFFF, 1f);
        }
    }

    // ── Constantes do gizmo ─────────────────────────────────────────────────────
    private const float GIZMO_LEN    = 5f;   // comprimento das setas em unidades de mundo
    private const float GIZMO_HIT_PX = 10f;  // raio de hitbox em pixels

    // Estado de drag por eixo (gizmo)
    private bool   _gizmoDragging    = false;
    private float  _gizmoDragStartPosX;
    private float  _gizmoDragStartPosY;
    private float  _gizmoDragStartH;
    private Vector2 _gizmoDragMouseStart;

    /// <summary>
    /// Desenha as setas de gizmo (X=vermelho, Y=verde, Z=azul) sobre o objeto selecionado.
    /// Atualiza _hoverAxis com base na posição do mouse.
    /// </summary>
    private void DrawMoveGizmo(Vector2 vpPos, Vector2 vpSz)
    {
        int sel = _state.SelectedObjectIndex;
        if (sel < 0 || _state.Dat == null) return;
        if (sel >= _state.Dat.Records.Count) return;

        var rec  = _state.Dat.Records[sel];
        var view = _cam.ViewMatrix;
        var proj = _cam.ProjectionMatrix(vpSz.X / vpSz.Y);
        var vp   = view * proj;

        // Origem do gizmo = base do objeto; eleva ao topo da AABB se disponível
        float ox = rec.PosX;
        float oy = rec.Height * ObjectRenderer.HEIGHT_SCALE;
        float oz = rec.PosY;

        // Topo da AABB para elevação do gizmo (se mesh disponível)
        if (_objRenderer.TryGetWorldBounds(sel, out _, out var wMax))
            oy = wMax.Y;

        var gizmoCtr = new OpenTK.Mathematics.Vector3(ox, oy, oz);

        // Pontas das setas (mundo)
        var tipX = gizmoCtr + new OpenTK.Mathematics.Vector3(GIZMO_LEN, 0f, 0f);
        var tipY = gizmoCtr + new OpenTK.Mathematics.Vector3(0f, GIZMO_LEN, 0f);
        var tipZ = gizmoCtr + new OpenTK.Mathematics.Vector3(0f, 0f, GIZMO_LEN);

        if (!WorldToScreenUnclipped(gizmoCtr, vp, vpPos, vpSz, out var sc)) return;
        WorldToScreenUnclipped(tipX, vp, vpPos, vpSz, out var sx);
        WorldToScreenUnclipped(tipY, vp, vpPos, vpSz, out var sy);
        WorldToScreenUnclipped(tipZ, vp, vpPos, vpSz, out var sz);

        var draw = ImGui.GetWindowDrawList();
        var io   = ImGui.GetIO();
        var mouse = io.MousePos;

        // Detecta hover — somente quando não está em drag de gizmo
        if (!_gizmoDragging)
        {
            _hoverAxis = GizmoAxis.None;
            if (PointNearSegment(mouse, sc, sx, GIZMO_HIT_PX)) _hoverAxis = GizmoAxis.X;
            else if (PointNearSegment(mouse, sc, sy, GIZMO_HIT_PX)) _hoverAxis = GizmoAxis.Y;
            else if (PointNearSegment(mouse, sc, sz, GIZMO_HIT_PX)) _hoverAxis = GizmoAxis.Z;
        }

        // Cores (brilham quando hovering)
        uint colX = _hoverAxis == GizmoAxis.X ? 0xFF4444FF : 0xFF2222CC;
        uint colY = _hoverAxis == GizmoAxis.Y ? 0xFF44FF44 : 0xFF22CC22;
        uint colZ = _hoverAxis == GizmoAxis.Z ? 0xFFFF4444 : 0xFFCC2222;
        float lw  = 2.5f;

        // Desenha setas
        draw.AddLine(sc, sx, colX, lw);
        draw.AddCircleFilled(sx, 5f, colX);  // cabeça da seta X

        draw.AddLine(sc, sy, colY, lw);
        draw.AddCircleFilled(sy, 5f, colY);  // cabeça da seta Y

        draw.AddLine(sc, sz, colZ, lw);
        draw.AddCircleFilled(sz, 5f, colZ);  // cabeça da seta Z

        // Esfera branca no centro
        draw.AddCircleFilled(sc, 6f, 0xFFFFFFFF);
        draw.AddCircle(sc, 6f, 0xFF888888, 0, 1.5f);

        // ── Drag de gizmo ────────────────────────────────────────────────────
        bool leftDown    = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left) || _pendingClick;

        if (!_gizmoDragging && _hoverAxis != GizmoAxis.None && leftClicked)
        {
            // Salva estado ANTES do drag de gizmo para poder desfazer
            _state.PushUndoObjects();
            // Inicia drag por eixo
            _gizmoDragging       = true;
            _gizmoDragStartPosX  = rec.PosX;
            _gizmoDragStartPosY  = rec.PosY;
            _gizmoDragStartH     = rec.Height;
            _gizmoDragMouseStart = mouse;
        }

        if (_gizmoDragging && leftDown && _state.Dat != null)
        {
            var r  = _state.Dat.Records[sel];
            var dm = mouse - _gizmoDragMouseStart;

            // Jacobiano screen→world por eixo
            var vpFD = vp;
            if (WorldToScreenUnclipped(gizmoCtr, vpFD, vpPos, vpSz, out var s0) &&
                WorldToScreenUnclipped(gizmoCtr + new OpenTK.Mathematics.Vector3(1f, 0f, 0f), vpFD, vpPos, vpSz, out var sxD) &&
                WorldToScreenUnclipped(gizmoCtr + new OpenTK.Mathematics.Vector3(0f, 1f, 0f), vpFD, vpPos, vpSz, out var syD) &&
                WorldToScreenUnclipped(gizmoCtr + new OpenTK.Mathematics.Vector3(0f, 0f, 1f), vpFD, vpPos, vpSz, out var szD))
            {
                switch (_hoverAxis)
                {
                    case GizmoAxis.X:
                    {
                        var axDir = sxD - s0;
                        float axLen = axDir.Length();
                        if (axLen > 0.1f)
                        {
                            float proj1D = Vector2.Dot(dm, axDir) / axLen;
                            r.PosX = Math.Clamp(_gizmoDragStartPosX + proj1D / axLen, 0f, 127f);
                            _sceneDirty = true;
                        }
                        break;
                    }
                    case GizmoAxis.Y:
                    {
                        var axDir = syD - s0;
                        float axLen = axDir.Length();
                        if (axLen > 0.1f)
                        {
                            float proj1D = Vector2.Dot(dm, axDir) / axLen;
                            r.Height = Math.Clamp(_gizmoDragStartH + proj1D / axLen * 4f, -500f, 500f);
                            _sceneDirty = true;
                        }
                        break;
                    }
                    case GizmoAxis.Z:
                    {
                        var axDir = syD - s0;
                        float axLen = axDir.Length();
                        if (axLen > 0.1f)
                        {
                            float proj1D = Vector2.Dot(dm, axDir) / axLen;
                            r.PosY = Math.Clamp(_gizmoDragStartPosY + proj1D / axLen, 0f, 127f);
                            _sceneDirty = true;
                        }
                        break;
                    }
                }
                _state.Dat.Records[sel] = r;
                _statusText = $"Gizmo [{sel}] eixo={_hoverAxis}  Pos=({r.PosX:F1},{r.PosY:F1})  H={r.Height:F1}";
            }
        }
        else if (_gizmoDragging && !leftDown)
        {
            _gizmoDragging = false;
            _statusText = "Gizmo aplicado. Ctrl+Z para desfazer.";
        }
    }

    /// <summary>
    /// Retorna true se o ponto P está a menos de maxDist pixels do segmento A→B.
    /// </summary>
    private static bool PointNearSegment(Vector2 p, Vector2 a, Vector2 b, float maxDist)
    {
        var ab  = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq < 0.01f) return Vector2.Distance(p, a) < maxDist;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        var closest = a + ab * t;
        return Vector2.Distance(p, closest) < maxDist;
    }

    /// <summary>
    /// Desenha o wireframe da AABB do objeto selecionado sobre o viewport.
    /// </summary>
    private void DrawSelectionOverlay(Vector2 vpPos, Vector2 vpSz)
    {
        int sel = _state.SelectedObjectIndex;
        if (sel < 0) return;
        if (!_objRenderer.TryGetWorldBounds(sel, out var wMin, out var wMax)) return;

        var draw = ImGui.GetWindowDrawList();
        var proj = _cam.ProjectionMatrix(vpSz.X / vpSz.Y);
        var view = _cam.ViewMatrix;
        var vp   = view * proj;

        uint col  = _isDraggingObj ? 0xFF00FFFF : 0xFFFFDD00; // ciano durante drag, amarelo parado
        float lw  = _isDraggingObj ? 2.0f : 1.5f;

        // 8 vértices do AABB
        var verts = new OpenTK.Mathematics.Vector3[]
        {
            new(wMin.X, wMin.Y, wMin.Z), new(wMax.X, wMin.Y, wMin.Z),
            new(wMax.X, wMin.Y, wMax.Z), new(wMin.X, wMin.Y, wMax.Z),
            new(wMin.X, wMax.Y, wMin.Z), new(wMax.X, wMax.Y, wMin.Z),
            new(wMax.X, wMax.Y, wMax.Z), new(wMin.X, wMax.Y, wMax.Z),
        };

        // 12 arestas do cubo
        int[,] edges = {
            {0,1},{1,2},{2,3},{3,0},  // face de baixo
            {4,5},{5,6},{6,7},{7,4},  // face de cima
            {0,4},{1,5},{2,6},{3,7},  // laterais
        };

        for (int e = 0; e < 12; e++)
        {
            if (WorldToScreen(verts[edges[e,0]], vp, vpPos, vpSz, out var s0) &&
                WorldToScreen(verts[edges[e,1]], vp, vpPos, vpSz, out var s1))
            {
                draw.AddLine(s0, s1, col, lw);
            }
        }
    }

    private static bool WorldToScreen(OpenTK.Mathematics.Vector3 world,
                                      OpenTK.Mathematics.Matrix4 viewProj,
                                      Vector2 vpPos, Vector2 vpSz,
                                      out Vector2 screen)
    {
        var clip = new OpenTK.Mathematics.Vector4(world.X, world.Y, world.Z, 1f) * viewProj;
        if (clip.W < 0.001f) { screen = Vector2.Zero; return false; }
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        screen = vpPos + new Vector2(
            (ndcX + 1f) * 0.5f * vpSz.X,
            (1f - ndcY) * 0.5f * vpSz.Y);
        return screen.X >= vpPos.X && screen.X <= vpPos.X + vpSz.X &&
               screen.Y >= vpPos.Y && screen.Y <= vpPos.Y + vpSz.Y;
    }

    /// <summary>
    /// Igual a WorldToScreen mas sem clipping de viewport — usado para gizmos
    /// que podem ter pontas levemente fora da área visível.
    /// </summary>
    private static bool WorldToScreenUnclipped(OpenTK.Mathematics.Vector3 world,
                                               OpenTK.Mathematics.Matrix4 viewProj,
                                               Vector2 vpPos, Vector2 vpSz,
                                               out Vector2 screen)
    {
        var clip = new OpenTK.Mathematics.Vector4(world.X, world.Y, world.Z, 1f) * viewProj;
        if (clip.W < 0.001f) { screen = Vector2.Zero; return false; }
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        // Limitar NDC a ±2 para evitar posições absurdas fora de tela
        if (ndcX < -2f || ndcX > 2f || ndcY < -2f || ndcY > 2f)
        { screen = Vector2.Zero; return false; }
        screen = vpPos + new Vector2(
            (ndcX + 1f) * 0.5f * vpSz.X,
            (1f - ndcY) * 0.5f * vpSz.Y);
        return true;
    }

    private void DrawViewModeButtons(Vector2 vpPos, Vector2 vpSz)
    {
        var tabs = new[] { "Perspective", "Lit", "Show", "Grid", "Collision" };
        float btnW = 82f;
        float startX = vpPos.X + vpSz.X - tabs.Length * (btnW + 2) - 6;
        float startY = vpPos.Y + 6;

        var draw = ImGui.GetWindowDrawList();
        var io   = ImGui.GetIO();

        for (int i = 0; i < tabs.Length; i++)
        {
            var p0 = new Vector2(startX + i * (btnW + 2), startY);
            var p1 = p0 + new Vector2(btnW, 20);
            bool hover = io.MousePos.X >= p0.X && io.MousePos.X <= p1.X &&
                         io.MousePos.Y >= p0.Y && io.MousePos.Y <= p1.Y;
            uint bg = hover ? 0xCC3377BB : 0xAA0A0A22;
            draw.AddRectFilled(p0, p1, bg, 4f);
            draw.AddText(p0 + new Vector2(6, 3), hover ? 0xFFDDEEFF : 0xFFAABBCC, tabs[i]);
        }
    }

    private void HandleViewportMouse(bool hovered)
    {
        var io    = ImGui.GetIO();
        var mouse = io.MousePos;

        if (_newMapModalOpen || _requestOpenNewMapModal) return;

        // Sincroniza índice de seleção com o ObjectRenderer
        _objRenderer.SelectedObjectIndex = _state.SelectedObjectIndex;

        // Atualiza tile sob cursor — também durante _vpPainting para manter rastreamento contínuo
        bool needsHover = hovered || _vpPainting;
        if (needsHover && _state.IsLoaded && _state.Trn != null)
        {
            float mx = mouse.X - _vpPos.X;
            float my = mouse.Y - _vpPos.Y;
            // Clamp dentro do viewport para evitar raio fora de tela durante drag
            mx = Math.Clamp(mx, 0f, _vpSize.X - 1f);
            my = Math.Clamp(my, 0f, _vpSize.Y - 1f);
            if (_cam.Unproject(mx, my, _vpSize.X, _vpSize.Y, out var ro, out var rd))
            {
                float planeY = (_state.HoverTileX >= 0 && _state.HoverTileY >= 0 &&
                                _state.HoverTileX < 64 && _state.HoverTileY < 64)
                    ? _state.Trn.Tiles[_state.HoverTileX + _state.HoverTileY * 64].Height * TerrainRenderer.HEIGHT_SCALE
                    : _state.Trn.Tiles[32 + 32 * 64].Height * TerrainRenderer.HEIGHT_SCALE;

                int lastTx = -1, lastTy = -1;
                OpenTK.Mathematics.Vector3 hit = OpenTK.Mathematics.Vector3.Zero;

                for (int iter = 0; iter < 4; iter++)
                {
                    if (!Camera3D.RayHitPlaneWorld(ro, rd, planeY, out hit)) break;
                    int tx = Math.Clamp((int)(hit.X / TerrainRenderer.TILE_SIZE), 0, 63);
                    int ty = Math.Clamp((int)(hit.Z / TerrainRenderer.TILE_SIZE), 0, 63);
                    float ny = _state.Trn.Tiles[tx + ty * 64].Height * TerrainRenderer.HEIGHT_SCALE;
                    if (tx == lastTx && ty == lastTy && MathF.Abs(ny - planeY) < 0.0005f) break;
                    lastTx = tx; lastTy = ty; planeY = ny;
                }

                if (lastTx >= 0 && lastTy >= 0)
                {
                    _state.HoverTileX = lastTx;
                    _state.HoverTileY = lastTy;
                    _hoverLocalX = Math.Clamp(hit.X, 0f, 127.999f);
                    _hoverLocalY = Math.Clamp(hit.Z, 0f, 127.999f);
                }
            }
        }
        else if (!_isDraggingObj)
        {
            _state.HoverTileX = _state.HoverTileY = -1;
            _hoverLocalX = _hoverLocalY = -1f;
        }

        if (!hovered && !_vpDraggingOrbit && !_vpDraggingPan && !_vpPainting && !_isDraggingObj) return;

        // Scroll = zoom
        if (hovered && io.MouseWheel != 0)
        {
            _cam.Zoom(io.MouseWheel);
            _sceneDirty = true;
        }

        // Botão direito = orbit
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            if (!_vpDraggingOrbit)
            {
                _vpDraggingOrbit = true;
                _vpLastMouse = mouse;
            }
            else
            {
                var delta = mouse - _vpLastMouse;
                _cam.Orbit(-delta.X * 0.4f, -delta.Y * 0.3f);
                _vpLastMouse = mouse;
                _sceneDirty = true;
            }
        }
        else { _vpDraggingOrbit = false; }

        // Botão do meio = pan
        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            if (!_vpDraggingPan)
            {
                _vpDraggingPan = true;
                _vpLastMouse   = mouse;
            }
            else
            {
                var delta = mouse - _vpLastMouse;
                _cam.Pan(delta.X, delta.Y);
                _vpLastMouse = mouse;
                _sceneDirty = true;
            }
        }
        else { _vpDraggingPan = false; }

        // ── Botão esquerdo — depende da ferramenta ativa ──────────────────
        bool leftDown    = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool anyActive   = ImGui.IsAnyItemActive();

        // Gizmo tem prioridade: se o mouse estava sobre uma seta OU está em drag de gizmo, não processa clique aqui
        bool gizmoHovered = (_hoverAxis != GizmoAxis.None) || _gizmoDragging;

        switch (_state.ActiveTool)
        {
            // ── Selecionar / Mover objeto ─────────────────────────────────
            // Select e Move compartilham a mesma lógica:
            //   clique simples → seleciona; clique+arrasto → move.
            case EditorTool.Select:
            case EditorTool.Move:
                if (_isDraggingObj)
                {
                    // ── Continuar drag ───────────────────────────────────
                    if (leftDown && _state.Dat != null)
                    {
                        bool shiftHeld = KeyboardState.IsKeyDown(Keys.LeftShift) ||
                                         KeyboardState.IsKeyDown(Keys.RightShift);

                        var r  = _state.Dat.Records[_draggedObjIdx];
                        var md = mouse - _freeDragOriginMouse;

                        if (shiftHeld)
                        {
                            // ── Shift+drag = mover verticalmente (Height) ────
                            float dH = -md.Y * 0.25f;
                            if (_areaDragStarts.Count > 1)
                            {
                                foreach (var (aIdx, aStart) in _areaDragStarts)
                                {
                                    var ar = _state.Dat.Records[aIdx];
                                    ar.Height = Math.Clamp(aStart.H + dH, -500f, 500f);
                                    _state.Dat.Records[aIdx] = ar;
                                }
                                _statusText = $"Altura [{_areaDragStarts.Count} objs]  (solte Shift para mover X/Z)";
                            }
                            else
                            {
                                float newH = Math.Clamp(_dragStartHeight + dH, -500f, 500f);
                                r.Height = newH;
                                _state.Dat.Records[_draggedObjIdx] = r;
                                _statusText = $"Altura [{_draggedObjIdx}]  Height={newH:F2}  (solte Shift para mover X/Z)";
                            }
                        }
                        else
                        {
                            // ── Drag normal = mover X/Z (Jacobian screen-space) ──
                            var vpFD     = _cam.ViewMatrix * _cam.ProjectionMatrix(_vpSize.X / _vpSize.Y);
                            var vpPosFD  = new Vector2(_vpPos.X, _vpPos.Y);
                            var vpSzFD   = new Vector2(_vpSize.X, _vpSize.Y);
                            var originFD = new OpenTK.Mathematics.Vector3(_dragStartPosX, _dragPlaneY, _dragStartPosY);

                            if (WorldToScreenUnclipped(originFD, vpFD, vpPosFD, vpSzFD, out var scFD) &&
                                WorldToScreenUnclipped(originFD + new OpenTK.Mathematics.Vector3(1f, 0f, 0f), vpFD, vpPosFD, vpSzFD, out var sxFD) &&
                                WorldToScreenUnclipped(originFD + new OpenTK.Mathematics.Vector3(0f, 0f, 1f), vpFD, vpPosFD, vpSzFD, out var szFD))
                            {
                                float a = sxFD.X - scFD.X, b = szFD.X - scFD.X;
                                float c = sxFD.Y - scFD.Y, d = szFD.Y - scFD.Y;
                                float det = a * d - b * c;
                                if (MathF.Abs(det) > 0.01f)
                                {
                                    float worldDX = ( d * md.X - b * md.Y) / det;
                                    float worldDZ = (-c * md.X + a * md.Y) / det;
                                    if (_areaDragStarts.Count > 1)
                                    {
                                        foreach (var (aIdx, aStart) in _areaDragStarts)
                                        {
                                            var ar = _state.Dat.Records[aIdx];
                                            ar.PosX = Math.Clamp(aStart.X + worldDX, 0f, 127f);
                                            ar.PosY = Math.Clamp(aStart.Y + worldDZ, 0f, 127f);
                                            _state.Dat.Records[aIdx] = ar;
                                        }
                                        _sceneDirty = true;
                                        _statusText = $"Movendo [{_areaDragStarts.Count} objs]  Shift=altura";
                                    }
                                    else
                                    {
                                        float newX = Math.Clamp(_dragStartPosX + worldDX, 0f, 127f);
                                        float newY = Math.Clamp(_dragStartPosY + worldDZ, 0f, 127f);
                                        r.PosX = newX;
                                        r.PosY = newY;
                                        _state.Dat.Records[_draggedObjIdx] = r;
                                        _sceneDirty = true;
                                        _statusText = $"Movendo [{_draggedObjIdx}]  Pos=({newX:F1},{newY:F1})  Shift=altura";
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Botão liberado: finaliza drag
                        _isDraggingObj = false;
                        _statusText = "Movimento aplicado. Use Ctrl+Z para desfazer.";
                    }
                }
                else
                {
                    // ── Click: seleciona o objeto (drag só começa após threshold) ──
                    bool clickNow = leftClicked || _pendingClick;
                    if (!gizmoHovered && hovered && clickNow && _state.IsLoaded && !anyActive)
                    {
                        float mx2 = mouse.X - _vpPos.X;
                        float my2 = mouse.Y - _vpPos.Y;
                        var viewProj2 = _cam.ViewMatrix * _cam.ProjectionMatrix(_vpSize.X / _vpSize.Y);
                        var vpPx2     = new OpenTK.Mathematics.Vector2(mx2, my2);
                        var vpSz2     = new OpenTK.Mathematics.Vector2(_vpSize.X, _vpSize.Y);

                        // Picking cascata:
                        // 1) Ray-AABB (depth-correct): seleciona o mais próximo da câmera
                        int picked = -1;
                        if (_cam.Unproject(mx2, my2, _vpSize.X, _vpSize.Y, out var ro2b, out var rd2b))
                            picked = _objRenderer.TryPickObject(ro2b, rd2b);
                        // 2) Screen-bounds: fallback para objetos sem mesh carregada
                        if (picked < 0)
                            picked = _objRenderer.TryPickScreenBounds(viewProj2, vpPx2, vpSz2);
                        // 3) Centro mais próximo (raio 48px): último recurso
                        if (picked < 0)
                            picked = _objRenderer.TryPickNearest(viewProj2, vpPx2, vpSz2, 48f);

                        _state.SelectedObjectIndex = picked;

                        if (picked >= 0 && _state.Dat != null)
                        {
                            var r = _state.Dat.Records[picked];
                            _statusText = $"Selecionado: [{picked}] ObjType={r.ObjType}  Pos=({r.PosX:F1},{r.PosY:F1})  | Shift+drag=altura";

                            // Guarda dados do objeto para eventual drag (não inicia ainda)
                            _pendingDragObj       = picked;
                            _pendingDragMouseStart = mouse;
                            _dragPlaneY           = r.Height * ObjectRenderer.HEIGHT_SCALE;
                            _dragStartPosX        = r.PosX;
                            _dragStartPosY        = r.PosY;
                            _dragStartHeight      = r.Height;
                        }
                        else
                        {
                            _pendingDragObj = -1;
                            _statusText     = "Nenhum objeto selecionado.";
                            _objRenderer.AreaSelectedObjects.Clear();
                            _sceneDirty = true;
                        }
                    }

                    // ── Threshold → inicia drag quando mouse moveu o suficiente ──
                    if (_pendingDragObj >= 0 && leftDown && _state.Dat != null)
                    {
                        var dm = mouse - _pendingDragMouseStart;
                        if (dm.LengthSquared() > DragThresholdPx * DragThresholdPx)
                        {
                            // Salva estado ANTES do drag para poder desfazer
                            _state.PushUndoObjects();
                            // Drag começa a partir da posição ATUAL do mouse (sem salto)
                            _isDraggingObj       = true;
                            _draggedObjIdx       = _pendingDragObj;
                            _freeDragOriginMouse = mouse;
                            _pendingDragObj      = -1;
                            var r = _state.Dat.Records[_draggedObjIdx];
                            _statusText = $"Movendo [{_draggedObjIdx}] ObjType={r.ObjType}  Shift=altura";

                            // Popula posições iniciais para multi-drag por área
                            _areaDragStarts.Clear();
                            if (_objRenderer.AreaSelectedObjects.Count > 1 &&
                                _objRenderer.AreaSelectedObjects.Contains(_draggedObjIdx))
                            {
                                foreach (int aIdx in _objRenderer.AreaSelectedObjects)
                                {
                                    if (aIdx < _state.Dat.Records.Count)
                                    {
                                        var ar = _state.Dat.Records[aIdx];
                                        _areaDragStarts[aIdx] = (ar.PosX, ar.PosY, ar.Height);
                                    }
                                }
                            }
                        }
                    }

                    // Cancela pending se botão liberado antes do threshold
                    if (!leftDown) _pendingDragObj = -1;
                }
                break;

            case EditorTool.Rotate:
                if (_isRotatingObj)
                {
                    if (leftDown && _state.Dat != null && _state.SelectedObjectIndex >= 0 &&
                        _state.SelectedObjectIndex < _state.Dat.Records.Count)
                    {
                        bool shiftHeld = KeyboardState.IsKeyDown(Keys.LeftShift) ||
                                         KeyboardState.IsKeyDown(Keys.RightShift);
                        float factor = shiftHeld ? 0.0025f : 0.01f;
                        float da = (mouse.X - _rotMouseStart.X) * factor;
                        if (_areaRotStarts.Count > 1)
                        {
                            foreach (var (aIdx, startAngle) in _areaRotStarts)
                            {
                                var ar = _state.Dat.Records[aIdx];
                                ar.Angle = startAngle + da;
                                _state.Dat.Records[aIdx] = ar;
                            }
                            _sceneDirty = true;
                            _statusText = $"Rotacionando [{_areaRotStarts.Count} objs]  Δangle={da:F3}";
                        }
                        else
                        {
                            var r = _state.Dat.Records[_state.SelectedObjectIndex];
                            r.Angle = _rotStartAngle + da;
                            _state.Dat.Records[_state.SelectedObjectIndex] = r;
                            _sceneDirty = true;
                            _statusText = $"Rotacionando [{_state.SelectedObjectIndex}] Angle={r.Angle:F3}";
                        }
                    }
                    else
                    {
                        _isRotatingObj = false;
                        _rotObjIdx = -1;
                        _statusText = "Rotacao aplicada. Use Ctrl+Z para desfazer.";
                    }
                }
                else
                {
                    bool clickNow = leftClicked || _pendingClick;
                    if (!gizmoHovered && hovered && clickNow && _state.IsLoaded && !anyActive)
                    {
                        float mx2 = mouse.X - _vpPos.X;
                        float my2 = mouse.Y - _vpPos.Y;
                        var viewProj2 = _cam.ViewMatrix * _cam.ProjectionMatrix(_vpSize.X / _vpSize.Y);
                        var vpPx2 = new OpenTK.Mathematics.Vector2(mx2, my2);
                        var vpSz2 = new OpenTK.Mathematics.Vector2(_vpSize.X, _vpSize.Y);

                        int picked = -1;
                        if (_cam.Unproject(mx2, my2, _vpSize.X, _vpSize.Y, out var ro2b, out var rd2b))
                            picked = _objRenderer.TryPickObject(ro2b, rd2b);
                        if (picked < 0)
                            picked = _objRenderer.TryPickScreenBounds(viewProj2, vpPx2, vpSz2);
                        if (picked < 0)
                            picked = _objRenderer.TryPickNearest(viewProj2, vpPx2, vpSz2, 48f);

                        _state.SelectedObjectIndex = picked;
                        _rotObjIdx = picked;
                        _rotMouseStart = mouse;
                        if (picked >= 0 && _state.Dat != null)
                        {
                            _rotStartAngle = _state.Dat.Records[picked].Angle;
                            _statusText = $"Selecionado: [{picked}] (arraste para rotacionar)";
                        }
                        else
                        {
                            _rotStartAngle = 0f;
                            _statusText = "Nenhum objeto selecionado.";
                        }
                    }

                    if (_rotObjIdx >= 0 && leftDown && _state.Dat != null)
                    {
                        var dm = mouse - _rotMouseStart;
                        if (dm.LengthSquared() > DragThresholdPx * DragThresholdPx)
                        {
                            _state.PushUndoObjects();
                            _isRotatingObj = true;

                            // Popula ângulos iniciais para multi-rotate por área
                            _areaRotStarts.Clear();
                            if (_objRenderer.AreaSelectedObjects.Count > 1 &&
                                _objRenderer.AreaSelectedObjects.Contains(_rotObjIdx))
                            {
                                foreach (int aIdx in _objRenderer.AreaSelectedObjects)
                                {
                                    if (aIdx < _state.Dat.Records.Count)
                                        _areaRotStarts[aIdx] = _state.Dat.Records[aIdx].Angle;
                                }
                            }
                        }
                    }

                    if (!leftDown)
                        _rotObjIdx = -1;
                }
                break;

            case EditorTool.Scale:
                if (_isScalingObj)
                {
                    if (leftDown && _state.Dat != null && _state.SelectedObjectIndex >= 0 &&
                        _state.SelectedObjectIndex < _state.Dat.Records.Count)
                    {
                        bool shiftHeld = KeyboardState.IsKeyDown(Keys.LeftShift) ||
                                         KeyboardState.IsKeyDown(Keys.RightShift);
                        float factor = shiftHeld ? 0.0025f : 0.01f;
                        var r = _state.Dat.Records[_state.SelectedObjectIndex];
                        float dy = (mouse.Y - _scaleMouseStart.Y);
                        float mul = 1f + (-dy) * factor;
                        mul = Math.Clamp(mul, 0.05f, 10f);
                        r.HasScale = true;
                        r.ScaleH = Math.Clamp(_scaleStartH * mul, 0.05f, 20f);
                        r.ScaleV = Math.Clamp(_scaleStartV * mul, 0.05f, 20f);
                        _state.Dat.Records[_state.SelectedObjectIndex] = r;
                        _sceneDirty = true;
                        _statusText = $"Escalando [{_state.SelectedObjectIndex}] Scale=({r.ScaleH:F2},{r.ScaleV:F2})";
                    }
                    else
                    {
                        _isScalingObj = false;
                        _scaleObjIdx = -1;
                        _statusText = "Escala aplicada. Use Ctrl+Z para desfazer.";
                    }
                }
                else
                {
                    bool clickNow = leftClicked || _pendingClick;
                    if (!gizmoHovered && hovered && clickNow && _state.IsLoaded && !anyActive)
                    {
                        float mx2 = mouse.X - _vpPos.X;
                        float my2 = mouse.Y - _vpPos.Y;
                        var viewProj2 = _cam.ViewMatrix * _cam.ProjectionMatrix(_vpSize.X / _vpSize.Y);
                        var vpPx2 = new OpenTK.Mathematics.Vector2(mx2, my2);
                        var vpSz2 = new OpenTK.Mathematics.Vector2(_vpSize.X, _vpSize.Y);

                        int picked = -1;
                        if (_cam.Unproject(mx2, my2, _vpSize.X, _vpSize.Y, out var ro2b, out var rd2b))
                            picked = _objRenderer.TryPickObject(ro2b, rd2b);
                        if (picked < 0)
                            picked = _objRenderer.TryPickScreenBounds(viewProj2, vpPx2, vpSz2);
                        if (picked < 0)
                            picked = _objRenderer.TryPickNearest(viewProj2, vpPx2, vpSz2, 48f);

                        _state.SelectedObjectIndex = picked;
                        _scaleObjIdx = picked;
                        _scaleMouseStart = mouse;
                        if (picked >= 0 && _state.Dat != null)
                        {
                            var sr = _state.Dat.Records[picked];
                            _scaleStartH = sr.HasScale ? sr.ScaleH : 1f;
                            _scaleStartV = sr.HasScale ? sr.ScaleV : 1f;
                            _statusText = $"Selecionado: [{picked}] (arraste para escalar)";
                        }
                        else
                        {
                            _scaleStartH = _scaleStartV = 1f;
                            _statusText = "Nenhum objeto selecionado.";
                        }
                    }

                    if (_scaleObjIdx >= 0 && leftDown && _state.Dat != null)
                    {
                        var dm = mouse - _scaleMouseStart;
                        if (dm.LengthSquared() > DragThresholdPx * DragThresholdPx)
                        {
                            _state.PushUndoObjects();
                            _isScalingObj = true;
                        }
                    }

                    if (!leftDown)
                        _scaleObjIdx = -1;
                }
                break;





            // ── Editar terreno ────────────────────────────────────────────
            case EditorTool.Level:
                if ((hovered || _vpPainting) && leftDown && _state.IsLoaded && !anyActive)
                {
                    bool shift = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
                    bool ctrl  = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);

                    int tx = _state.HoverTileX, ty = _state.HoverTileY;
                    if (tx >= 0 && ty >= 0)
                    {
                        if (!_levelStrokeActive)
                        {
                            _state.PushUndoPaint();
                            _levelStrokeActive = true;
                        }

                        _state.FlattenMode = ctrl;
                        _state.RaiseDelta  = shift ? -Math.Abs(_state.RaiseDelta) : Math.Abs(_state.RaiseDelta);
                        if (_state.ApplyTool(tx, ty))
                        {
                            _terrain?.MarkDirty();
                            _sceneDirty = true;
                            if (_state.Trn != null)
                            {
                                string modeStr = _state.FlattenMode ? "Nivelando" : (shift ? "Abaixando" : "Elevando");
                                _statusText = $"{modeStr} ({tx},{ty}) altura={_state.Trn.Tiles[tx + ty * 64].Height}";
                            }
                        }
                    }
                    else
                    {
                        _statusText = $"Cursor fora do mapa ({_state.HoverTileX},{_state.HoverTileY})";
                    }
                    _vpPainting = true;
                }
                else
                {
                    if (_vpPainting && _levelStrokeActive)
                    {
                        _terrain?.MarkDirty();
                        _sceneDirty = true;
                        _state.RebuildMinimap();
                    }
                    _vpPainting = false;
                    _levelStrokeActive = false;
                }
                break;

            case EditorTool.PaintTexture:
                if ((hovered || _vpPainting) && leftDown && _state.IsLoaded && !anyActive)
                {
                    int tx = _state.HoverTileX, ty = _state.HoverTileY;
                    if (tx >= 0 && ty >= 0)
                    {
                        if (_state.Trn != null)
                        {
                            if (!_paintStrokeActive)
                            {
                                _state.PushUndoPaint();
                                _paintStrokeActive = true;
                            }

                            int r2 = (int)Math.Ceiling(_state.BrushRadius);
                            float br = _state.BrushRadius;
                            for (int dy2 = -r2; dy2 <= r2; dy2++)
                            {
                                for (int dx2 = -r2; dx2 <= r2; dx2++)
                                {
                                    if (_state.PaintSquare)
                                    {
                                        if (Math.Abs(dx2) > br || Math.Abs(dy2) > br) continue;
                                    }
                                    else
                                    {
                                        if (dx2 * dx2 + dy2 * dy2 > br * br) continue;
                                    }
                                    int ttx = tx + dx2, tty = ty + dy2;
                                    if (ttx < 0 || ttx >= 64 || tty < 0 || tty >= 64) continue;
                                    ref var tl = ref _state.Trn.Tiles[ttx + tty * 64];
                                    tl.TileIndex = (byte)_state.SelectedTileIndex;
                                }
                            }

                            _terrain?.MarkTileMapDirty();

                            string tileName = _state.TileNameById.TryGetValue(_state.SelectedTileIndex, out var tn)
                                ? tn : $"#{_state.SelectedTileIndex}";
                            _statusText = $"Pintando ({tx},{ty}) → tile {_state.SelectedTileIndex} [{tileName}]";
                        }
                    }
                    else
                    {
                        _statusText = $"Cursor fora do mapa ({_state.HoverTileX},{_state.HoverTileY})";
                    }
                    _vpPainting = true;
                }
                else
                {
                    if (_vpPainting && _paintStrokeActive)
                    {
                        _terrain?.MarkDirty();
                        _sceneDirty = true;
                        _state.RebuildMinimap();
                    }
                    _vpPainting = false;
                    _paintStrokeActive = false;
                }
                break;

            case EditorTool.AttributeMap:
                if ((hovered || _vpPainting) && leftDown && _state.IsLoaded && !anyActive && _state.AttributeMap != null)
                {
                    bool shift = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
                    bool ctrl  = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);

                    int tx = _state.HoverTileX, ty = _state.HoverTileY;
                    if (tx >= 0 && ty >= 0)
                    {
                        if (!_attrStrokeActive)
                        {
                            _state.PushUndoAttributeMap();
                            _attrStrokeActive = true;
                        }

                        int br = (int)MathF.Round(_state.AttrBrushRadius);
                        br = Math.Clamp(br, 0, 10);
                        bool shiftHeld = KeyboardState.IsKeyDown(Keys.LeftShift) ||
                                         KeyboardState.IsKeyDown(Keys.RightShift);
                        bool enable = !shiftHeld;
                        int ex = _state.Trn != null ? _state.Trn.EnvPosX : 0;
                        int ey = _state.Trn != null ? _state.Trn.EnvPosY : 0;
                        _state.AttributeMap.SetMaskAtFieldTiles(ex, ey, tx, ty, _state.SelectedAttribute, enable, br);
                        _attrOverlayDirty = true;
                        _statusText = $"AttrMap ({tx},{ty}) {(enable ? "+=" : "-=")} {_state.SelectedAttribute} [{GetAttrName(_state.SelectedAttribute)}]";
                    }
                    else
                    {
                        _statusText = $"Cursor fora do mapa ({_state.HoverTileX},{_state.HoverTileY})";
                    }
                    _vpPainting = true;
                }
                else
                {
                    if (_vpPainting && _attrStrokeActive && _attrOverlayDirty)
                    {
                        RebuildAttrOverlay();
                        _attrOverlayDirty = false;
                    }
                    _vpPainting = false;
                    _attrStrokeActive = false;
                }
                break;

            case EditorTool.Object:
                if (hovered && (leftClicked || _pendingClick) && _state.IsLoaded && !anyActive && _state.Trn != null && _state.Dat != null)
                {
                    int tx = _state.HoverTileX, ty = _state.HoverTileY;
                    if (tx >= 0 && ty >= 0)
                    {
                        _state.PushUndoObjects();
                        float posX = Math.Clamp((tx + 0.5f) * TerrainRenderer.TILE_SIZE, 0f, 127f);
                        float posY = Math.Clamp((ty + 0.5f) * TerrainRenderer.TILE_SIZE, 0f, 127f);
                        float h = _state.Trn.Tiles[tx + ty * 64].Height;
                        var rec = new DatRecord
                        {
                            ObjType = (uint)Math.Max(0, _addObjType),
                            PosX = posX,
                            PosY = posY,
                            Height = h,
                            Angle = 0f,
                            TextureSetIndex = 0,
                            MaskIndex = 0,
                            HasScale = false,
                            ScaleH = 1f,
                            ScaleV = 1f
                        };
                        _state.Dat.Records.Add(rec);
                        int idx = _state.Dat.Records.Count - 1;
                        _state.SelectedObjectIndex = idx;
                        _objRenderer.SelectedObjectIndex = idx;
                        _sceneDirty = true;
                        ShowNotif($"+ Objeto (ObjType {_addObjType}) em ({posX:F0},{posY:F0})", 3.5f);
                        _statusText = $"Objeto adicionado: #{idx} ObjType={_addObjType}";
                    }
                }
                break;

            case EditorTool.Collision:
                // Atalho do AttrMap focado em CantGo (bit 2)
                if (_state.AttributeMap == null)
                {
                    TryAutoLoadAttributeMap();
                    if (_state.AttributeMap == null)
                    {
                        ShowNotif("AttributeMap.dat não encontrado. Configure nas Settings.", 4f);
                        break;
                    }
                }
                if ((hovered || _vpPainting) && leftDown && _state.IsLoaded && !anyActive && _state.AttributeMap != null)
                {
                    bool shiftHeld = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
                    int tx = _state.HoverTileX, ty = _state.HoverTileY;
                    if (tx >= 0 && ty >= 0)
                    {
                        if (!_attrStrokeActive) { _state.PushUndoAttributeMap(); _attrStrokeActive = true; }
                        int ex = _state.Trn != null ? _state.Trn.EnvPosX : 0;
                        int ey = _state.Trn != null ? _state.Trn.EnvPosY : 0;
                        const byte CANT_GO = 2;
                        _state.AttributeMap.SetMaskAtFieldTiles(ex, ey, tx, ty, CANT_GO, !shiftHeld, (int)MathF.Round(_state.AttrBrushRadius));
                        _attrOverlayDirty = true;
                        _statusText = $"Colisão ({tx},{ty}) {(!shiftHeld ? "+=" : "-=")} CantGo | Shift=remover";
                    }
                    _vpPainting = true;
                }
                else
                {
                    if (_vpPainting && _attrStrokeActive && _attrOverlayDirty)
                    {
                        RebuildAttrOverlay();
                        _attrOverlayDirty = false;
                    }
                    _vpPainting = false;
                    _attrStrokeActive = false;
                }
                break;

            case EditorTool.Trigger:
                // Atalho do AttrMap focado em PvP (bit 64) / Teleport (bit 16)
                if (_state.AttributeMap == null)
                {
                    TryAutoLoadAttributeMap();
                    if (_state.AttributeMap == null)
                    {
                        ShowNotif("AttributeMap.dat não encontrado. Configure nas Settings.", 4f);
                        break;
                    }
                }
                if ((hovered || _vpPainting) && leftDown && _state.IsLoaded && !anyActive && _state.AttributeMap != null)
                {
                    bool shiftHeld = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
                    int tx = _state.HoverTileX, ty = _state.HoverTileY;
                    if (tx >= 0 && ty >= 0)
                    {
                        if (!_attrStrokeActive) { _state.PushUndoAttributeMap(); _attrStrokeActive = true; }
                        int ex = _state.Trn != null ? _state.Trn.EnvPosX : 0;
                        int ey = _state.Trn != null ? _state.Trn.EnvPosY : 0;
                        _state.AttributeMap.SetMaskAtFieldTiles(ex, ey, tx, ty, _triggerAttrMask, !shiftHeld, (int)MathF.Round(_state.AttrBrushRadius));
                        _attrOverlayDirty = true;
                        _statusText = $"Trigger ({tx},{ty}) {(!shiftHeld ? "+=" : "-=")} mask={_triggerAttrMask} | Shift=remover";
                    }
                    _vpPainting = true;
                }
                else
                {
                    if (_vpPainting && _attrStrokeActive && _attrOverlayDirty)
                    {
                        RebuildAttrOverlay();
                        _attrOverlayDirty = false;
                    }
                    _vpPainting = false;
                    _attrStrokeActive = false;
                }
                break;

            case EditorTool.Light:
                // Edita a cor (vertex color) do tile sob o cursor
                if ((hovered || _vpPainting) && leftDown && _state.IsLoaded && _state.Trn != null && !anyActive)
                {
                    bool shiftHeld = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
                    int tx = _state.HoverTileX, ty = _state.HoverTileY;
                    if (tx >= 0 && ty >= 0)
                    {
                        if (!_paintStrokeActive)
                        {
                            _state.PushUndoPaint();
                            if (_lightPaintObjects && _state.Dat != null) _state.PushUndoObjects();
                            _paintStrokeActive = true;
                        }
                        int r2 = (int)Math.Ceiling(_state.BrushRadius);
                        for (int dy2 = -r2; dy2 <= r2; dy2++)
                        {
                            for (int dx2 = -r2; dx2 <= r2; dx2++)
                            {
                                if (dx2 * dx2 + dy2 * dy2 > _state.BrushRadius * _state.BrushRadius) continue;
                                int ttx = tx + dx2, tty = ty + dy2;
                                if (ttx < 0 || ttx >= 64 || tty < 0 || tty >= 64) continue;
                                ref var tl = ref _state.Trn.Tiles[ttx + tty * 64];
                                // Shift = resetar para branco; normal = aplicar cor selecionada
                                tl.SetColor(shiftHeld ? 0xFFFFFFu : _lightBrushColor);
                            }
                        }
                        _terrain?.MarkDirty();
                        _sceneDirty = true;

                        // ── Pintar objetos no raio (só se toggle ativo) ───────────
                        if (_lightPaintObjects && _state.Dat != null)
                        {
                            float worldR = _state.BrushRadius * TerrainRenderer.TILE_SIZE;
                            float worldR2 = worldR * worldR;
                            float cx = _hoverLocalX, cy = _hoverLocalY;
                            byte newR = (byte)(_lightBrushColor & 0xFF);
                            byte newG = (byte)((_lightBrushColor >> 8) & 0xFF);
                            byte newB = (byte)((_lightBrushColor >> 16) & 0xFF);
                            for (int oi = 0; oi < _state.Dat.Records.Count; oi++)
                            {
                                var orec = _state.Dat.Records[oi];
                                float odx = orec.PosX - cx, ody = orec.PosY - cy;
                                if (odx * odx + ody * ody <= worldR2)
                                {
                                    if (shiftHeld)
                                    {
                                        orec.HasColorOverride = false;
                                        orec.ColorR = orec.ColorG = orec.ColorB = 255;
                                        _objPartColors.Remove(oi);
                                    }
                                    else if (_lightPaintByPart && _lightPartIndex >= 0)
                                    {
                                        if (!_objPartColors.TryGetValue(oi, out var map))
                                        {
                                            map = new Dictionary<int, uint>();
                                            _objPartColors[oi] = map;
                                        }
                                        map[_lightPartIndex] = _lightBrushColor & 0xFFFFFFu;
                                    }
                                    else
                                    {
                                        orec.HasColorOverride = true;
                                        orec.ColorR = newR;
                                        orec.ColorG = newG;
                                        orec.ColorB = newB;
                                    }
                                    _state.Dat.Records[oi] = orec;
                                }
                            }
                            _objRenderer.MarkColorsDirty();
                        }

                        _statusText = $"Light ({tx},{ty}) cor=#{_lightBrushColor:X6} | Shift=reset";
                    }
                    _vpPainting = true;
                }
                else
                {
                    if (_vpPainting && _paintStrokeActive) _state.RebuildMinimap();
                    _vpPainting = false;
                    _paintStrokeActive = false;
                }
                break;

            case EditorTool.Area:
                // Seleção retangular de objetos por drag
                if (hovered && _state.IsLoaded && _state.Dat != null)
                {
                    if (leftClicked || _pendingClick)
                    {
                        _areaSelStart = mouse;
                        _areaSelDragging = true;
                        // Sem Ctrl: limpa seleção anterior
                        bool ctrlHeld = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);
                        if (!ctrlHeld) _state.SelectedObjectIndex = -1;
                    }
                    if (_areaSelDragging && leftDown)
                    {
                        _areaSelEnd = mouse;
                    }
                    if (_areaSelDragging && !leftDown)
                    {
                        // Finaliza seleção: seleciona TODOS os objetos no retângulo
                        _areaSelDragging = false;
                        var vp2 = _cam.ViewMatrix * _cam.ProjectionMatrix(_vpSize.X / _vpSize.Y);
                        float xMin = Math.Min(_areaSelStart.X, _areaSelEnd.X);
                        float xMax = Math.Max(_areaSelStart.X, _areaSelEnd.X);
                        float yMin = Math.Min(_areaSelStart.Y, _areaSelEnd.Y);
                        float yMax = Math.Max(_areaSelStart.Y, _areaSelEnd.Y);
                        // Só seleciona se o rect for pelo menos 5x5 px (evita clique simples)
                        if (xMax - xMin > 5f || yMax - yMin > 5f)
                        {
                            bool ctrlArea = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);
                            if (!ctrlArea) _objRenderer.AreaSelectedObjects.Clear();

                            int countInRect = 0;
                            int firstInRect = -1;
                            for (int i = 0; i < _state.Dat.Records.Count; i++)
                            {
                                var rec = _state.Dat.Records[i];
                                var world = new OpenTK.Mathematics.Vector3(rec.PosX, rec.Height * ObjectRenderer.HEIGHT_SCALE, rec.PosY);
                                if (WorldToScreen(world, vp2, new Vector2(_vpPos.X, _vpPos.Y), new Vector2(_vpSize.X, _vpSize.Y), out var s))
                                {
                                    if (s.X >= xMin && s.X <= xMax && s.Y >= yMin && s.Y <= yMax)
                                    {
                                        _objRenderer.AreaSelectedObjects.Add(i);
                                        if (firstInRect < 0) firstInRect = i;
                                        countInRect++;
                                    }
                                }
                            }
                            if (firstInRect >= 0)
                            {
                                _state.SelectedObjectIndex = firstInRect;
                                _objRenderer.SelectedObjectIndex = firstInRect;
                                _statusText = $"Área: {countInRect} objeto(s) selecionado(s) | Ctrl=adicionar à seleção";
                            }
                            else
                            {
                                _statusText = "Área: nenhum objeto no retângulo";
                            }
                            _sceneDirty = true;
                        }
                        _areaSelStart = _areaSelEnd = Vector2.Zero;
                    }
                }
                else if (!leftDown)
                {
                    _areaSelDragging = false;
                }
                break;

        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Painel inferior: Tiles / Textures / Objects / Prefabs
    // ────────────────────────────────────────────────────────────────────────

    private void DrawBottomPanel(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, COL_PANEL);
        ImGui.Begin("##bottom", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

        // Abas
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 2));
        TabToggle("Tiles",    BottomTab.Tiles, disabled: false); ImGui.SameLine();
        TabToggle("Texturas", BottomTab.Textures, disabled: false); ImGui.SameLine();
        TabToggle("Objetos",  BottomTab.Objects, disabled: false); ImGui.SameLine();
        TabToggle("Prefabs",  BottomTab.Prefabs, disabled: false);
        ImGui.PopStyleVar();
        ImGui.Separator();


        switch (_bottomTab)
        {
            case BottomTab.Tiles:    DrawTilesGrid(); break;
            case BottomTab.Textures:
                ImGui.TextDisabled("Texturas abre em uma janela grande.");
                if (ImGui.Button("Abrir janela de Texturas##opentexmodal", new Vector2(-1, 0)))
                {
                    _showTexturesModal = true;
                    ImGui.OpenPopup("Texturas##modal");
                }
                break;
            case BottomTab.Objects:  DrawObjectsList(); break;
            case BottomTab.Prefabs:  ImGui.Text("(Prefabs)"); break;
        }

        DrawTexturesModal();

        ImGui.PopStyleColor();
        ImGui.End();
    }

    private void DrawTilesGrid()
    {
        int maxTile = _state.IsLoaded ? Math.Max(256, _state.TileNameById.Keys.DefaultIfEmpty(0).Max() + 1) : 256;
        int listed  = _state.IsLoaded ? _state.TileNameById.Count : 0;
        int texLoaded = _tileCache.LoadedCount;

        ImGui.Text($"Tiles: {listed}   Texturas: {texLoaded}/{maxTile}");
        ImGui.BeginChild("##tilegrid", new Vector2(0, 0), false);

        var avail   = ImGui.GetContentRegionAvail();
        const float CELL = 64f;   // célula maior para acomodar textura
        int cols    = Math.Max(1, (int)(avail.X / CELL));
        var draw    = ImGui.GetWindowDrawList();
        var origin  = ImGui.GetCursorScreenPos();

        for (int id = 0; id < maxTile; id++)
        {
            int col = id % cols;
            int row = id / cols;
            var p   = origin + new Vector2(col * CELL + 2, row * CELL + 2);
            var p2  = p + new Vector2(CELL - 4, CELL - 4);
            float inner = CELL - 4f;

            bool isSelected = (id == _state.SelectedTileIndex);

            if (isSelected)
                draw.AddRectFilled(p - new Vector2(2,2), p2 + new Vector2(2,2), 0xFF2288CC);

            // Tentar usar textura real, senão usar cor
            int glTex = _tileCache.Get(id);
            if (glTex != 0)
            {
                // Renderizar textura OpenGL como imagem ImGui
                ImGui.SetCursorScreenPos(p);
                ImGui.Image(new IntPtr(glTex), new Vector2(inner, inner));
                // Borda de seleção sobre a textura
                if (isSelected)
                    draw.AddRect(p, p2, 0xFF2288CC, 3f, ImDrawFlags.None, 2f);
                else
                    draw.AddRect(p, p2, 0x55FFFFFF, 2f);
            }
            else
            {
                uint bg = TileImguiColor(id);
                draw.AddRectFilled(p, p2, bg, 3f);
            }

            // ID e nome (sempre visíveis sobre a imagem)
            draw.AddText(p + new Vector2(3, 2), 0xFFFFFFFF, id.ToString());
            if (_state.TileNameById.TryGetValue(id, out var nm))
            {
                string shortNm = nm.Length > 9 ? nm[..9] : nm;
                draw.AddText(p + new Vector2(3, inner - 13), 0xCCFFFFFF, shortNm);
            }

            // Hitbox (quando não há textura o InvisibleButton precisa ser posicionado)
            if (glTex == 0)
            {
                ImGui.SetCursorScreenPos(p);
                if (ImGui.InvisibleButton("##tile" + id, new Vector2(inner, inner)))
                {
                    _state.SelectedTileIndex = id;
                    // Ao selecionar um tile, ativa automaticamente a ferramenta Paint
                    if (_state.IsLoaded) _state.ActiveTool = EditorTool.PaintTexture;
                    _statusText = $"Tile {id} selecionado" + (_state.TileNameById.TryGetValue(id, out var n2) ? $" — {n2}" : "");
                }
            }
            else
            {
                // Já renderizado com ImGui.Image — verificar clique
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _state.SelectedTileIndex = id;
                    // Ao selecionar um tile, ativa automaticamente a ferramenta Paint
                    if (_state.IsLoaded) _state.ActiveTool = EditorTool.PaintTexture;
                    _statusText = $"Tile {id} selecionado" + (_state.TileNameById.TryGetValue(id, out var n2) ? $" — {n2}" : "");
                }
            }
        }

        float rowCount = (float)Math.Ceiling(maxTile / (float)cols);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, rowCount * CELL + 4));
        ImGui.Dummy(Vector2.Zero);
        ImGui.EndChild();
    }

    private void DrawTexturesModal()
    {
        if (!_showTexturesModal) return;

        var io = ImGui.GetIO();
        float w = MathF.Min(1280f, io.DisplaySize.X * 0.96f);
        float h = MathF.Min(860f,  io.DisplaySize.Y * 0.92f);
        ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Once);

        bool open = true;
        if (ImGui.BeginPopupModal("Texturas##modal", ref open, ImGuiWindowFlags.NoResize))
        {
            DrawTexturesTab(640f);
            ImGui.Separator();
            if (ImGui.Button("Fechar##texmodalclose", new Vector2(120, 0)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (!open)
            _showTexturesModal = false;
    }

    private void DrawTexturesTab(float maxPreview)
    {
        if (string.IsNullOrWhiteSpace(_state.EnvFolder) || !Directory.Exists(_state.EnvFolder))
        {
            ImGui.TextDisabled("Configure a pasta Env primeiro.");
            if (ImGui.Button("Selecionar pasta do cliente..."))
                PickClientRoot();
            return;
        }

        EnsureTextureScan();
        _texPreview.Configure(_state.EnvFolder);

        ImGui.TextDisabled($"Arquivos encontrados: {_texAll.Count}   Novas: {_texNew.Count}   Registradas: {_state.TileNameById.Count}");

        bool onlyNew = _texOnlyNew;
        if (ImGui.Checkbox("Mostrar apenas novas", ref onlyNew))
            _texOnlyNew = onlyNew;

        ImGui.SameLine();
        if (ImGui.Button("Recarregar"))
            ForceTextureScan();

        ImGui.SameLine();
        if (ImGui.Button("Adicionar arquivo..."))
        {
            if (_pickTextureTask == null)
            {
                _pickTextureTask = Dialogs.PickFileAsync(
                    "Texturas|*.wys;*.png;*.tga;*.jpg;*.jpeg;*.bmp|Todos|*.*",
                    _state.EnvFolder,
                    "Selecionar textura para copiar para o Env");
            }
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("Filtro##texfilter", ref _texFilter, 64);

        var list = _texOnlyNew ? _texNew : _texAll;
        const float BottomControlsH = 116f;
        float topH = ImGui.GetContentRegionAvail().Y - BottomControlsH;
        if (topH < 120f) topH = 120f;

        ImGui.BeginChild("##textop", new Vector2(0, topH), false);
        float wAvail = ImGui.GetContentRegionAvail().X;
        float hAvail = ImGui.GetContentRegionAvail().Y;

        float desiredPrev = maxPreview > 220f ? 420f : 240f;
        float previewW = Math.Clamp(desiredPrev, 220f, Math.Max(220f, wAvail - 220f - 8f));
        float listW = Math.Max(220f, wAvail - previewW - 8f);
        float listH = hAvail;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.07f, 0.09f, 0.15f, 1f));
        ImGui.BeginChild("##texlist", new Vector2(listW, listH), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        for (int i = 0; i < list.Count; i++)
        {
            string nm = list[i];
            if (!string.IsNullOrWhiteSpace(_texFilter) &&
                !nm.Contains(_texFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            string? rp = _texPreview.ResolvePath(nm);
            string ext = rp != null ? Path.GetExtension(rp).ToLowerInvariant() : "";
            string label = string.IsNullOrWhiteSpace(ext) ? nm : (nm + ext);
            bool sel = i == _texSelected;
            if (ImGui.Selectable(label, sel))
            {
                _texSelected = i;
                _texAssignTileId = FindNextFreeTileId();
            }
            if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(rp))
                ImGui.SetTooltip(rp);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.SameLine(0, 8);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.07f, 0.09f, 0.15f, 1f));
        ImGui.BeginChild("##texprev", new Vector2(previewW, listH), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        string? selectedName = (_texSelected >= 0 && _texSelected < list.Count) ? list[_texSelected] : null;

        ImGui.TextColored(new Vector4(0.55f, 0.75f, 1f, 1f), "Prévia");
        ImGui.Separator();
        float pw = ImGui.GetContentRegionAvail().X;
        float img = Math.Clamp(pw - 6f, 160f, maxPreview);

        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            int tex = _texPreview.Get(selectedName);
            string? rp = _texPreview.ResolvePath(selectedName);
            string ext = rp != null ? Path.GetExtension(rp).ToLowerInvariant() : "";

            if (tex != 0)
            {
                float imgX = (pw - img) * 0.5f;
                if (imgX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + imgX);
                ImGui.Image(new IntPtr(tex), new Vector2(img, img));
            }
            else
            {
                uint plCol = 0xFF223344;
                var p0 = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(p0, p0 + new Vector2(img, img), plCol, 6f);
                ImGui.GetWindowDrawList().AddRect(p0, p0 + new Vector2(img, img), 0x55FFFFFF, 6f);
                ImGui.Dummy(new Vector2(img, img));
                ImGui.TextDisabled("Sem prévia");
            }

            ImGui.Spacing();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + pw - 4f);
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 1f, 1f), string.IsNullOrWhiteSpace(ext) ? selectedName : (selectedName + ext));
            ImGui.PopTextWrapPos();
            if (!string.IsNullOrWhiteSpace(rp))
                ImGui.TextDisabled(rp);
            if (_state.TileNameById.ContainsValue(selectedName))
                ImGui.TextDisabled("Status: registrada");
            else
                ImGui.TextDisabled("Status: nova");

            ImGui.Separator();
            ImGui.TextDisabled("Converter para formato do WYD:");
            bool f128 = _texConvertForce128;
            if (ImGui.Checkbox("Forcar 128x128 (recomendado)", ref f128))
                _texConvertForce128 = f128;
            bool ov = _texConvertOverwrite;
            if (ImGui.Checkbox("Sobrescrever .wys se existir", ref ov))
                _texConvertOverwrite = ov;

            if (ImGui.Button("Converter para .wys##convwys", new Vector2(-1, 0)))
            {
                TryConvertSelectedTextureToWys(selectedName);
                ForceTextureScan();
            }
        }
        else
        {
            ImGui.TextDisabled("Selecione uma");
            ImGui.TextDisabled("textura na lista");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.EndChild();

        ImGui.Separator();

        bool canRegister = _state.IsLoaded && selectedName != null;
        if (!canRegister) ImGui.BeginDisabled();
        int tid = _texAssignTileId;
        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("TileID", ref tid, 1, 5);
        tid = Math.Clamp(tid, 0, 255);
        _texAssignTileId = tid;

        ImGui.SameLine();
        if (ImGui.Button("Registrar no TileID"))
        {
            if (selectedName != null)
            {
                if (TryRegisterEnvTexture(_texAssignTileId, selectedName))
                    ShowNotif($"+ Textura registrada: {selectedName} (Tile {_texAssignTileId})", 4f);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Registrar automatico"))
        {
            if (selectedName != null)
            {
                int free = FindNextFreeTileId();
                if (free < 0) _statusText = "Nao ha TileID livre (0..255).";
                else
                {
                    if (TryRegisterEnvTexture(free, selectedName))
                        ShowNotif($"+ Textura registrada: {selectedName} (Tile {free})", 4f);
                }
            }
        }
        if (!canRegister) ImGui.EndDisabled();
    }

    private void TryConvertSelectedTextureToWys(string baseName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_state.EnvFolder) || !Directory.Exists(_state.EnvFolder))
            {
                _statusText = "EnvFolder nao configurado.";
                return;
            }

            var decoded = _texPreview.DecodeRgba(baseName);
            if (decoded == null)
            {
                _statusText = "Nao consegui ler a textura selecionada.";
                return;
            }

            var (rgba, w, h, srcPath) = decoded.Value;
            if (Path.GetExtension(srcPath).Equals(".wys", StringComparison.OrdinalIgnoreCase))
            {
                _statusText = "Ja esta em .wys.";
                return;
            }

            if (_texConvertForce128 && (w != 128 || h != 128))
            {
                rgba = WysLoader.ScaleTo(rgba, w, h, 128, 128);
                w = 128;
                h = 128;
            }

            byte[] wys = WysWriter.EncodeFromRgba(rgba, w, h);

            string dstDir = Path.Combine(_state.EnvFolder, "Texture");
            Directory.CreateDirectory(dstDir);
            string dstPath = Path.Combine(dstDir, baseName + ".wys");
            if (File.Exists(dstPath) && !_texConvertOverwrite)
            {
                _statusText = "Ja existe .wys (marque sobrescrever).";
                return;
            }

            File.WriteAllBytes(dstPath, wys);
            _texPreview.Invalidate(baseName);
            foreach (var kv in _state.TileNameById)
                if (string.Equals(kv.Value, baseName, StringComparison.OrdinalIgnoreCase))
                    _tileCache.Invalidate(kv.Key);
            _statusText = $"Convertido: {Path.GetFileName(srcPath)} -> {baseName}.wys";
            ShowNotif($"+ .wys gerado: {baseName}.wys", 4f);
        }
        catch (Exception ex)
        {
            _statusText = "Erro ao converter: " + ex.Message;
        }
    }

    private void DrawDeleteMapModal()
    {
        if (!_showDeleteMapModal) return;

        ImGui.SetNextWindowSize(new Vector2(560, 300), ImGuiCond.Once);
        bool open = true;
        if (ImGui.BeginPopupModal("Excluir mapa##modal", ref open, ImGuiWindowFlags.NoResize))
        {
            string env = _state.EnvFolder;
            string f = _deleteMapField;
            string trnPath = string.IsNullOrWhiteSpace(env) ? "" : Path.Combine(env, f + ".trn");
            string datPath = string.IsNullOrWhiteSpace(env) ? "" : Path.Combine(env, f + ".dat");
            bool trnExists = !string.IsNullOrWhiteSpace(trnPath) && File.Exists(trnPath);
            bool datExists = !string.IsNullOrWhiteSpace(datPath) && File.Exists(datPath);

            byte ex = 0, ey = 0;
            if (trnExists)
            {
                try
                {
                    var trn = TrnFile.Load(trnPath);
                    ex = trn.EnvPosX;
                    ey = trn.EnvPosY;
                }
                catch { }
            }
            else if (ParseFieldCoords(f, out int rr, out int cc))
            {
                ex = (byte)Math.Clamp(rr, 0, 255);
                ey = (byte)Math.Clamp(cc, 0, 255);
            }

            string? root = null;
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            {
                var dirName = new DirectoryInfo(env).Name;
                if (dirName.Equals("Env", StringComparison.OrdinalIgnoreCase))
                    root = Directory.GetParent(env)?.FullName;
            }
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                root = _clientRoot;

            string mmPath = (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                ? Path.Combine(root, "UI", $"m{ex:00}{ey:00}.wyt")
                : "";
            bool mmExists = !string.IsNullOrWhiteSpace(mmPath) && File.Exists(mmPath);

            ImGui.TextColored(new Vector4(1f, 0.55f, 0.55f, 1f), "Atencao: isso apaga arquivos do cliente.");
            ImGui.TextDisabled("Mapa:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), f);
            ImGui.Separator();

            ImGui.TextDisabled($"TRN: {(trnExists ? "OK" : "nao encontrado")}  ({trnPath})");
            ImGui.TextDisabled($"DAT: {(datExists ? "OK" : "nao encontrado")}  ({datPath})");
            if (!string.IsNullOrWhiteSpace(mmPath))
                ImGui.TextDisabled($"Minimap: {(mmExists ? "OK" : "nao encontrado")}  ({mmPath})");
            ImGui.TextDisabled($"EnvPos: X={ex}  Y={ey}");
            ImGui.TextDisabled($"Coords centro: X={ex * 128 + 64}  Y={ey * 128 + 64}");

            bool delMm = _deleteMapAlsoMinimap;
            if (ImGui.Checkbox("Apagar minimapa (UI\\mXXYY.wyt)", ref delMm))
                _deleteMapAlsoMinimap = delMm;

            ImGui.Spacing();
            ImGui.TextDisabled($"Digite '{f}' para confirmar:");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##delconfirm", ref _deleteMapConfirm, 32);

            bool canDelete = string.Equals(_deleteMapConfirm.Trim(), f, StringComparison.OrdinalIgnoreCase);
            if (!canDelete) ImGui.BeginDisabled();
            if (ImGui.Button("Apagar agora##delmapok", new Vector2(140, 0)))
            {
                if (TryDeleteMapFiles(f, _deleteMapAlsoMinimap))
                {
                    _showDeleteMapModal = false;
                    ImGui.CloseCurrentPopup();
                }
            }
            if (!canDelete) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancelar##delmapcancel", new Vector2(140, 0)))
            {
                _showDeleteMapModal = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (!open)
            _showDeleteMapModal = false;
    }

    private bool TryDeleteMapFiles(string fieldName, bool alsoMinimap)
    {
        try
        {
            string env = _state.EnvFolder;
            if (string.IsNullOrWhiteSpace(env) || !Directory.Exists(env))
            {
                _statusText = "EnvFolder nao configurado.";
                return false;
            }

            string trnPath = Path.Combine(env, fieldName + ".trn");
            string datPath = Path.Combine(env, fieldName + ".dat");

            byte ex = 0, ey = 0;
            if (File.Exists(trnPath))
            {
                try
                {
                    var trn = TrnFile.Load(trnPath);
                    ex = trn.EnvPosX;
                    ey = trn.EnvPosY;
                }
                catch { }
            }
            else if (ParseFieldCoords(fieldName, out int rr, out int cc))
            {
                ex = (byte)Math.Clamp(rr, 0, 255);
                ey = (byte)Math.Clamp(cc, 0, 255);
            }

            if (alsoMinimap)
            {
                string? root = null;
                var dirName = new DirectoryInfo(env).Name;
                if (dirName.Equals("Env", StringComparison.OrdinalIgnoreCase))
                    root = Directory.GetParent(env)?.FullName;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    root = _clientRoot;
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    string mm = Path.Combine(root, "UI", $"m{ex:00}{ey:00}.wyt");
                    if (File.Exists(mm)) File.Delete(mm);
                }
            }

            if (File.Exists(trnPath)) File.Delete(trnPath);
            if (File.Exists(datPath)) File.Delete(datPath);

            string loaded = Path.GetFileNameWithoutExtension(_state.TrnPath);
            if (!string.IsNullOrWhiteSpace(loaded) &&
                string.Equals(loaded, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                _state.Unload();
                _terrain?.MarkDirty();
            }

            for (int i = _openTabs.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_openTabs[i], fieldName, StringComparison.OrdinalIgnoreCase))
                    _openTabs.RemoveAt(i);
            }
            if (_activeTab >= _openTabs.Count) _activeTab = Math.Max(0, _openTabs.Count - 1);

            _availableFields = EditorState.ScanFields(env);
            if (string.Equals(_selectedField, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                if (_availableFields.Length > 0) SelectField(_availableFields[0]);
                else _selectedField = "";
            }

            _statusText = $"Mapa apagado: {fieldName}";
            ShowNotif(_statusText, 5f);
            return true;
        }
        catch (Exception ex)
        {
            _statusText = "Erro ao apagar mapa: " + ex.Message;
            return false;
        }
    }

    private void EnsureTextureScan()
    {
        if (!string.Equals(_texScanEnv, _state.EnvFolder, StringComparison.OrdinalIgnoreCase))
            ForceTextureScan();
    }

    private void ForceTextureScan()
    {
        _texScanEnv = _state.EnvFolder;
        _texAll.Clear();
        _texNew.Clear();

        if (string.IsNullOrWhiteSpace(_state.EnvFolder) || !Directory.Exists(_state.EnvFolder))
            return;

        string env = _state.EnvFolder;
        var dirs = new[]
        {
            env,
            Path.Combine(env, "Tile"),
            Path.Combine(env, "tile"),
            Path.Combine(env, "Texture"),
            Path.Combine(env, "texture"),
        };

        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".wys", ".png", ".tga", ".jpg", ".jpeg", ".bmp" };

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs)
        {
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.EnumerateFiles(d))
            {
                var ext = Path.GetExtension(f);
                if (!exts.Contains(ext)) continue;
                var nm = Path.GetFileNameWithoutExtension(f);
                if (!string.IsNullOrWhiteSpace(nm))
                    set.Add(nm);
            }
        }

        _texAll.AddRange(set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

        var registered = new HashSet<string>(_state.TileNameById.Values, StringComparer.OrdinalIgnoreCase);
        foreach (var nm in _texAll)
        {
            if (!registered.Contains(nm))
                _texNew.Add(nm);
        }

        if (_texOnlyNew)
            _texSelected = Math.Min(_texSelected, _texNew.Count - 1);
        else
            _texSelected = Math.Min(_texSelected, _texAll.Count - 1);
    }

    private int FindNextFreeTileId()
    {
        for (int i = 0; i <= 255; i++)
        {
            if (!_state.TileNameById.ContainsKey(i))
                return i;
        }
        return -1;
    }

    private bool TryRegisterEnvTexture(int tileId, string baseName)
    {
        if (tileId < 0 || tileId > 255)
        {
            _statusText = "TileID deve ser 0..255.";
            return false;
        }
        if (!_state.IsLoaded)
        {
            _statusText = "Carregue um mapa antes de registrar.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(_state.EnvTextureListPath) || !File.Exists(_state.EnvTextureListPath))
        {
            _statusText = "EnvTextureList3 nao configurado/encontrado.";
            return false;
        }

        try
        {
            string ext = Path.GetExtension(_state.EnvTextureListPath).ToLowerInvariant();
            if (ext == ".txt")
            {
                UpsertEnvTextureListTxt(_state.EnvTextureListPath, tileId, baseName);
            }
            else if (ext == ".bin")
            {
                var entries = EnvTextureListBin.Load(_state.EnvTextureListPath);
                int idx = tileId + 10;
                if (idx >= 0 && idx < entries.Length)
                    entries[idx] = new EnvTextureEntry(baseName, 0, 0, 0);
                EnvTextureListBin.Save(_state.EnvTextureListPath, entries);
            }
            else
            {
                _statusText = "Formato nao suportado para EnvTextureList (use .txt ou .bin).";
                return false;
            }

            _state.TileNameById[tileId] = baseName;
            _tileCache.Invalidate(tileId);
            _tileCache.Configure(_state.EnvFolder, _state.TileNameById);
            _terrain?.MarkTextureDirty();
            ForceTextureScan();
            _statusText = $"Textura registrada: Tile {tileId} = {baseName}";
            return true;
        }
        catch (Exception ex)
        {
            _statusText = "Erro ao registrar: " + ex.Message;
            return false;
        }
    }

    private static void UpsertEnvTextureListTxt(string path, int tileId, string baseName)
    {
        var keep = new List<string>();
        var map = new Dictionary<int, string>();

        if (File.Exists(path))
        {
            foreach (var ln in File.ReadAllLines(path))
            {
                var t = ln.Trim();
                if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("//"))
                {
                    keep.Add(ln);
                    continue;
                }
                var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[0], out int id))
                {
                    keep.Add(ln);
                    continue;
                }
                map[id] = parts[1];
            }
        }

        map[tileId] = baseName;

        var outLines = new List<string>(keep.Count + map.Count);
        outLines.AddRange(keep);
        foreach (var kv in map.OrderBy(k => k.Key))
            outLines.Add($"{kv.Key} {kv.Value}");

        File.WriteAllLines(path, outLines);
    }

    private void DrawObjectsList()
    {
        if (_state.Dat == null) { ImGui.TextDisabled("Sem objetos carregados."); return; }

        int   selIdx = _state.SelectedObjectIndex;
        float avail  = ImGui.GetContentRegionAvail().X;

        // ── Linha de stats + botão Adicionar ─────────────────────────────
        ImGui.TextDisabled($"Total: {_state.Dat.Records.Count}");
        if (selIdx >= 0 && selIdx < _state.Dat.Records.Count)
        {
            var sr = _state.Dat.Records[selIdx];
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f),
                $"  Sel: #{selIdx}  {(_state.MeshNameById.TryGetValue((int)sr.ObjType, out var sn) ? TextLists.GetMeshDisplayName(sn) : $"ObjType {sr.ObjType}")}");
        }

        // Botão que abre o popup modal
        bool canAdd = _state.IsLoaded;
        ImGui.SameLine(avail - 102f);
        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.Button("+ Adicionar Objeto", new Vector2(102, 0)))
        {
            ImGui.OpenPopup("Adicionar Objeto##modal");
        }
        if (!canAdd) ImGui.EndDisabled();

        // ── Popup modal de adição ─────────────────────────────────────────
        ImGui.SetNextWindowSize(new Vector2(720, 560), ImGuiCond.Once);
        bool modalOpen = true;
        if (ImGui.BeginPopupModal("Adicionar Objeto##modal", ref modalOpen,
            ImGuiWindowFlags.NoResize))
        {
            DrawAddObjectModal();
            ImGui.EndPopup();
        }

        ImGui.Separator();

        // ── Campo de busca da lista ───────────────────────────────────────
        ImGui.SetNextItemWidth(avail - 4f);
        ImGui.InputTextWithHint("##objlistfilt", "Buscar por nome ou ObjType...",
            ref _objListFilter, 64);

        // ── Lista de objetos ──────────────────────────────────────────────
        ImGui.BeginChild("##objlist", Vector2.Zero, false);
        int shown = 0;
        for (int i = 0; i < _state.Dat.Records.Count; i++)
        {
            var    r       = _state.Dat.Records[i];
            bool   hasName = _state.MeshNameById.TryGetValue((int)r.ObjType, out var rname);
            string disp    = hasName ? TextLists.GetMeshDisplayName(rname!) : $"ObjType {r.ObjType}";

            if (!string.IsNullOrEmpty(_objListFilter) &&
                !disp.Contains(_objListFilter, StringComparison.OrdinalIgnoreCase) &&
                !r.ObjType.ToString().Contains(_objListFilter))
                continue;

            if (shown >= 800) { ImGui.TextDisabled("...use o filtro para ver mais"); break; }
            shown++;

            bool isSel = (i == selIdx);

            // Badge colorido + nome + posição em uma linha
            // Usa PushStyleColor para o Selectable selecionado
            if (isSel)
            {
                ImGui.PushStyleColor(ImGuiCol.Header,        new Vector4(0.10f, 0.30f, 0.65f, 1f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.12f, 0.38f, 0.78f, 1f));
            }

            // Quadrado colorido como badge (via Image + DrawList após o item)
            var  p        = ImGui.GetCursorScreenPos();
            var  badgeSz  = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
            uint badgeCol = ObjTypeColor(r.ObjType, isSel ? 1f : 0.70f);
            ImGui.GetWindowDrawList().AddRectFilled(p + new Vector2(2,2),
                p + badgeSz - new Vector2(2,2), badgeCol, 3f);
            ImGui.Dummy(badgeSz);   // reserva espaço do badge
            ImGui.SameLine(0, 4);

            string label = $"{disp}  #{i}  ({r.PosX:F0},{r.PosY:F0})##objsel{i}";
            if (ImGui.Selectable(label, isSel, ImGuiSelectableFlags.None,
                new Vector2(0, ImGui.GetFrameHeight())))
            {
                _state.SelectedObjectIndex = i;
                if (_state.ActiveTool != EditorTool.Select && _state.ActiveTool != EditorTool.Move)
                    _state.ActiveTool = EditorTool.Move;
            }
            if (isSel) ImGui.PopStyleColor(2);
        }
        ImGui.EndChild();
    }

    /// <summary>Conteúdo do popup modal "Adicionar Objeto".</summary>
    private void DrawAddObjectModal()
    {
        // ★ Esta linha aparece no console enquanto o modal estiver ABERTO (uma vez por frame)

        float totalW = ImGui.GetContentRegionAvail().X;

        // ── Cabeçalho ─────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(0.4f, 1f, 0.5f, 1f), "Selecione um objeto para adicionar ao mapa:");
        ImGui.Separator();

        // Campo de busca (filtro de nome)
        bool busyConverting = _conversionTask != null && !_conversionTask.IsCompleted;
        ImGui.SetNextItemWidth(totalW);
        ImGui.InputTextWithHint("##catfilt", "Buscar por nome ou id...", ref _objFilter, 64);

        // Status de conversão em andamento (a conversão é disparada pelo botão da toolbar)
        if (busyConverting)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), $"Convertendo: {_convStatus}");
        }

        // ── Filtro de categoria ───────────────────────────────────────────
        ImGui.Text("Categoria:");
        ImGui.SameLine(0, 8);
        float catBtnW = (totalW - 80f) / 4f;
        string[] catLabels = { "Todos", "Arvores", "Edificios", "Custom" };
        for (int ci = 0; ci < catLabels.Length; ci++)
        {
            bool isActive = (_objCategory == ci);
            if (isActive) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.05f, 0.30f, 0.60f, 1f));
            if (ImGui.Button(catLabels[ci] + $"##cat{ci}", new Vector2(catBtnW, 0)))
                _objCategory = ci;
            if (isActive) ImGui.PopStyleColor();
            if (ci < catLabels.Length - 1) ImGui.SameLine(0, 4);
        }

        ImGui.Separator();

        // ── Corpo: lista (esquerda) + prévia (direita) ────────────────────
        const float previewPanelW = 220f;
        float listW = totalW - previewPanelW - 8f;
        float bodyH = 295f;

        // ── Painel esquerdo: lista de meshes ──────────────────────────────
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.06f, 0.08f, 0.16f, 1f));
        ImGui.BeginChild("##catlist", new Vector2(listW, bodyH), true);

        var filtered = _state.MeshNameById
            .Where(kv =>
            {
                // Filtro de texto
                string disp = TextLists.GetMeshDisplayName(kv.Value);
                bool textOk = string.IsNullOrEmpty(_objFilter) ||
                              disp.Contains(_objFilter, StringComparison.OrdinalIgnoreCase) ||
                              kv.Key.ToString().Contains(_objFilter) ||
                              kv.Value.Contains(_objFilter, StringComparison.OrdinalIgnoreCase);
                if (!textOk) return false;

                // Filtro de categoria
                return _objCategory switch
                {
                    1 => IsCategoryTree(kv.Key, kv.Value),
                    2 => IsCategoryBuilding(kv.Key, kv.Value),
                    3 => kv.Value.Contains("CustomMeshes", StringComparison.OrdinalIgnoreCase) ||
                         kv.Value.Contains("\\custom\\", StringComparison.OrdinalIgnoreCase) ||
                         kv.Value.Contains("/custom/", StringComparison.OrdinalIgnoreCase),
                    _ => true,             // Todos
                };
            })
            .OrderBy(kv => TextLists.GetMeshDisplayName(kv.Value))
            .ToList();

        var  drawList = ImGui.GetWindowDrawList();
        float rowH    = ImGui.GetFrameHeight() + 2f;
        float rowW    = ImGui.GetContentRegionAvail().X;

        foreach (var kv in filtered)
        {
            string meshDisp = TextLists.GetMeshDisplayName(kv.Value);
            bool   isChosen = (_addObjType == kv.Key);
            uint   bc       = ObjTypeColor((uint)kv.Key, isChosen ? 1f : 0.65f);

            // InvisibleButton cria a área interativa — funciona garantido no OpenTK+ImGui.NET
            bool clicked = ImGui.InvisibleButton($"##row{kv.Key}", new Vector2(rowW, rowH));
            var  rMin    = ImGui.GetItemRectMin();
            var  rMax    = ImGui.GetItemRectMax();
            bool hovered = ImGui.IsItemHovered();

            // Fundo da linha
            if (isChosen)
                drawList.AddRectFilled(rMin, rMax, 0xCC2A5C8A);
            else if (hovered)
                drawList.AddRectFilled(rMin, rMax, 0x22FFFFFF);

            // Badge colorido à esquerda
            float badgeSz = rowH - 4f;
            drawList.AddRectFilled(
                rMin + new Vector2(2, 2),
                rMin + new Vector2(badgeSz + 2f, badgeSz + 2f),
                bc, 3f);

            // Texto do mesh
            float textY = rMin.Y + (rowH - ImGui.GetFontSize()) * 0.5f;
            drawList.AddText(new Vector2(rMin.X + badgeSz + 8f, textY),
                isChosen ? 0xFFFFFFFF : 0xCCFFFFFF, meshDisp);

            // ID alinhado à direita
            string idStr = $"id {kv.Key}";
            float  idW   = ImGui.CalcTextSize(idStr).X;
            drawList.AddText(new Vector2(rMax.X - idW - 4f, textY), 0xFF888888, idStr);

            if (clicked)
            {
                _addObjType = kv.Key;
            }

            // Duplo clique = seleciona + adiciona imediatamente
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _addObjType = kv.Key;
                AddObjectAtCamera();
                ImGui.CloseCurrentPopup();
                break;
            }

            if (hovered)
                ImGui.SetTooltip($"Arquivo: {kv.Value}\nObjType: {kv.Key}\nDuplo clique para adicionar direto");
        }

        if (filtered.Count == 0)
            ImGui.TextDisabled("Nenhum mesh encontrado.");

        ImGui.EndChild();
        ImGui.PopStyleColor();

        // ── Painel direito: prévia 3D ──────────────────────────────────────
        ImGui.SameLine(0, 8);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.07f, 0.09f, 0.15f, 1f));
        ImGui.BeginChild("##preview", new Vector2(previewPanelW, bodyH), true);

        float pw = ImGui.GetContentRegionAvail().X;

        ImGui.TextColored(new Vector4(0.55f, 0.75f, 1f, 1f), "Prévia 3D");
        ImGui.Separator();

        float imgSize = pw - 4f;          // fill the panel width
        if (imgSize < 80f) imgSize = 80f;
        if (imgSize > 200f) imgSize = 200f;

        if (_addObjType > 0)
        {
            // Render (or return cached) thumbnail
            int thumbTex = _objRenderer.RenderThumbnail(_addObjType, (int)imgSize);

            if (thumbTex != 0)
            {
                // Center the image horizontally
                float imgX = (pw - imgSize) * 0.5f;
                if (imgX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + imgX);
                ImGui.Image(new IntPtr(thumbTex), new Vector2(imgSize, imgSize));

                // Object info below image
                ImGui.Spacing();
                if (_state.MeshNameById.TryGetValue(_addObjType, out var pMesh))
                {
                    string dispName = TextLists.GetMeshDisplayName(pMesh);
                    // Wrap long names
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + pw - 4f);
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 1f, 1f), dispName);
                    ImGui.PopTextWrapPos();
                    ImGui.TextDisabled(pMesh);
                }
                else
                {
                    ImGui.TextDisabled($"ObjType {_addObjType}");
                    ImGui.TextDisabled("(mesh não mapeada)");
                }
                ImGui.Spacing();
                ImGui.TextDisabled($"ID: {_addObjType}");
            }
            else
            {
                // Mesh not loadable — show placeholder with type color
                uint plCol = ObjTypeColor((uint)_addObjType, 0.5f);
                var  plPos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(plPos, plPos + new Vector2(imgSize, imgSize), plCol, 6f);
                ImGui.GetWindowDrawList().AddRect(plPos, plPos + new Vector2(imgSize, imgSize),
                    0xFFFFFFFF & 0x40FFFFFF, 6f);
                ImGui.Dummy(new Vector2(imgSize, imgSize));

                ImGui.Spacing();
                ImGui.TextDisabled("Sem prévia");
                if (_state.MeshNameById.TryGetValue(_addObjType, out var nm))
                    ImGui.TextDisabled(TextLists.GetMeshDisplayName(nm));
                else
                    ImGui.TextDisabled($"ObjType {_addObjType}");
            }
        }
        else
        {
            // Nothing selected yet
            ImGui.Spacing();
            ImGui.TextDisabled("Selecione um");
            ImGui.TextDisabled("objeto na lista");
            ImGui.Spacing();
            ImGui.TextDisabled("ou dê duplo");
            ImGui.TextDisabled("clique para");
            ImGui.TextDisabled("adicionar.");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();

        // ── Entrada manual por ObjType numérico ───────────────────────────
        ImGui.Separator();
        ImGui.Text("ObjType:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("##manualtype", ref _addObjType, 0, 0);
        _addObjType = Math.Clamp(_addObjType, 0, 99999);
        ImGui.SameLine(0, 12);
        if (_state.MeshNameById.TryGetValue(_addObjType, out var prevMesh))
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.5f, 1f), TextLists.GetMeshDisplayName(prevMesh));
        else
            ImGui.TextDisabled("(mesh não mapeada)");

        // ── Botões de ação ─────────────────────────────────────────────────
        ImGui.Separator();
        float bw = (totalW - 8f) * 0.5f;

        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.00f, 0.40f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.00f, 0.55f, 0.28f, 1f));
        if (ImGui.Button("Adicionar ao Mapa##doadd", new Vector2(bw, 0)))
        {
            AddObjectAtCamera();
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleColor(2);

        ImGui.SameLine(0, 8);
        if (ImGui.Button("Cancelar##closemodal", new Vector2(bw, 0)))
            ImGui.CloseCurrentPopup();

        if (_addObjType > 0 && _state.MeshNameById.TryGetValue(_addObjType, out var delPath))
        {
            bool isCustom = delPath.Contains("CustomMeshes", StringComparison.OrdinalIgnoreCase);
            if (isCustom)
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.05f, 0.08f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.75f, 0.10f, 0.12f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.45f, 0.05f, 0.07f, 1f));
                if (ImGui.Button("Excluir Arquivos do Mesh##delmesh", new Vector2(-1, 0)))
                {
                    _pendingDeleteMeshType = _addObjType;
                    _pendingDeleteMeshRemoveInstances = true;
                    ImGui.OpenPopup("Excluir Mesh##confirmdelmesh");
                }
                ImGui.PopStyleColor(3);
            }
        }

        bool open = true;
        if (ImGui.BeginPopupModal("Excluir Mesh##confirmdelmesh", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            int t = _pendingDeleteMeshType;
            string disp = (t > 0 && _state.MeshNameById.TryGetValue(t, out var p))
                ? TextLists.GetMeshDisplayName(p) : $"ObjType {t}";

            int instCount = 0;
            if (t > 0 && _state.Dat != null)
            {
                for (int i = 0; i < _state.Dat.Records.Count; i++)
                    if ((int)_state.Dat.Records[i].ObjType == t) instCount++;
            }

            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Excluir este mesh e arquivos?");
            ImGui.Separator();
            ImGui.Text($"{disp}  (ObjType {t})");
            ImGui.TextDisabled($"Instâncias no mapa atual: {instCount}");
            ImGui.Spacing();

            ImGui.Checkbox("Remover instâncias do mapa atual", ref _pendingDeleteMeshRemoveInstances);

            ImGui.Separator();
            float w = 140f;
            if (ImGui.Button("Excluir##confirmdel", new Vector2(w, 0)))
            {
                DeleteMeshAsset(t, _pendingDeleteMeshRemoveInstances);
                _pendingDeleteMeshType = -1;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancelar##canceldel", new Vector2(w, 0)))
            {
                _pendingDeleteMeshType = -1;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DeleteMeshAsset(int objType, bool removeInstancesFromCurrentMap)
    {
        if (objType <= 0) return;
        if (string.IsNullOrWhiteSpace(_clientRoot) || !Directory.Exists(_clientRoot)) return;
        if (!_state.MeshNameById.TryGetValue(objType, out var relPath)) return;

        string absMsa = relPath;
        if (!Path.IsPathRooted(absMsa))
            absMsa = Path.GetFullPath(Path.Combine(_clientRoot, relPath));
        if (!File.Exists(absMsa))
        {
            string alt = Path.GetFullPath(Path.Combine(_clientRoot, "Mesh", Path.GetFileName(relPath)));
            if (File.Exists(alt)) absMsa = alt;
        }

        var toDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(absMsa)) toDelete.Add(absMsa);

        string meshDir = Path.GetDirectoryName(absMsa) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(absMsa);
        string basePrefix = baseName;
        if (basePrefix.Length > 7) basePrefix = basePrefix[..7];
        string altCustom1 = Path.Combine(_clientRoot, "Mesh", "CustomMeshes");
        string altCustom2 = Path.Combine(_clientRoot, "mesh", "CustomMeshes");

        if (File.Exists(absMsa))
        {
            var mesh = MshLoader.Load(absMsa);
            if (mesh != null)
            {
                foreach (var tn in mesh.TextureNames)
                {
                    string n = Path.GetFileNameWithoutExtension(tn ?? "");
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    foreach (var ext in new[] { ".wys", ".png", ".jpg", ".jpeg", ".tga", ".bmp" })
                    {
                        string p1 = Path.Combine(meshDir, n + ext);
                        if (File.Exists(p1)) toDelete.Add(p1);
                        string p2 = Path.Combine(_clientRoot, "mesh", n + ext);
                        if (File.Exists(p2)) toDelete.Add(p2);
                        string p3 = Path.Combine(_clientRoot, "Mesh", n + ext);
                        if (File.Exists(p3)) toDelete.Add(p3);
                    }
                }
            }
        }

        foreach (var ext in new[] { ".wys", ".png", ".jpg", ".jpeg", ".tga", ".bmp" })
        {
            string p = Path.Combine(meshDir, baseName + ext);
            if (File.Exists(p)) toDelete.Add(p);
        }

        void AddPatternDeletes(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, baseName + "_tex*"))
                toDelete.Add(f);
            foreach (var f in Directory.GetFiles(dir, basePrefix + "_*"))
                toDelete.Add(f);
        }

        AddPatternDeletes(meshDir);
        if (!string.Equals(meshDir, altCustom1, StringComparison.OrdinalIgnoreCase)) AddPatternDeletes(altCustom1);
        if (!string.Equals(meshDir, altCustom2, StringComparison.OrdinalIgnoreCase)) AddPatternDeletes(altCustom2);

        if (removeInstancesFromCurrentMap && _state.Dat != null)
        {
            _state.PushUndoObjects();
            _state.Dat.Records.RemoveAll(r => (int)r.ObjType == objType);
            _state.SelectedObjectIndex = Math.Min(_state.SelectedObjectIndex, _state.Dat.Records.Count - 1);
            _objRenderer.SelectedObjectIndex = _state.SelectedObjectIndex;
        }

        _state.MeshNameById.Remove(objType);

        if (!string.IsNullOrWhiteSpace(_state.MeshListPath) && File.Exists(_state.MeshListPath))
            MeshConverter.RemoveObjTypeFromMeshList(_state.MeshListPath, objType);

        string meshMeshList = Path.Combine(_clientRoot, "Mesh", "MeshList.txt");
        if (File.Exists(meshMeshList) && !string.Equals(meshMeshList, _state.MeshListPath, StringComparison.OrdinalIgnoreCase))
            MeshConverter.RemoveObjTypeFromMeshList(meshMeshList, objType);

        string uiMeshList = Path.Combine(_clientRoot, "UI", "MeshList.txt");
        if (File.Exists(uiMeshList))
            MeshConverter.RemoveObjTypeFromMeshList(uiMeshList, objType);

        string meshMeshList2 = Path.Combine(_clientRoot, "Mesh", "Mesh_MeshList.txt");
        if (File.Exists(meshMeshList2))
            MeshConverter.RemoveObjTypeFromMeshList(meshMeshList2, objType);

        foreach (var f in toDelete)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        if (_state.Dat != null)
        {
            string gameFolder = !string.IsNullOrWhiteSpace(_clientRoot)
                ? _clientRoot
                : (Directory.GetParent(_state.EnvFolder)?.FullName ?? _state.EnvFolder);
            _objRenderer.Clear();
            _objRenderer.Configure(gameFolder, _state.MeshNameById, _state.Dat.Records, _objPartColors);
        }

        _statusText = $"Excluído: ObjType {objType}";
    }

    /// <summary>Lança a conversão assíncrona de um arquivo 3D externo para .msa.</summary>
    private void StartConversion(string filePath)
    {
        _convDone   = false;
        _convStatus = "Iniciando...";
        _convProgress = new Progress<string>(msg => _convStatus = msg);

        // gameFolder = pasta raiz do cliente (pai de Env), para que o relPath
        // seja calculado em relação ao mesmo base que o ObjectRenderer usa.
        string convGameFolder = !string.IsNullOrWhiteSpace(_clientRoot)
            ? _clientRoot
            : (Directory.GetParent(_state.EnvFolder ?? "")?.FullName ?? "");

        // ── Forçar reconversão: deletar .msa antigo ─────────
        string meshBaseName = MeshConverter.SanitizeNamePublic(
            Path.GetFileNameWithoutExtension(filePath));
        string sourceDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "";
        string oldMsa1   = Path.Combine(sourceDir, meshBaseName + ".msa");
        if (File.Exists(oldMsa1))
        {
            try { File.Delete(oldMsa1); } catch { }
        }
        if (!string.IsNullOrEmpty(convGameFolder))
        {
            string[] preferredDirs =
            {
                Path.Combine(convGameFolder, "mesh", "CustomMeshes"),
                Path.Combine(convGameFolder, "Mesh", "CustomMeshes"),
                Path.Combine(convGameFolder, "Env", "Mesh", "CustomMeshes"),
            };
            foreach (var preferredDir in preferredDirs)
            {
                string oldMsa2 = Path.Combine(preferredDir, meshBaseName + ".msa");
                if (File.Exists(oldMsa2))
                {
                    try { File.Delete(oldMsa2); } catch { }
                }

                if (Directory.Exists(preferredDir))
                {
                    string prefix = meshBaseName;
                    if (prefix.Length > 7) prefix = prefix[..7];
                    foreach (var ext in new[] { ".wys", ".png", ".jpg", ".jpeg", ".tga", ".bmp",
                                                ".WYS", ".PNG", ".JPG", ".JPEG", ".TGA", ".BMP" })
                    {
                        string p0 = Path.Combine(preferredDir, meshBaseName + ext);
                        if (File.Exists(p0)) { try { File.Delete(p0); } catch { } }
                    }
                    try
                    {
                        foreach (var f in Directory.GetFiles(preferredDir, prefix + "_*"))
                            try { File.Delete(f); } catch { }
                    }
                    catch { }
                    try
                    {
                        foreach (var f in Directory.GetFiles(preferredDir, meshBaseName + "_tex*"))
                            try { File.Delete(f); } catch { }
                    }
                    catch { }
                }
            }
        }

        _conversionTask = MeshConverter.ConvertAsync(
            filePath,
            convGameFolder,
            _state.MeshNameById,
            _convProgress);
    }

    private static string StageManualFiles(string[] pickedFiles)
    {
        try
        {
            if (pickedFiles == null || pickedFiles.Length == 0) return "";

            string modelFile = "";
            foreach (var f in pickedFiles)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".glb" or ".gltf" or ".fbx" or ".obj" or ".dae" or ".3ds")
                {
                    modelFile = f;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(modelFile) || !File.Exists(modelFile)) return "";

            string modelDir = Path.GetDirectoryName(Path.GetFullPath(modelFile)) ?? "";
            string baseName = MeshConverter.SanitizeNamePublic(Path.GetFileNameWithoutExtension(modelFile));
            string stageDir = Path.Combine(Path.GetTempPath(), "WydMapEditor", "ManualConvert", baseName + "_" + DateTime.UtcNow.Ticks);
            Directory.CreateDirectory(stageDir);

            foreach (var src in pickedFiles)
            {
                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) continue;

                string rel = Path.GetFileName(src);
                try
                {
                    if (!string.IsNullOrEmpty(modelDir))
                    {
                        string rel2 = Path.GetRelativePath(modelDir, Path.GetFullPath(src));
                        if (!rel2.StartsWith(".."))
                            rel = rel2;
                    }
                }
                catch { }

                string dst = Path.Combine(stageDir, rel);
                string? dstDir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dstDir))
                    Directory.CreateDirectory(dstDir);
                try { File.Copy(src, dst, overwrite: true); } catch { }
            }

            string stagedModel = Path.Combine(stageDir, Path.GetFileName(modelFile));
            return File.Exists(stagedModel) ? stagedModel : "";
        }
        catch { return ""; }
    }

    /// <summary>Chamado quando a tarefa de conversão termina.</summary>
    private void OnConversionComplete(ConversionResult? result)
    {
        if (result == null)
        {
            _statusText = $"Falha na conversão: {_convStatus}";
            return;
        }

        // ── Registrar o novo ObjType em memória ────────────────────────────
        _state.MeshNameById[result.ObjType] = result.RelativeMsaPath;

        // ── Reconfigura o ObjectRenderer com a nova lista ──────────────────
        if (_state.Dat != null)
        {
            // Usa a pasta raiz do cliente (pai de Env), igual ao LoadMap
            string gameFolder2 = !string.IsNullOrWhiteSpace(_clientRoot)
                ? _clientRoot
                : (Directory.GetParent(_state.EnvFolder ?? "")?.FullName ?? _state.EnvFolder ?? "");
            _objRenderer.Configure(gameFolder2, _state.MeshNameById, _state.Dat.Records, _objPartColors);
        }

        // ── Gravar no MeshList.txt ─────────────────────────────────────────
        string? meshListPath = _state.MeshListPath;
        if (string.IsNullOrWhiteSpace(meshListPath) || !File.Exists(meshListPath))
        {
            string gameFolder3 = !string.IsNullOrWhiteSpace(_clientRoot)
                ? _clientRoot
                : (Directory.GetParent(_state.EnvFolder ?? "")?.FullName ?? "");
            if (!string.IsNullOrEmpty(gameFolder3))
                meshListPath = MeshListReader.FindMeshList(gameFolder3);
        }
        if (!string.IsNullOrWhiteSpace(meshListPath))
            MeshConverter.AppendToMeshList(meshListPath, result.ObjType, result.RelativeMsaPath);
        if (!string.IsNullOrEmpty(_clientRoot))
        {
            string uiMeshList = Path.Combine(_clientRoot, "UI", "MeshList.txt");
            if (File.Exists(uiMeshList) && !string.Equals(uiMeshList, meshListPath, StringComparison.OrdinalIgnoreCase))
                MeshConverter.AppendToMeshList(uiMeshList, result.ObjType, result.RelativeMsaPath);
        }

        if (!string.IsNullOrWhiteSpace(_clientRoot))
            EnsureVanillaClientAssets(result);

        // ── Selecionar o novo tipo e fechar o modal ────────────────────────
        _addObjType = result.ObjType;
        _statusText = $"Importado: {result.DisplayName} (ObjType {result.ObjType})";
    }

    private void EnsureVanillaClientAssets(ConversionResult result)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_clientRoot) || !Directory.Exists(_clientRoot)) return;
            if (string.IsNullOrWhiteSpace(result.MsaPath) || !File.Exists(result.MsaPath)) return;

            string meshDir = Directory.Exists(Path.Combine(_clientRoot, "mesh"))
                ? Path.Combine(_clientRoot, "mesh")
                : Path.Combine(_clientRoot, "Mesh");
            Directory.CreateDirectory(meshDir);

            string meshTextureList = Path.Combine(_clientRoot, "Mesh", "MeshTextureList.bin");
            if (!File.Exists(meshTextureList))
                meshTextureList = Path.Combine(_clientRoot, "mesh", "MeshTextureList.bin");
            if (!File.Exists(meshTextureList)) return;

            var mesh = MshLoader.Load(result.MsaPath);
            if (mesh == null || mesh.TextureNames == null) return;

            string msaDir = Path.GetDirectoryName(result.MsaPath) ?? "";

            foreach (var texBase in mesh.TextureNames)
            {
                if (string.IsNullOrWhiteSpace(texBase)) continue;
                string src = FindTextureFile(msaDir, texBase);
                if (string.IsNullOrWhiteSpace(src)) continue;

                var (rgba, w, h, hasAlpha) = LoadRgbaFromFile(src);
                if (rgba == null || w <= 0 || h <= 0) continue;

                string outWys = Path.Combine(meshDir, texBase + ".wys");
                if (!File.Exists(outWys))
                {
                    byte[] wys = WysWriter.EncodeFromRgba(rgba, w, h);
                    File.WriteAllBytes(outWys, wys);
                }

                MeshTextureListBin.EnsureEntry(meshTextureList, $"mesh\\{texBase}.wys", hasAlpha);
            }
        }
        catch { }
    }

    private static string FindTextureFile(string dir, string texBase)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "";
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".PNG", ".JPG", ".JPEG", ".TGA", ".BMP" })
        {
            string p = Path.Combine(dir, texBase + ext);
            if (File.Exists(p)) return p;
        }
        return "";
    }

    private static (byte[]? rgba, int w, int h, bool hasAlpha) LoadRgbaFromFile(string path)
    {
        using var bmp = new System.Drawing.Bitmap(path);
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

        bool hasAlpha = false;
        for (int i = 0; i < data.Length; i += 4)
        {
            byte b = data[i];
            data[i] = data[i + 2];
            data[i + 2] = b;
            if (data[i + 3] != 255) hasAlpha = true;
        }
        return (data, bmp32.Width, bmp32.Height, hasAlpha);
    }

    /// <summary>Tenta descobrir o ObjType de um arquivo de mesh e adiciona no mapa.</summary>
    private void ResolveAndAddObjectFile(string filePath)
    {
        if (_state.Dat == null || _state.Trn == null) return;

        string baseName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

        // Procura o ObjType pelo nome do arquivo no MeshList
        int foundType = -1;
        foreach (var kv in _state.MeshNameById)
        {
            string mn = Path.GetFileNameWithoutExtension(kv.Value).ToLowerInvariant();
            if (mn == baseName) { foundType = kv.Key; break; }
        }

        if (foundType >= 0)
        {
            _addObjType = foundType;
            AddObjectAtCamera();
            _statusText = $"Objeto adicionado: {baseName} (ObjType {foundType})";
        }
        else
        {
            // Não encontrado na lista — usa ObjType 0 com aviso
            _statusText = $"Aviso: '{baseName}' não encontrado no MeshList. Selecione o ObjType manualmente.";
            ImGui.OpenPopup("Adicionar Objeto##modal");
        }
    }

    // ── Classificação de categoria por ObjType e nome de arquivo ─────────────

    private static bool IsCategoryTree(int objType, string relPath)
    {
        // Por faixa de ObjType (conhecidas do WYD)
        if (objType >= 331 && objType <= 342) return true;
        if (objType >= 351 && objType <= 378) return true;
        // Por nome do arquivo
        string name = Path.GetFileNameWithoutExtension(relPath).ToLowerInvariant();
        return name.Contains("tree")  || name.Contains("arv")   || name.Contains("palm")  ||
               name.Contains("bush")  || name.Contains("plant")  || name.Contains("flower")||
               name.Contains("leaf")  || name.Contains("grass")  || name.Contains("moss")  ||
               name.Contains("fern")  || name.Contains("bamboo") || name.Contains("ivy")   ||
               name.Contains("shrub") || name.Contains("trunk");
    }

    private static bool IsCategoryBuilding(int objType, string relPath)
    {
        // Por faixa de ObjType (conhecidas do WYD)
        if (objType >= 100 && objType <= 200) return true;
        if (objType >= 251 && objType <= 254) return true;
        // Por nome do arquivo
        string name = Path.GetFileNameWithoutExtension(relPath).ToLowerInvariant();
        return name.Contains("wall")   || name.Contains("castle") || name.Contains("house")  ||
               name.Contains("build")  || name.Contains("tower")  || name.Contains("gate")   ||
               name.Contains("door")   || name.Contains("bridge") || name.Contains("fence")  ||
               name.Contains("pillar") || name.Contains("column") || name.Contains("stone")  ||
               name.Contains("rock")   || name.Contains("temple") || name.Contains("floor")  ||
               name.Contains("roof")   || name.Contains("stair")  || name.Contains("step");
    }

    /// <summary>Gera cor ABGR consistente para um ObjType — usada nos badges da lista.</summary>
    private static uint ObjTypeColor(uint objType, float brightness)
    {
        uint h = objType ^ (objType >> 13); h *= 0x45d9f3b; h ^= h >> 15;
        float hue = (h & 0xFFFF) / 65535f;
        float s = 0.70f, v = brightness;
        float c = v * s, x = c * (1f - MathF.Abs((hue * 6f) % 2f - 1f)), m = v - c;
        int seg = (int)(hue * 6f) % 6;
        var (r, g, b) = seg switch {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x),
            3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x)
        };
        return ImGui.ColorConvertFloat4ToU32(new Vector4(r + m, g + m, b + m, 1f));
    }

    private void AddObjectAtCamera()
    {
        if (_state.Dat == null)  return;
        if (_state.Trn == null)  return;

        _state.PushUndoObjects();  // salva antes de adicionar para poder desfazer

        // Posição = alvo atual da câmera (centro do viewport), convertido para tile coords
        float worldX = _cam.Target.X;
        float worldZ = _cam.Target.Z;

        // Converter coordenadas de mundo para posição de objeto (PosX/PosY são em unidades do mapa)
        float posX = Math.Clamp(worldX, 0f, 127f);
        float posY = Math.Clamp(worldZ, 0f, 127f);

        // Altura do terrain naquele ponto
        int tileX = Math.Clamp((int)(worldX / TerrainRenderer.TILE_SIZE), 0, 63);
        int tileY = Math.Clamp((int)(worldZ / TerrainRenderer.TILE_SIZE), 0, 63);
        float terrainH = _state.Trn.Tiles[tileX + tileY * 64].Height;


        var newRec = new DatRecord
        {
            ObjType         = (uint)_addObjType,
            PosX            = posX,
            PosY            = posY,
            Height          = terrainH,
            Angle           = 0f,
            TextureSetIndex = 0,
            MaskIndex       = 0,
            HasScale        = false,
            ScaleH          = 1f,
            ScaleV          = 1f,
        };

        _state.Dat.Records.Add(newRec);
        int newIdx = _state.Dat.Records.Count - 1;
        _state.SelectedObjectIndex       = newIdx;
        _objRenderer.SelectedObjectIndex = newIdx;
        _state.ActiveTool                = EditorTool.Move;


        // Verifica se o mesh existe no renderer
        bool meshLoaded = _state.MeshNameById.TryGetValue(_addObjType, out var mn2);

        // ── Voar câmera até o novo objeto ────────────────────────────────────
        float worldY = terrainH * ObjectRenderer.HEIGHT_SCALE;
        _cam.Target = new OpenTK.Mathematics.Vector3(worldX, worldY, worldZ);
        // Manter distância atual mas garantir que não esteja muito longe
        if (_cam.Distance > 40f) _cam.Distance = 30f;

        // ── Notificação visual ────────────────────────────────────────────────
        string meshName = meshLoaded
            ? TextLists.GetMeshDisplayName(mn2!) : $"ObjType {_addObjType}";
        string notif = $"+ Objeto adicionado: {meshName}  (#{newIdx})  pos ({posX:F0}, {posY:F0})";
        ShowNotif(notif, 4f);
        _statusText = notif;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Painel direito: Propriedades + Ferramentas + Minimap
    // ────────────────────────────────────────────────────────────────────────

    private void DrawRightPanel(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, COL_PANEL);
        ImGui.Begin("##right", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

        // ── Propriedades ──────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Propriedades", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int sel = _state.SelectedObjectIndex;
            if (sel >= 0 && _state.Dat != null && sel < _state.Dat.Records.Count)
            {
                var r = _state.Dat.Records[sel];
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.1f, 1f), $"[{sel}] ObjType {r.ObjType}");
                ImGui.Separator();

                float px = r.PosX, py = r.PosY, ph = r.Height;
                float ang = r.Angle * 180f / MathF.PI; // exibir em graus

                ImGui.SetNextItemWidth(-1f);
                if (ImGui.DragFloat("PosX##opx", ref px, 0.1f, 0f, 127f, "%.2f"))
                { r.PosX = px; _state.Dat.Records[sel] = r; _sceneDirty = true; }

                ImGui.SetNextItemWidth(-1f);
                if (ImGui.DragFloat("PosY##opy", ref py, 0.1f, 0f, 127f, "%.2f"))
                { r.PosY = py; _state.Dat.Records[sel] = r; _sceneDirty = true; }

                ImGui.SetNextItemWidth(-1f);
                if (ImGui.DragFloat("Altura##oph", ref ph, 0.5f, -128f, 127f, "%.1f"))
                { r.Height = ph; _state.Dat.Records[sel] = r; _sceneDirty = true; }

                ImGui.SetNextItemWidth(-1f);
                if (ImGui.DragFloat("Angulo (graus)##oa", ref ang, 1f, -360f, 360f, "%.1f°"))
                { r.Angle = ang * MathF.PI / 180f; _state.Dat.Records[sel] = r; _sceneDirty = true; }

                if (r.HasScale)
                {
                    float sh = r.ScaleH, sv = r.ScaleV;
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.DragFloat("ScaleH##osch", ref sh, 0.05f, 0.01f, 10f, "%.2f"))
                    { r.ScaleH = sh; _state.Dat.Records[sel] = r; _sceneDirty = true; }
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.DragFloat("ScaleV##oscv", ref sv, 0.05f, 0.01f, 10f, "%.2f"))
                    { r.ScaleV = sv; _state.Dat.Records[sel] = r; _sceneDirty = true; }
                }

                ImGui.Spacing();
                if (ImGui.Button("Deselecionar##ods", new Vector2(-1, 0)))
                    _state.SelectedObjectIndex = -1;
            }
            else
            {
                ImGui.TextDisabled("Nenhum objeto selecionado.");
                ImGui.TextDisabled("Use Select (Q) + clique para selecionar,");
                ImGui.TextDisabled("ou Move (W) + arraste para mover.");
            }
        }

        // ── Visualização ──────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Visualizacao", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string[] visModes = { "Terrain", "Wireframe" };
            int viz = (int)_state.VizMode;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.Combo("##viz", ref viz, visModes, visModes.Length))
                _state.VizMode = (VisualizationMode)viz;

            bool rm = _state.RenderMeshes;
            if (ImGui.Checkbox("Renderizar meshes reais", ref rm))
                _state.RenderMeshes = rm;

            bool sg = _state.ShowGrid;
            if (ImGui.Checkbox("Grid", ref sg))
                _state.ShowGrid = sg;

            if (ImGui.Button("Reset Camera", new Vector2(-1, 0)))
                _cam.Reset();
        }

        // ── Ferramentas ───────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Tools", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string[] toolNames = Enum.GetNames<EditorTool>();
            int tidx = (int)_state.ActiveTool;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.Combo("Ferramenta##tc", ref tidx, toolNames, toolNames.Length))
                _state.ActiveTool = (EditorTool)tidx;

            ImGui.Spacing();

            switch (_state.ActiveTool)
            {
                case EditorTool.Level:        DrawToolLevel();        break;
                case EditorTool.PaintTexture: DrawToolPaint();        break;
                case EditorTool.AttributeMap: DrawToolAttributeMap(); break;

                case EditorTool.Select:
                    ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.5f, 1f), "Clique num objeto 3D para selecionar.");
                    ImGui.TextDisabled("Ctrl+Z = desfazer   Q=Select  W=Move");
                    ImGui.Separator();
                    ImGui.TextDisabled("Para EDITAR TERRENO:");
                    if (ImGui.Button("  Mudar para Terrain (T)  ##qkTerrain", new Vector2(-1, 0)))
                        _state.ActiveTool = EditorTool.Level;
                    ImGui.TextDisabled("Para PINTAR TEXTURAS:");
                    if (ImGui.Button("  Mudar para Paint (P)  ##qkPaint", new Vector2(-1, 0)))
                        _state.ActiveTool = EditorTool.PaintTexture;
                    break;

                case EditorTool.Move:
                    ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.5f, 1f), "Clique num objeto para arrastar.");
                    ImGui.TextDisabled("Eixos X/Z: arraste no plano horizontal.");
                    ImGui.TextDisabled("Eixo Y: arraste o gizmo verde (altura).");
                    break;

                case EditorTool.Area:
                    DrawToolArea();
                    break;

                case EditorTool.Rotate:
                    ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.5f, 1f), "Clique num objeto e arraste para rotacionar.");
                    ImGui.TextDisabled("Shift = ajuste fino");
                    if (_state.Dat != null && _state.SelectedObjectIndex >= 0 &&
                        _state.SelectedObjectIndex < _state.Dat.Records.Count)
                    {
                        float a = _state.Dat.Records[_state.SelectedObjectIndex].Angle;
                        ImGui.TextDisabled($"Selecionado: #{_state.SelectedObjectIndex}  Angle={a:F3}");
                    }
                    break;

                case EditorTool.Scale:
                    ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.5f, 1f), "Clique num objeto e arraste para escalar.");
                    ImGui.TextDisabled("Shift = ajuste fino");
                    if (_state.Dat != null && _state.SelectedObjectIndex >= 0 &&
                        _state.SelectedObjectIndex < _state.Dat.Records.Count)
                    {
                        var rs = _state.Dat.Records[_state.SelectedObjectIndex];
                        float sh = rs.HasScale ? rs.ScaleH : 1f;
                        float sv = rs.HasScale ? rs.ScaleV : 1f;
                        ImGui.TextDisabled($"Selecionado: #{_state.SelectedObjectIndex}  Scale=({sh:F2},{sv:F2})");
                        if (ImGui.Button("Reset scale", new Vector2(-1, 0)))
                        {
                            _state.PushUndoObjects();
                            rs.HasScale = true;
                            rs.ScaleH = 1f;
                            rs.ScaleV = 1f;
                            _state.Dat.Records[_state.SelectedObjectIndex] = rs;
                            _statusText = "Scale resetado para 1.0";
                        }
                    }
                    break;

                case EditorTool.Collision:
                    DrawToolCollision();
                    break;

                case EditorTool.Trigger:
                    DrawToolTrigger();
                    break;

                case EditorTool.Light:
                    DrawToolLight();
                    break;

                case EditorTool.Object:
                    ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.5f, 1f), "Clique no terreno para colocar um objeto.");
                    ImGui.TextDisabled($"ObjType atual: {_addObjType} (altere em 'Objetos' ou no popup de adicionar).");
                    break;

                default:
                    ImGui.TextDisabled("(ferramenta em desenvolvimento)");
                    break;
            }
        }

        // ── Minimap ───────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Minimap", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_state.MinimapTexture != 0)
            {
                float avW  = ImGui.GetContentRegionAvail().X;
                float mmSz = Math.Min(avW - 8f, 200f);
                var   mpUV0 = new Vector2(0, 1);
                var   mpUV1 = new Vector2(1, 0);
                ImGui.Image(new IntPtr(_state.MinimapTexture), new Vector2(mmSz, mmSz), mpUV0, mpUV1);

                // Retículo no minimap
                if (_state.HoverTileX >= 0 && _state.HoverTileY >= 0)
                {
                    var mmPos = ImGui.GetItemRectMin();
                    float cx  = mmPos.X + (_state.HoverTileX / 64f) * mmSz;
                    float cy  = mmPos.Y + (1f - _state.HoverTileY / 64f) * mmSz;
                    ImGui.GetWindowDrawList().AddCircle(new Vector2(cx, cy), 3f, 0xFFFF4444);
                }
            }
            else
            {
                ImGui.TextDisabled("(carregue um mapa)");
            }
        }

        ImGui.PopStyleColor();
        ImGui.End();
    }

    private void DrawToolLevel()
    {
        // ── Dica de uso ─────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "Clique e arraste no mapa:");
        ImGui.TextDisabled("  Esquerdo  = Elevar terreno");
        ImGui.TextDisabled("  Shift+Esq = Abaixar terreno");
        ImGui.TextDisabled("  Ctrl+Esq  = Nivelar para altura-alvo");
        ImGui.Separator();

        // ── Modo atual ──────────────────────────────────────────────────────
        bool flatten = _state.FlattenMode;
        if (flatten)
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Modo: NIVELAR (Ctrl)");
        else
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "Modo: ELEVAR/ABAIXAR");

        // ── Velocidade de elevação ───────────────────────────────────────────
        float rd = Math.Abs(_state.RaiseDelta);
        ImGui.SliderFloat("Velocidade", ref rd, 0.1f, 5f, "%.1f unid/frame");
        _state.RaiseDelta = rd;  // sinal (+/-) aplicado pelo HandleViewportMouse (Shift)

        // ── Raio do pincel ───────────────────────────────────────────────────
        float br = _state.BrushRadius;
        ImGui.SliderFloat("Raio", ref br, 0.5f, 20f);
        _state.BrushRadius = br;

        // ── Altura-alvo (só para modo Flatten) ──────────────────────────────
        float lh = _state.LevelHeight;
        ImGui.InputFloat("Altura-alvo (Ctrl)", ref lh, 1f, 5f, "%.0f");
        _state.LevelHeight = Math.Clamp(lh, _state.HeightMin, _state.HeightMax);

        bool sq = _state.SquareBrush;
        ImGui.Checkbox("Pincel quadrado", ref sq);
        _state.SquareBrush = sq;

        if (ImGui.Button("Capturar altura do tile atual", new Vector2(-1, 0)))
        {
            _state.CaptureHeight(_state.HoverTileX, _state.HoverTileY);
            _statusText = $"Altura capturada: {_state.LevelHeight}";
        }

        ImGui.Separator();
        ImGui.TextDisabled($"Limite: {_state.HeightMin} .. {_state.HeightMax}");

        // Tile em hover
        if (_state.HoverTileX >= 0 && _state.HoverTileY >= 0 && _state.Trn != null)
        {
            var t = _state.Trn.Tiles[_state.HoverTileX + _state.HoverTileY * 64];
            ImGui.TextDisabled($"Tile ({_state.HoverTileX},{_state.HoverTileY})  Alt: {t.Height}");
        }
    }

    private void DrawToolPaint()
    {
        bool sq = _state.PaintSquare;
        ImGui.Checkbox("Brush quadrado", ref sq);
        _state.PaintSquare = sq;

        float br = _state.BrushRadius;
        ImGui.SliderFloat("Raio", ref br, 0.5f, 10f);
        _state.BrushRadius = br;

        int tileId = _state.SelectedTileIndex;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputInt("Textura tile", ref tileId, 1, 5))
            _state.SelectedTileIndex = Math.Clamp(tileId, 0, 255);

        // Preview da textura selecionada
        const float PREV = 48f;
        int previewTex = _tileCache.Get(_state.SelectedTileIndex);
        if (previewTex != 0)
        {
            ImGui.Image(new IntPtr(previewTex), new Vector2(PREV, PREV));
        }
        else
        {
            var draw2 = ImGui.GetWindowDrawList();
            var p2    = ImGui.GetCursorScreenPos() + new Vector2(0, 4);
            draw2.AddRectFilled(p2, p2 + new Vector2(PREV, PREV), TileImguiColor(_state.SelectedTileIndex), 4f);
            draw2.AddRect(p2, p2 + new Vector2(PREV, PREV), 0xFF2288CC, 4f);
            draw2.AddText(p2 + new Vector2(4, 4), 0xFFFFFFFF, _state.SelectedTileIndex.ToString());
            ImGui.Dummy(new Vector2(0, PREV + 8));
        }

        ImGui.TextWrapped("Pintura aplica em area quadrada de tiles e atua na textura principal.");
    }

    private void DrawToolAttributeMap()
    {
        // Tenta auto-detectar o AttributeMap.dat se ainda não foi carregado
        if (_state.AttributeMap == null)
            TryAutoLoadAttributeMap();

        // Se path já está preenchido mas o objeto ainda não carregou, tenta carregar
        if (_state.AttributeMap == null && !string.IsNullOrWhiteSpace(_state.AttributeMapPath) && File.Exists(_state.AttributeMapPath))
        {
            if (_state.LoadAttributeMap(_state.AttributeMapPath))
                RebuildAttrOverlay();
        }

        // Reconstrói overlay se foi destruído (ex: troca de mapa)
        if (_state.AttributeMap != null && _attrOverlayTex == 0)
            RebuildAttrOverlay();

        if (_state.AttributeMap == null)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), "AttributeMap.dat nao encontrado.");
            ImGui.TextDisabled("Coloque o arquivo AttributeMap.dat na pasta Env, UI ou Mesh,");
            ImGui.TextDisabled("ou na raiz do cliente WYD.");
            ImGui.Separator();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("Caminho##attrpath", ref _state.AttributeMapPath, 512);
            if (ImGui.Button("Carregar##attrload", new Vector2(-1, 0)))
            {
                if (File.Exists(_state.AttributeMapPath))
                    try { if (_state.LoadAttributeMap(_state.AttributeMapPath)) RebuildAttrOverlay(); } catch { }
            }
            return;
        }

        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "AttributeMap.dat carregado!");
        ImGui.TextDisabled("Clique/arraste = adicionar bit. Shift+Clique/arraste = remover bit.");
        ImGui.Separator();

        // ── Tipo de zona a pintar ───────────────────────────────────────────
        ImGui.Text("Zona a pintar:");
        var types = AttributeMapFile.AttributeTypes;
        for (int i = 0; i < types.Length; i++)
        {
            var (mask, name, color) = types[i];
            bool selected = (_state.SelectedAttribute == mask);
            var c = new Vector4((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, ((color >> 16) & 0xFF) / 255f, 1f);
            var ch = new Vector4(MathF.Min(1f, c.X + 0.15f), MathF.Min(1f, c.Y + 0.15f), MathF.Min(1f, c.Z + 0.15f), 1f);
            var ca = new Vector4(MathF.Min(1f, c.X + 0.25f), MathF.Min(1f, c.Y + 0.25f), MathF.Min(1f, c.Z + 0.25f), 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, c);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ch);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ca);
            if (selected) ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            if (selected) ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 1f, 1f, 0.9f));

            if (ImGui.Button($"{name}##attr{i}", new Vector2(-1, 0)))
                _state.SelectedAttribute = mask;
            if (selected) ImGui.PopStyleColor();
            if (selected) ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }

        ImGui.Separator();

        float br = _state.AttrBrushRadius;
        ImGui.SliderFloat("Raio (tiles)", ref br, 0f, 6f, "%.0f");
        _state.AttrBrushRadius = br;

        bool showOv = _state.ShowAttributeOverlay;
        if (ImGui.Checkbox("Manter overlay visivel", ref showOv))
        {
            _state.ShowAttributeOverlay = showOv;
            if (showOv && _attrOverlayTex == 0) RebuildAttrOverlay();
        }

        ImGui.Separator();
        // Info tile atual
        int tx = _state.HoverTileX, ty = _state.HoverTileY;
        if (tx >= 0 && ty >= 0)
        {
            int ex = _state.Trn != null ? _state.Trn.EnvPosX : 0;
            int ey = _state.Trn != null ? _state.Trn.EnvPosY : 0;
            byte cur = _state.AttributeMap.GetAtFieldTile(ex, ey, tx, ty);
            ImGui.TextDisabled($"Tile ({tx},{ty}) attr={cur} [{GetAttrName(cur)}]");
        }

        if (ImGui.Button("Salvar AttributeMap##saveattrmap", new Vector2(-1, 0)))
        {
            try
            {
                SaveAttributeMapEverywhere(saveWorkingCopy: true);
                _statusText = "AttributeMap salvo!";
            }
            catch (Exception ex) { _statusText = "Erro: " + ex.Message; }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Ferramenta Area — seleção retangular de objetos
    // ────────────────────────────────────────────────────────────────────────

    private void DrawToolArea()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.5f, 1f), "Seleção por área:");
        ImGui.TextDisabled("  Clique+arraste = retângulo de seleção");
        ImGui.TextDisabled("  Ctrl+clique = manter seleção anterior");
        ImGui.Separator();

        int objCount = _state.Dat?.Records.Count ?? 0;
        ImGui.TextDisabled($"Objetos no mapa: {objCount}");

        int sel = _state.SelectedObjectIndex;
        if (sel >= 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.1f, 1f), $"Selecionado: #{sel}");
            ImGui.TextDisabled("Use Select/Move para reposicionar.");
        }
        else
        {
            ImGui.TextDisabled("Nenhum objeto selecionado.");
        }

        ImGui.Separator();
        if (ImGui.Button("Limpar seleção##areaclr", new Vector2(-1, 0)))
            _state.SelectedObjectIndex = -1;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Ferramenta Collision — atalho AttrMap CantGo
    // ────────────────────────────────────────────────────────────────────────

    private void DrawToolCollision()
    {
        if (_state.AttributeMap == null)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), "AttributeMap.dat não carregado.");
            ImGui.TextDisabled("Configure o AttributeMap nas Settings.");
            return;
        }
        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "Colisão (CantGo):");
        ImGui.TextDisabled("  Clique/arraste = adicionar bloqueio");
        ImGui.TextDisabled("  Shift+Clique   = remover bloqueio");
        ImGui.Separator();

        float br = _state.AttrBrushRadius;
        ImGui.SliderFloat("Raio (tiles)##coll", ref br, 0f, 6f, "%.0f");
        _state.AttrBrushRadius = br;

        bool showOv = _state.ShowAttributeOverlay;
        if (ImGui.Checkbox("Manter overlay visível##collov", ref showOv))
        {
            _state.ShowAttributeOverlay = showOv;
            if (showOv && _attrOverlayTex == 0) RebuildAttrOverlay();
        }

        if (_state.HoverTileX >= 0 && _state.HoverTileY >= 0 && _state.Trn != null)
        {
            int ex = _state.Trn.EnvPosX, ey = _state.Trn.EnvPosY;
            byte cur = _state.AttributeMap.GetAtFieldTile(ex, ey, _state.HoverTileX, _state.HoverTileY);
            bool blocked = (cur & 2) != 0;
            ImGui.TextDisabled($"Tile ({_state.HoverTileX},{_state.HoverTileY}) bloqueado: {(blocked ? "SIM" : "não")}");
        }

        ImGui.Separator();
        if (ImGui.Button("Salvar AttributeMap##savecoll", new Vector2(-1, 0)))
            try { SaveAttributeMapEverywhere(true); _statusText = "AttrMap salvo!"; } catch (Exception e) { _statusText = "Erro: " + e.Message; }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Ferramenta Trigger — atalho AttrMap com mask selecionável
    // ────────────────────────────────────────────────────────────────────────

    private void DrawToolTrigger()
    {
        if (_state.AttributeMap == null)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), "AttributeMap.dat não carregado.");
            ImGui.TextDisabled("Configure o AttributeMap nas Settings.");
            return;
        }
        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "Trigger / Zona:");
        ImGui.TextDisabled("  Clique/arraste = marcar zona");
        ImGui.TextDisabled("  Shift+Clique   = desmarcar zona");
        ImGui.Separator();

        ImGui.Text("Tipo de zona:");
        foreach (var (mask, name, color) in AttributeMapFile.AttributeTypes)
        {
            if (mask == 0 || mask == 2) continue; // Normal e CantGo estão na Collision
            bool sel = (_triggerAttrMask == mask);
            var c = new Vector4((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, ((color >> 16) & 0xFF) / 255f, 1f);
            if (sel) ImGui.PushStyleColor(ImGuiCol.Button, c);
            if (ImGui.Button($"{name}##trig{mask}", new Vector2(-1, 0)))
                _triggerAttrMask = mask;
            if (sel) ImGui.PopStyleColor();
        }

        ImGui.Separator();
        float br = _state.AttrBrushRadius;
        ImGui.SliderFloat("Raio (tiles)##trig", ref br, 0f, 6f, "%.0f");
        _state.AttrBrushRadius = br;

        bool showOv = _state.ShowAttributeOverlay;
        if (ImGui.Checkbox("Manter overlay##trigov", ref showOv))
        {
            _state.ShowAttributeOverlay = showOv;
            if (showOv && _attrOverlayTex == 0) RebuildAttrOverlay();
        }

        if (ImGui.Button("Salvar AttributeMap##savetrig", new Vector2(-1, 0)))
            try { SaveAttributeMapEverywhere(true); _statusText = "AttrMap salvo!"; } catch (Exception e) { _statusText = "Erro: " + e.Message; }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Ferramenta Light — pintura de vertex color (tile.Color)
    // ────────────────────────────────────────────────────────────────────────

    private void DrawToolLight()
    {
        // ── Aba: Cor | Textura ───────────────────────────────────────────────
        bool tabCor = ImGui.BeginTabBar("##lighttabs");
        if (tabCor)
        {
            if (ImGui.BeginTabItem("Cor Terreno"))
            {
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), "Pintura de cor (Vertex Light):");
                ImGui.TextDisabled("  Clique/arraste = aplicar cor");
                ImGui.TextDisabled("  Shift+Clique   = resetar branco");
                ImGui.Separator();

                bool changed = false;
                changed |= ImGui.SliderFloat("R##lr", ref _lightR, 0f, 1f);
                changed |= ImGui.SliderFloat("G##lg", ref _lightG, 0f, 1f);
                changed |= ImGui.SliderFloat("B##lb", ref _lightB, 0f, 1f);
                if (changed)
                    _lightBrushColor = (uint)(_lightR * 255) | ((uint)(_lightG * 255) << 8) | ((uint)(_lightB * 255) << 16);

                var p0 = ImGui.GetCursorScreenPos();
                uint previewCol = 0xFF000000u
                    | ((_lightBrushColor & 0xFFu) << 16)
                    | (((_lightBrushColor >> 8) & 0xFFu) << 8)
                    | ((_lightBrushColor >> 16) & 0xFFu);
                ImGui.GetWindowDrawList().AddRectFilled(p0, p0 + new Vector2(ImGui.GetContentRegionAvail().X, 18), previewCol, 4f);
                ImGui.Dummy(new Vector2(0, 20));

                ImGui.Separator();
                ImGui.Text("Cor rápida:");
                var colors = new (string label, float r, float g, float b)[]
                {
                    ("Branco",   1f, 1f, 1f),   ("Amarelo",  1f, 0.9f, 0.4f),
                    ("Laranja",  1f, 0.5f, 0.1f), ("Azul",   0.3f, 0.5f, 1f),
                    ("Roxo",     0.6f, 0.2f, 0.8f), ("Verde", 0.2f, 0.8f, 0.3f),
                    ("Vermelho", 0.9f, 0.1f, 0.1f), ("Escuro", 0.2f, 0.2f, 0.25f),
                };
                float bw = (ImGui.GetContentRegionAvail().X - 4f) / 2f;
                for (int i = 0; i < colors.Length; i++)
                {
                    var (lbl, r, g, b) = colors[i];
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(r * 0.6f, g * 0.6f, b * 0.6f, 1f));
                    if (ImGui.Button(lbl + "##lc" + i, new Vector2(bw, 0)))
                    {
                        _lightR = r; _lightG = g; _lightB = b;
                        _lightBrushColor = (uint)(r * 255) | ((uint)(g * 255) << 8) | ((uint)(b * 255) << 16);
                    }
                    ImGui.PopStyleColor();
                    if ((i & 1) == 0) ImGui.SameLine(0, 4);
                }

                ImGui.Separator();
                float br = _state.BrushRadius;
                ImGui.SliderFloat("Raio (tiles)##light", ref br, 0.5f, 10f);
                _state.BrushRadius = br;

                // Toggle: pintar objetos no raio
                bool paintObjs = _lightPaintObjects;
                if (ImGui.Checkbox("Pintar objetos no raio também##lpaintobj", ref paintObjs))
                    _lightPaintObjects = paintObjs;
                if (_lightPaintObjects)
                {
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "  ⚠ Pintará COR nos objetos!");
                    bool byPart = _lightPaintByPart;
                    if (ImGui.Checkbox("Pintar por parte (submesh)##lpaintpart", ref byPart))
                    {
                        _lightPaintByPart = byPart;
                        if (!byPart) _lightPartIndex = -1;
                    }
                    if (_lightPaintByPart)
                    {
                        int part = _lightPartIndex < 0 ? 0 : _lightPartIndex;
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.InputInt("Parte##lpart", ref part, 1))
                            _lightPartIndex = Math.Max(0, part);
                        ImGui.TextDisabled("Use a aba 'Textura Objeto' para ver quantas partes o mesh tem.");
                    }
                }

                if (_state.HoverTileX >= 0 && _state.HoverTileY >= 0 && _state.Trn != null)
                {
                    var t = _state.Trn.Tiles[_state.HoverTileX + _state.HoverTileY * 64];
                    ImGui.TextDisabled($"Tile ({_state.HoverTileX},{_state.HoverTileY}) cor=#{(t.Color & 0xFFFFFFu):X6}");
                }

                ImGui.Separator();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.1f, 0.1f, 1f));
                if (ImGui.Button("Resetar COR de TODOS os objetos##resetallobj", new Vector2(-1, 0)))
                {
                    if (_state.Dat != null)
                    {
                        _state.PushUndoObjects();
                        for (int oi = 0; oi < _state.Dat.Records.Count; oi++)
                        {
                            var orec = _state.Dat.Records[oi];
                            orec.HasColorOverride = false;
                            orec.ColorR = orec.ColorG = orec.ColorB = 255;
                            _state.Dat.Records[oi] = orec;
                        }
                        _objPartColors.Clear();
                        _objRenderer.MarkColorsDirty();
                        _statusText = "Todas as cores de objetos foram resetadas.";
                    }
                }
                ImGui.PopStyleColor();
                ImGui.TextDisabled("(use para desfazer pintura acidental)");

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Textura Objeto"))
            {
                DrawLightTextureTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    // ── Seletor de textura para pintar objetos ─────────────────────────────
    private void DrawLightTextureTab()
    {
        // Garante que o scan de texturas foi feito antes de exibir a lista
        EnsureTextureScan();
        _texPreview.Configure(_state.EnvFolder);

        ImGui.TextColored(new Vector4(0.55f, 0.85f, 1f, 1f), "Pintar textura em objetos:");
        ImGui.TextDisabled("  Selecione textura abaixo e aplique");
        ImGui.TextDisabled("  ao objeto selecionado.");
        ImGui.Separator();

        // Textura atualmente selecionada para pintar
        if (!string.IsNullOrEmpty(_lightTexOverride))
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), $"Textura: {_lightTexOverride}");
            if (ImGui.Button("Limpar textura##clrtex", new Vector2(-1, 0)))
                _lightTexOverride = "";
        }
        else
        {
            ImGui.TextDisabled("(nenhuma textura selecionada)");
        }

        ImGui.Separator();

        // Filtro
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##ltexfilter", ref _lightTexFilter, 64);
        ImGui.SameLine(0, 4);
        ImGui.TextDisabled("Busca");

        // Lista de texturas disponíveis (usa _texAll que já está carregado)
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.06f, 0.08f, 0.16f, 1f));
        ImGui.BeginChild("##ltexlist", new Vector2(-1, 130), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        var list = _texAll; // já populado pelo scan de texturas do EnvFolder
        for (int i = 0; i < list.Count; i++)
        {
            string nm = list[i];
            if (!string.IsNullOrEmpty(_lightTexFilter) &&
                !nm.Contains(_lightTexFilter, StringComparison.OrdinalIgnoreCase)) continue;

            bool isSel = (i == _lightTexSelected);
            if (ImGui.Selectable(nm + "##ltex" + i, isSel))
            {
                _lightTexSelected = i;
                _lightTexOverride = nm;
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        // Preview da textura selecionada
        if (!string.IsNullOrEmpty(_lightTexOverride))
        {
            int prevTex = _texPreview.Get(_lightTexOverride);
            if (prevTex != 0)
                ImGui.Image(new IntPtr(prevTex), new Vector2(64, 64));
            else
                ImGui.TextDisabled("(sem prévia)");
        }

        ImGui.Separator();

        // ── Aplicar ao objeto selecionado ────────────────────────────────
        int selIdx = _state.SelectedObjectIndex;
        bool hasSel = selIdx >= 0 && _state.Dat != null && selIdx < _state.Dat.Records.Count;

        if (!hasSel)
        {
            ImGui.TextDisabled("(selecione um objeto com Select)");
        }
        else
        {
            var orec = _state.Dat!.Records[selIdx];
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.1f, 1f), $"Selecionado: #{selIdx}  ObjType {orec.ObjType}");

            // Textura atual do objeto
            string curTex = orec.TextureOverrideName ?? "";
            if (!string.IsNullOrEmpty(curTex))
                ImGui.TextDisabled($"Textura atual: {curTex}");
            else
                ImGui.TextDisabled("Textura atual: (original do mesh)");

            // Cor atual do objeto
            float ocR = orec.HasColorOverride ? orec.ColorR / 255f : 1f;
            float ocG = orec.HasColorOverride ? orec.ColorG / 255f : 1f;
            float ocB = orec.HasColorOverride ? orec.ColorB / 255f : 1f;
            ImGui.Text("Cor:");
            ImGui.SameLine();
            var cp0 = ImGui.GetCursorScreenPos();
            uint objPreview = 0xFF000000u | ((uint)(ocB * 255) << 16) | ((uint)(ocG * 255) << 8) | (uint)(ocR * 255);
            ImGui.GetWindowDrawList().AddRectFilled(cp0, cp0 + new Vector2(40, 14), objPreview, 3f);
            ImGui.Dummy(new Vector2(40, 14));

            int subCount = _objRenderer.GetSubMeshCount((int)orec.ObjType);
            if (subCount > 0)
            {
                ImGui.TextDisabled($"Partes do mesh: {subCount} (0..{subCount - 1})");
                bool byPart = _lightPaintByPart;
                if (ImGui.Checkbox("Aplicar cor só na parte##selpart", ref byPart))
                {
                    _lightPaintByPart = byPart;
                    if (!byPart) _lightPartIndex = -1;
                }
                if (_lightPaintByPart)
                {
                    int part = _lightPartIndex < 0 ? 0 : _lightPartIndex;
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputInt("Parte##selparti", ref part, 1))
                        _lightPartIndex = Math.Clamp(part, 0, subCount - 1);
                }
            }

            // Botões de ação
            bool canApply = !string.IsNullOrEmpty(_lightTexOverride);
            if (!canApply) ImGui.BeginDisabled();
            if (ImGui.Button("Aplicar textura##applyobjcol", new Vector2(-1, 0)))
            {
                _state.PushUndoObjects();
                orec.TextureOverrideName = _lightTexOverride;
                _state.Dat.Records[selIdx] = orec;
                _objRenderer.MarkColorsDirty();
                _statusText = $"Textura '{_lightTexOverride}' aplicada ao obj #{selIdx}";
            }
            if (!canApply) ImGui.EndDisabled();

            if (ImGui.Button("Aplicar cor do brush##applybrushcol", new Vector2(-1, 0)))
            {
                _state.PushUndoObjects();
                if (_lightPaintByPart && _lightPartIndex >= 0)
                {
                    if (!_objPartColors.TryGetValue(selIdx, out var map))
                    {
                        map = new Dictionary<int, uint>();
                        _objPartColors[selIdx] = map;
                    }
                    map[_lightPartIndex] = _lightBrushColor & 0xFFFFFFu;
                }
                else
                {
                    orec.HasColorOverride = true;
                    orec.ColorR = (byte)(_lightBrushColor & 0xFF);
                    orec.ColorG = (byte)((_lightBrushColor >> 8) & 0xFF);
                    orec.ColorB = (byte)((_lightBrushColor >> 16) & 0xFF);
                    _state.Dat.Records[selIdx] = orec;
                }
                _objRenderer.MarkColorsDirty();
                _statusText = _lightPaintByPart && _lightPartIndex >= 0
                    ? $"Cor aplicada ao objeto #{selIdx} (parte {_lightPartIndex})"
                    : $"Cor aplicada ao objeto #{selIdx}";
            }

            if (!string.IsNullOrEmpty(curTex) || orec.HasColorOverride || (_objPartColors.TryGetValue(selIdx, out var pm) && pm.Count != 0))
            {
                if (ImGui.Button("Resetar tudo (cor+textura)##resetall", new Vector2(-1, 0)))
                {
                    _state.PushUndoObjects();
                    orec.TextureOverrideName = null;
                    orec.HasColorOverride = false;
                    orec.ColorR = orec.ColorG = orec.ColorB = 255;
                    _state.Dat!.Records[selIdx] = orec;
                    _objPartColors.Remove(selIdx);
                    _objRenderer.MarkColorsDirty();
                    _statusText = $"Objeto #{selIdx} resetado.";
                }
            }

            ImGui.Separator();
            ImGui.TextDisabled("Texture Set (0=padrão):");
            int texSet = orec.TextureSetIndex;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputInt("##texset", ref texSet, 1))
            {
                texSet = Math.Max(0, texSet);
                _state.PushUndoObjects();
                orec.TextureSetIndex = texSet;
                _state.Dat!.Records[selIdx] = orec;
                _statusText = $"TextureSetIndex #{selIdx} = {texSet}";
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Map Browser (aba "Mapa")
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Extrai row/col de nomes como "Field0304" → row=3, col=4.</summary>
    private static bool ParseFieldCoords(string name, out int row, out int col)
    {
        row = col = 0;
        if (!name.StartsWith("Field", StringComparison.OrdinalIgnoreCase)) return false;
        var digits = name[5..]; // tudo após "Field"
        // Truncar em não-dígito
        int len = 0;
        while (len < digits.Length && char.IsDigit(digits[len])) len++;
        digits = digits[..len];

        if (digits.Length == 4)
        {
            row = int.Parse(digits[..2]);
            col = int.Parse(digits[2..]);
            return true;
        }
        if (digits.Length == 2)
        {
            row = 0;
            col = int.Parse(digits);
            return true;
        }
        return false;
    }

    private void DrawMapBrowser(Vector2 vpPos, Vector2 vpSize)
    {
        var draw = ImGui.GetWindowDrawList();
        var io   = ImGui.GetIO();

        // Nota: DrawList usa formato ABGR (0xAABBGGRR), diferente de RGBA.
        // Helper inline: C(r,g,b,a) = (a<<24)|(b<<16)|(g<<8)|r
        static uint C(byte r, byte g, byte b, byte a = 255)
            => (uint)a << 24 | (uint)b << 16 | (uint)g << 8 | r;

        // ── Fundo gradiente escuro azul-marinho ──────────────────────────────
        draw.AddRectFilledMultiColor(vpPos, vpPos + vpSize,
            C(10,  8, 22), C( 8,  7, 18), C( 5,  5, 12), C( 5,  4, 14));

        // ── Grid de linhas decorativas (azul sutil) ──────────────────────────
        draw.PushClipRect(vpPos, vpPos + vpSize, true);
        const float GRID_STEP = 36f;
        uint gridCol = C(0, 40, 140, 12);   // azul escuro, 5% alpha
        for (float gx = vpPos.X % GRID_STEP; gx < vpPos.X + vpSize.X; gx += GRID_STEP)
            draw.AddLine(new Vector2(gx, vpPos.Y), new Vector2(gx, vpPos.Y + vpSize.Y), gridCol);
        for (float gy = vpPos.Y % GRID_STEP; gy < vpPos.Y + vpSize.Y; gy += GRID_STEP)
            draw.AddLine(new Vector2(vpPos.X, gy), new Vector2(vpPos.X + vpSize.X, gy), gridCol);

        // ── Vignette (borda escura suave) ────────────────────────────────────
        draw.AddRectFilledMultiColor(vpPos, vpPos + vpSize,
            C(0,0,0,80), C(0,0,0,0), C(0,0,0,0), C(0,0,0,80));
        draw.AddRectFilledMultiColor(
            vpPos + new Vector2(0, vpSize.Y * 0.5f), vpPos + vpSize,
            C(0,0,0,0), C(0,0,0,0), C(0,0,0,80), C(0,0,0,80));

        draw.PopClipRect();

        var center = vpPos + vpSize * 0.5f;

        if (_availableFields.Length == 0)
        {
            // ── Painel central flutuante ─────────────────────────────────────
            float boxW = Math.Min(460f, vpSize.X - 60f);
            float boxH = 160f;
            var boxMin = center - new Vector2(boxW * 0.5f, boxH * 0.5f);
            var boxMax = boxMin + new Vector2(boxW, boxH);

            // Fundo do painel
            draw.AddRectFilled(boxMin, boxMax, C(14, 12, 28, 200), 10f);
            // Borda azul
            draw.AddRect(boxMin, boxMax, C(0, 60, 210, 90), 10f, ImDrawFlags.None, 1.5f);
            // Linha accent no topo (azul brilhante)
            draw.AddLine(
                boxMin + new Vector2(10, 0),
                boxMin + new Vector2(boxW - 10, 0),
                C(0, 80, 255), 2f);

            // Título
            string icon = _state.EnvFolder == "" ? "  FoxMap Studio  " : "  Carregando...  ";
            var tsIcon = ImGui.CalcTextSize(icon);
            draw.AddText(new Vector2(center.X - tsIcon.X * 0.5f, boxMin.Y + 18f),
                C(200, 220, 255), icon);

            // Separador interno
            draw.AddLine(
                new Vector2(boxMin.X + 16, boxMin.Y + 42),
                new Vector2(boxMax.X - 16, boxMin.Y + 42),
                C(0, 60, 180, 50));

            // Dica principal
            string hint = _state.EnvFolder == ""
                ? "Configure a pasta do cliente WYD no painel esquerdo"
                : "Escaneando mapas...";
            var tsHint = ImGui.CalcTextSize(hint);
            draw.AddText(new Vector2(center.X - tsHint.X * 0.5f, boxMin.Y + 54f),
                C(110, 130, 170), hint);

            // Dica secundária
            if (_state.EnvFolder == "")
            {
                string sub = "Open  ->  selecione a pasta  ->  carregue um Field";
                var tsSub = ImGui.CalcTextSize(sub);
                draw.AddText(new Vector2(center.X - tsSub.X * 0.5f, boxMin.Y + 80f),
                    C(55, 70, 100), sub);
            }

            // Versão no canto inferior direito
            string ver = "v1.0";
            var tsVer = ImGui.CalcTextSize(ver);
            draw.AddText(vpPos + vpSize - tsVer - new Vector2(12, 8),
                C(30, 45, 75), ver);

            ImGui.InvisibleButton("##browserhold", vpSize);
            return;
        }

        var existing = new HashSet<string>(_availableFields, StringComparer.OrdinalIgnoreCase);
        int maxRow = 0, maxCol = 0;
        bool hasSpatial = false;
        foreach (var f in _availableFields)
        {
            if (!ParseFieldCoords(f, out int r, out int c)) continue;
            if (r > maxRow) maxRow = r;
            if (c > maxCol) maxCol = c;
            hasSpatial = true;
        }

        int gridRows, gridCols;
        if (hasSpatial && maxRow <= 31 && maxCol <= 31)
        {
            gridRows = 32;
            gridCols = 32;
        }
        else if (hasSpatial)
        {
            gridRows = Math.Max(1, maxRow + 1);
            gridCols = Math.Max(1, maxCol + 1);
        }
        else
        {
            gridCols = Math.Max(1, (int)Math.Sqrt(_availableFields.Length));
            gridRows = Math.Max(1, (_availableFields.Length + gridCols - 1) / gridCols);
        }

        const float BASE_CELL = 44f;
        const float BASE_GAP  = 3f;
        float CELL = BASE_CELL * _browserZoom;
        float GAP  = BASE_GAP  * _browserZoom;

        // Pan com botão do meio ou arrasto com botão esquerdo no fundo
        bool modalOpen = _newMapModalOpen || _requestOpenNewMapModal;
        bool inBrowser = io.MousePos.X >= vpPos.X && io.MousePos.X < vpPos.X + vpSize.X &&
                         io.MousePos.Y >= vpPos.Y && io.MousePos.Y < vpPos.Y + vpSize.Y;
        bool allowNav  = inBrowser && !modalOpen;

        if (allowNav && ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            if (!_browserDragging) { _browserDragging = true; _browserLastMouse = io.MousePos; }
            else { _browserOffset += io.MousePos - _browserLastMouse; _browserLastMouse = io.MousePos; }
        }
        else { _browserDragging = false; }

        // Auto-fit inicial: se zoom padrão não cabe, reduz para caber tudo
        if (_browserZoom == 1.0f && _availableFields.Length > 0)
        {
            float contentW = gridCols * CELL + (gridCols - 1) * GAP;
            float contentH = gridRows * CELL + (gridRows - 1) * GAP;
            float neededW  = contentW + 20f;
            float neededH  = contentH + 40f;
            if (neededW > vpSize.X || neededH > vpSize.Y)
            {
                float fitZoom = Math.Min(vpSize.X / neededW, vpSize.Y / neededH) * 0.95f;
                if (fitZoom < 1.0f)
                {
                    _browserZoom = fitZoom;
                    CELL = BASE_CELL * _browserZoom;
                    GAP  = BASE_GAP  * _browserZoom;
                }
            }
        }

        // Clamp/centralizar offset para manter todo o conteúdo visível
        float contentW2 = gridCols * CELL + (gridCols - 1) * GAP;
        float contentH2 = gridRows * CELL + (gridRows - 1) * GAP;
        float freeX = vpSize.X - 20f - contentW2;
        float freeY = vpSize.Y - 40f - contentH2;
        float minX = Math.Min(0f, freeX);
        float maxX = Math.Max(0f, freeX);
        float minY = Math.Min(0f, freeY);
        float maxY = Math.Max(0f, freeY);
        _browserOffset.X = Math.Clamp(_browserOffset.X, minX, maxX);
        _browserOffset.Y = Math.Clamp(_browserOffset.Y, minY, maxY);
        if (freeX > 0f) _browserOffset.X = freeX * 0.5f;
        if (freeY > 0f) _browserOffset.Y = freeY * 0.5f;

        // Clip ao viewport
        draw.PushClipRect(vpPos + new Vector2(0, 24), vpPos + vpSize, true);

        static void DrawEmptyCell3D(ImDrawListPtr d, Vector2 a, Vector2 b, uint outline)
        {
            float w = b.X - a.X;
            float h = b.Y - a.Y;
            float pad = MathF.Max(3f, MathF.Min(w, h) * 0.12f);
            var p0 = a + new Vector2(pad, pad);
            var p1 = b - new Vector2(pad, pad);

            uint top    = 0xFF15203A;
            uint front  = 0xFF0F172B;
            uint shadow = 0xFF0A1020;

            d.AddRectFilled(p0, p1, front, 4f);
            d.AddRect(p0, p1, outline, 4f);

            float t = MathF.Min(p1.X - p0.X, p1.Y - p0.Y) * 0.18f;
            var tp0 = p0;
            var tp1 = new Vector2(p1.X, p0.Y + t);
            d.AddRectFilled(tp0, tp1, top, 4f);

            var sp0 = new Vector2(p1.X - t, p0.Y + t);
            var sp1 = p1;
            d.AddRectFilled(sp0, sp1, shadow, 4f);

            float cx = (p0.X + p1.X) * 0.5f;
            float cy = (p0.Y + p1.Y) * 0.5f;
            float s  = MathF.Min(p1.X - p0.X, p1.Y - p0.Y) * 0.22f;
            d.AddLine(new Vector2(cx - s, cy), new Vector2(cx + s, cy), outline, 2f);
            d.AddLine(new Vector2(cx, cy - s), new Vector2(cx, cy + s), outline, 2f);
        }

        string? clickedField = null;
        string? dblClickedField = null;
        bool dblClickedEmpty = false;
        bool tooltipHovered = false;

        for (int row = 0; row < gridRows; row++)
        {
            for (int col = 0; col < gridCols; col++)
            {
                string name;
                bool existsField;
                if (hasSpatial)
                {
                    name = $"Field{row:D2}{col:D2}";
                    existsField = existing.Contains(name);
                }
                else
                {
                    int idx = row * gridCols + col;
                    if (idx >= _availableFields.Length) continue;
                    name = _availableFields[idx];
                    existsField = true;
                }

                float cx = vpPos.X + 10f + _browserOffset.X + col * (CELL + GAP);
                float cy = vpPos.Y + 30f + _browserOffset.Y + row * (CELL + GAP);
                if (cx + CELL < vpPos.X || cx > vpPos.X + vpSize.X) continue;
                if (cy + CELL < vpPos.Y || cy > vpPos.Y + vpSize.Y) continue;

                var p0 = new Vector2(cx, cy);
                var p1 = new Vector2(cx + CELL, cy + CELL);

                bool isLoaded = _openTabs.Count > 1 && _openTabs.Contains(name);
                bool isSelected = existsField && name == _selectedField;
                bool hovering = inBrowser && io.MousePos.X >= p0.X && io.MousePos.X < p1.X &&
                                           io.MousePos.Y >= p0.Y && io.MousePos.Y < p1.Y;

                uint border = isLoaded ? 0xFF0088FF :
                              isSelected ? 0xFF00AAFF :
                              hovering ? 0xFF335588 :
                              existsField ? 0xFF1A2040 : 0xFF223048;

                uint bg = existsField ? (isLoaded ? 0xFF0D1E33 : 0xFF0B0B18) : 0xFF070A14;
                if (hovering) bg = existsField ? 0xFF111122 : 0xFF0B1020;

                draw.AddRectFilled(p0, p1, bg, 4f);
                draw.AddRect(p0, p1, border, 4f, ImDrawFlags.None, (isSelected || isLoaded) ? 2f : 1f);

                if (existsField)
                {
                    uint mini = 0x221D2A3Au;
                    for (int gi = 1; gi < 4; gi++)
                    {
                        float gx = p0.X + gi * (CELL / 4f);
                        float gy = p0.Y + gi * (CELL / 4f);
                        draw.AddLine(new Vector2(gx, p0.Y), new Vector2(gx, p1.Y), mini);
                        draw.AddLine(new Vector2(p0.X, gy), new Vector2(p1.X, gy), mini);
                    }

                    if (_browserZoom >= 0.75f || hovering)
                    {
                        draw.AddText(p0 + new Vector2(4, 3), 0xFFDDDDDD, name);
                        draw.AddText(p0 + new Vector2(4, CELL - 16), 0xFF7788AA, $"{row:D2}:{col:D2}");
                    }

                    if (isLoaded)
                        draw.AddRectFilled(p0 + new Vector2(0, CELL - 4), p1, 0xFF22AA44, 3f);
                }
                else
                {
                    DrawEmptyCell3D(draw, p0, p1, border);
                }

                if (hovering && !modalOpen)
                {
                    if (existsField) EnsureBrowserPreview(name);
                    ImGui.SetNextWindowSizeConstraints(
                        new Vector2(280, 0),
                        new Vector2(BrowserPreviewDisplaySize + 80f, 520f));
                    ImGui.BeginTooltip();
                    tooltipHovered |= ImGui.IsWindowHovered();

                    ImGui.Text(name);
                    ImGui.TextDisabled(existsField ? "Existe" : "Vazio");
                    if (existsField && _browserPreviewTerrain != null &&
                        string.Equals(_browserPreviewField, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_browserPreviewTrn != null)
                        {
                            if (!string.IsNullOrWhiteSpace(_browserPreviewTrn.MapName))
                                ImGui.TextDisabled(_browserPreviewTrn.MapName);
                            ImGui.TextDisabled($"EnvPos: X={_browserPreviewTrn.EnvPosX}  Y={_browserPreviewTrn.EnvPosY}");
                            int tpX = _browserPreviewTrn.EnvPosX * 128 + 64;
                            int tpY = _browserPreviewTrn.EnvPosY * 128 + 64;
                            ImGui.TextDisabled($"Teleport: X={tpX}  Y={tpY}");
                            _browserPreviewTerrain.ShowGrid = false;
                            _browserPreviewTerrain.WireFrame = false;
                            ImGui.Spacing();
                            bool showMeshes = _browserPreviewShowMeshes;
                            if (ImGui.Checkbox("Modelos 3D", ref showMeshes))
                                _browserPreviewShowMeshes = showMeshes;
                            int objCount = _browserPreviewDat?.Records.Count ?? 0;
                            if (objCount > 0) ImGui.SameLine();
                            if (objCount > 0) ImGui.TextDisabled($"Objetos: {objCount}");

                            _browserPreviewTerrain.Render(_browserPreviewCam, BrowserPreviewRenderSize, BrowserPreviewRenderSize);
                            if (_browserPreviewShowMeshes && _browserPreviewDat != null && _browserPreviewTerrain.Fbo != 0)
                            {
                                ConfigureBrowserPreviewObjects();
                                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _browserPreviewTerrain.Fbo);
                                GL.Viewport(0, 0, BrowserPreviewRenderSize, BrowserPreviewRenderSize);
                                GL.Disable(EnableCap.ScissorTest);
                                GL.Disable(EnableCap.CullFace);
                                var view = _browserPreviewCam.ViewMatrix;
                                var proj = _browserPreviewCam.ProjectionMatrix(1f);
                                _browserPreviewObjRenderer.Render(view, proj);
                                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                            }

                            GL.Disable(EnableCap.DepthTest);
                            var imgPos = ImGui.GetCursorScreenPos();
                            ImGui.Image(
                                new IntPtr(_browserPreviewTerrain.ColorTexture),
                                new Vector2(BrowserPreviewDisplaySize, BrowserPreviewDisplaySize),
                                new Vector2(0, 1), new Vector2(1, 0));

                            bool previewHovered = ImGui.IsItemHovered();
                            if (previewHovered && io.MouseWheel != 0f)
                                _browserPreviewCam.Zoom(io.MouseWheel);
                            if (previewHovered && ImGui.IsMouseDown(ImGuiMouseButton.Right))
                            {
                                if (!_browserPreviewDragging) { _browserPreviewDragging = true; _browserPreviewLastMouse = io.MousePos; }
                                else
                                {
                                    var delta = io.MousePos - _browserPreviewLastMouse;
                                    _browserPreviewCam.Orbit(-delta.X * 0.35f, -delta.Y * 0.25f);
                                    _browserPreviewLastMouse = io.MousePos;
                                }
                            }
                            else { _browserPreviewDragging = false; }
                        }
                        else
                        {
                            ImGui.Spacing();
                            ImGui.TextDisabled("Carregando previa 3D...");
                        }
                    }
                    if (!existsField) ImGui.TextDisabled("Duplo-clique: criar mapa aqui");
                    ImGui.EndTooltip();

                    if (existsField)
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) clickedField = name;
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) dblClickedField = name;
                    }
                    else
                    {
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            dblClickedField = name;
                            dblClickedEmpty = true;
                        }
                    }
                }
            }
        }

        draw.PopClipRect();

        // Barra de título do browser
        draw.AddRectFilled(vpPos, vpPos + new Vector2(vpSize.X, 24), 0xEE0C0C12);
        int usedCount = existing.Count;
        int totalCount = gridRows * gridCols;
        int emptyCount = Math.Max(0, totalCount - usedCount);
        string title = $"  Mapas: {usedCount}/{totalCount}  (vazios: {emptyCount})  |  scroll = zoom ({_browserZoom:P0})  |  botão do meio = navegar  |  duplo-clique = abrir/criar";
        draw.AddText(vpPos + new Vector2(4, 5), 0xFF669966, title);

        // Botão para resetar zoom+posição
        var resetP = vpPos + new Vector2(vpSize.X - 90f, 2f);
        bool resetHover = inBrowser && io.MousePos.X >= resetP.X && io.MousePos.X < resetP.X + 84 &&
                                       io.MousePos.Y >= resetP.Y && io.MousePos.Y < resetP.Y + 20;
        draw.AddRectFilled(resetP, resetP + new Vector2(84, 20), resetHover ? 0xFF1A4488 : 0xFF0A1830, 3f);
        draw.AddText(resetP + new Vector2(6, 3), resetHover ? 0xFF88CCFF : 0xFF4488BB, "Reset View");
        if (resetHover && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _browserOffset = Vector2.Zero;
            _browserZoom   = 1.0f;  // vai reduzir automaticamente no próximo frame se não couber
        }

        // Invisible button para captura de eventos (cobre tudo)
        ImGui.SetCursorScreenPos(vpPos);
        ImGui.InvisibleButton("##browser", vpSize);

        // Processar cliques
        if (dblClickedField != null)
        {
            if (dblClickedEmpty)
            {
                OpenNewMap(dblClickedField);
            }
            else
            {
                SelectField(dblClickedField);
                LoadCurrentMap();
            }
        }
        else if (clickedField != null) SelectField(clickedField);

        // Zoom com scroll do mouse (zoom em direção ao cursor)
        // Nota: não aplica enquanto o mouse está sobre o tooltip (para scroll controlar a câmera da prévia).
        if (allowNav && !tooltipHovered && io.MouseWheel != 0f)
        {
            float oldZoom = _browserZoom;
            _browserZoom = Math.Clamp(_browserZoom * (1f + io.MouseWheel * 0.12f), 0.15f, 3.0f);
            float scale  = _browserZoom / oldZoom;
            Vector2 mouseLocal = io.MousePos - vpPos;
            _browserOffset = mouseLocal - (mouseLocal - _browserOffset) * scale;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Status bar
    // ────────────────────────────────────────────────────────────────────────

    private void DrawStatusBar(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.28f, 0.12f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 3));
        ImGui.Begin("##status", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);

        string trnName = Path.GetFileName(_state.TrnPath);
        string left = _state.IsLoaded ? $"TRN: {trnName}   Objetos: {_state.Dat?.Records.Count ?? 0}" : "Sem mapa";
        string right = _statusText;

        ImGui.Text(left);
        ImGui.SameLine(size.X - ImGui.CalcTextSize(right).X - 20);
        ImGui.Text(right);

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        ImGui.End();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Ações do editor
    // ────────────────────────────────────────────────────────────────────────

    private void SelectField(string f)
    {
        _selectedField   = f;
        _state.TrnPath   = f + ".trn";
        _state.DatPath   = f + ".dat";
    }

    private void LoadCurrentMap()
    {
        if (_state.TryLoad())
        {
            _terrain?.MarkDirty();
            _sceneDirty = true;
            _tileCache.Configure(_state.EnvFolder, _state.TileNameById);
            _terrain?.SetTileCache(_tileCache);
            string gameFolder = !string.IsNullOrWhiteSpace(_clientRoot)
                ? _clientRoot
                : (Directory.GetParent(_state.EnvFolder)?.FullName ?? _state.EnvFolder);
            if (_state.Dat != null)
            {
                _objPartColors.Clear();
                _objRenderer.Configure(gameFolder, _state.MeshNameById, _state.Dat.Records, _objPartColors);
                // Carregar cores de objetos do arquivo auxiliar .datcolor
                LoadDatColor(_state.DatPath, _state.Dat.Records, _objPartColors);
            }
            string tab = string.IsNullOrWhiteSpace(_selectedField) ? _state.Trn?.MapName ?? "Mapa" : _selectedField;
            if (!_openTabs.Contains(tab)) _openTabs.Add(tab);
            _activeTab  = _openTabs.IndexOf(tab);
            _state.ActiveTool = EditorTool.Select;
            _statusText = $"Carregado: {tab}   Tiles: {_state.Trn?.Tiles.Length}   Objetos: {_state.Dat?.Records.Count}";
        }
        else
        {
            _statusText = "Erro: " + _state.LastError;
        }
    }

    private void TrySave()
    {
        RemapHighObjTypesForClient();
        NormalizeDatScaleTypesForSave();

        if (!string.IsNullOrWhiteSpace(_state.TrnPath) && File.Exists(_state.TrnPath)) BackupIfExists(_state.TrnPath);
        if (!string.IsNullOrWhiteSpace(_state.DatPath) && File.Exists(_state.DatPath)) BackupIfExists(_state.DatPath);
        if (_state.TrySave())
        {
            // Salvar cores de objetos em arquivo auxiliar .datcolor
            if (_state.Dat != null)
                SaveDatColor(_state.DatPath, _state.Dat.Records, _objPartColors);

            if (_patchServerHeightmapOnSave && _state.Trn != null && File.Exists(_serverHeightmapPath))
            {
                if (!TryPatchServerHeightmap(_state.Trn))
                {
                    _statusText = "Salvo, mas erro no heightmap: " + _statusText;
                    return;
                }
            }

            if (_saveAttributeMapOnSave)
            {
                try { SaveAttributeMapEverywhere(saveWorkingCopy: true); }
                catch (Exception ex) { _statusText = "Salvo, mas erro no AttributeMap: " + ex.Message; return; }
            }
            string extra = (_saveAttributeMapOnSave)
                ? $"  AttrMap: work={(_lastAttrMapSavedWorkingCopy ? 1 : 0)} client={_lastAttrMapSavedClientCount} server={(_lastAttrMapSavedServer ? 1 : 0)}"
                : "";
            string scaleExtra = (_lastScaleConvertedCount > 0 || _lastScaleDroppedCount > 0)
                ? $"  Scale: conv={_lastScaleConvertedCount} drop={_lastScaleDroppedCount}"
                : "";
            _statusText = "Salvo com sucesso." + extra + scaleExtra;
        }
        else _statusText = "Erro ao salvar: " + _state.LastError;
    }

    private static bool IsDatScaleType(int objType) => objType >= 501 && objType < 600;

    // ── Persistência de cores/texturas de objetos (.datcolor) ──────────────────
    // Formato: linhas "INDEX R G B [TEXNAME]"
    // TEXNAME é opcional — ausência = sem override de textura.
    // O arquivo fica ao lado do .dat, ex: Field0101.datcolor

    private static void SaveDatColor(string datPath, List<DatRecord> records, Dictionary<int, Dictionary<int, uint>> partColors)
    {
        if (string.IsNullOrWhiteSpace(datPath)) return;
        string colorPath = Path.ChangeExtension(datPath, ".datcolor");
        bool hasAny = records.Any(r => r.HasColorOverride || !string.IsNullOrEmpty(r.TextureOverrideName))
                      || (partColors.Count != 0 && partColors.Values.Any(m => m.Count != 0));
        if (!hasAny)
        {
            if (File.Exists(colorPath)) File.Delete(colorPath);
            return;
        }
        using var sw = new System.IO.StreamWriter(colorPath, false, System.Text.Encoding.ASCII);
        sw.WriteLine("# FoxMapStudio object colors v3");
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            bool hasTex = !string.IsNullOrEmpty(r.TextureOverrideName);
            if (r.HasColorOverride || hasTex)
            {
                byte cr = r.HasColorOverride ? r.ColorR : (byte)255;
                byte cg = r.HasColorOverride ? r.ColorG : (byte)255;
                byte cb = r.HasColorOverride ? r.ColorB : (byte)255;
                if (hasTex)
                    sw.WriteLine($"{i} {cr} {cg} {cb} {r.TextureOverrideName}");
                else
                    sw.WriteLine($"{i} {cr} {cg} {cb}");
            }

            if (partColors.TryGetValue(i, out var map) && map.Count != 0)
            {
                foreach (var kv in map.OrderBy(k => k.Key))
                {
                    uint rgb = kv.Value;
                    byte pr = (byte)(rgb & 0xFF);
                    byte pg = (byte)((rgb >> 8) & 0xFF);
                    byte pb = (byte)((rgb >> 16) & 0xFF);
                    sw.WriteLine($"{i} {kv.Key} {pr} {pg} {pb}");
                }
            }
        }
    }

    private static void LoadDatColor(string datPath, List<DatRecord> records, Dictionary<int, Dictionary<int, uint>> partColors)
    {
        if (string.IsNullOrWhiteSpace(datPath)) return;
        string colorPath = Path.ChangeExtension(datPath, ".datcolor");
        if (!File.Exists(colorPath)) return;
        try
        {
            partColors.Clear();
            foreach (var line in File.ReadLines(colorPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[0], out int idx)) continue;
                if (idx < 0 || idx >= records.Count) continue;

                if (parts.Length >= 5 &&
                    int.TryParse(parts[1], out int part) && part >= 0 &&
                    byte.TryParse(parts[2], out byte pr) &&
                    byte.TryParse(parts[3], out byte pg) &&
                    byte.TryParse(parts[4], out byte pb))
                {
                    if (!partColors.TryGetValue(idx, out var map))
                    {
                        map = new Dictionary<int, uint>();
                        partColors[idx] = map;
                    }
                    map[part] = (uint)(pr | (pg << 8) | (pb << 16));
                    continue;
                }

                if (!byte.TryParse(parts[1], out byte cr)) continue;
                if (!byte.TryParse(parts[2], out byte cg)) continue;
                if (!byte.TryParse(parts[3], out byte cb)) continue;
                var rec = records[idx];
                if (cr != 255 || cg != 255 || cb != 255)
                {
                    rec.HasColorOverride = true;
                    rec.ColorR = cr; rec.ColorG = cg; rec.ColorB = cb;
                }
                if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
                    rec.TextureOverrideName = parts[4];
                records[idx] = rec;
            }
        }
        catch { /* ignora erros de leitura */ }
    }

    private static int FindFreeDatScaleType(HashSet<int> used)
    {
        for (int t = 511; t < 600; t++)
            if (!used.Contains(t)) return t;
        return 0;
    }

    private void NormalizeDatScaleTypesForSave()
    {
        _lastScaleConvertedCount = 0;
        _lastScaleDroppedCount = 0;

        if (_state.Dat == null) return;
        if (_state.Dat.Records.Count == 0) return;

        var used = new HashSet<int>(_state.MeshNameById.Keys);
        for (int i = 0; i < _state.Dat.Records.Count; i++)
            used.Add((int)_state.Dat.Records[i].ObjType);

        string root = ResolveClientRoot();

        for (int i = 0; i < _state.Dat.Records.Count; i++)
        {
            var r = _state.Dat.Records[i];
            if (!r.HasScale) continue;

            int ot = (int)r.ObjType;
            if (IsDatScaleType(ot)) continue;

            if (!_state.MeshNameById.TryGetValue(ot, out var meshPath) || string.IsNullOrWhiteSpace(meshPath))
            {
                r.HasScale = false;
                r.ScaleH = 1f;
                r.ScaleV = 1f;
                _state.Dat.Records[i] = r;
                _lastScaleDroppedCount++;
                continue;
            }

            int existingType = 0;
            foreach (var kv in _state.MeshNameById)
            {
                if (IsDatScaleType(kv.Key) && string.Equals(kv.Value, meshPath, StringComparison.OrdinalIgnoreCase))
                {
                    existingType = kv.Key;
                    break;
                }
            }

            if (existingType != 0)
            {
                r.ObjType = (uint)existingType;
                _state.Dat.Records[i] = r;
                continue;
            }

            int newType = FindFreeDatScaleType(used);
            if (newType == 0)
            {
                r.HasScale = false;
                r.ScaleH = 1f;
                r.ScaleV = 1f;
                _state.Dat.Records[i] = r;
                _lastScaleDroppedCount++;
                continue;
            }

            used.Add(newType);
            _state.MeshNameById[newType] = meshPath;

            if (!string.IsNullOrWhiteSpace(_state.MeshListPath))
                MeshConverter.AppendToMeshList(_state.MeshListPath, newType, meshPath);

            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                string uiMeshList = Path.Combine(root, "UI", "MeshList.txt");
                if (File.Exists(uiMeshList) && !string.Equals(uiMeshList, _state.MeshListPath, StringComparison.OrdinalIgnoreCase))
                    MeshConverter.AppendToMeshList(uiMeshList, newType, meshPath);
            }

            r.ObjType = (uint)newType;
            _state.Dat.Records[i] = r;
            _lastScaleConvertedCount++;
        }
    }

    private void TbToolBtnRestricted(string label, EditorTool tool)
    {
        var prevTool = _state.ActiveTool;
        TbToolBtn(label, tool);

        // Ao mudar para/de AttributeMap, reconstruir overlay porque o alpha
        // dos tiles "Normal" muda (visível quando AttrMap ativo, transparente fora).
        bool nowAttrMap  = _state.ActiveTool == EditorTool.AttributeMap;
        bool wasAttrMap  = prevTool           == EditorTool.AttributeMap;
        if (nowAttrMap != wasAttrMap && _state.AttributeMap != null)
            RebuildAttrOverlay();
    }

    /// <summary>
    /// Busca o AttributeMap.dat automaticamente nos locais padrão do cliente WYD
    /// e carrega se encontrar. Chamado ao ativar a ferramenta AttrMap sem path configurado.
    /// </summary>
    private void TryAutoLoadAttributeMap()
    {
        if (_state.AttributeMap != null) return;

        var candidates = new List<string>();

        // 1. Path já configurado manualmente
        if (!string.IsNullOrWhiteSpace(_state.AttributeMapPath))
            candidates.Add(_state.AttributeMapPath);

        // 2. Locais padrão baseados na raiz do cliente
        string root = ResolveClientRoot();
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            candidates.Add(Path.Combine(root, "AttributeMap.dat"));
            candidates.Add(Path.Combine(root, "Env",  "AttributeMap.dat"));
            candidates.Add(Path.Combine(root, "UI",   "AttributeMap.dat"));
            candidates.Add(Path.Combine(root, "Mesh", "AttributeMap.dat"));
        }

        // 3. Pasta Env diretamente
        if (!string.IsNullOrWhiteSpace(_state.EnvFolder) && Directory.Exists(_state.EnvFolder))
        {
            candidates.Add(Path.Combine(_state.EnvFolder, "AttributeMap.dat"));
            // Pasta pai da Env (raiz do cliente se Env estiver lá dentro)
            string? envParent = Directory.GetParent(_state.EnvFolder)?.FullName;
            if (!string.IsNullOrWhiteSpace(envParent))
                candidates.Add(Path.Combine(envParent, "AttributeMap.dat"));
        }

        // 4. Path do servidor (configuração lateral)
        if (!string.IsNullOrWhiteSpace(_serverAttributeMapPath) && File.Exists(_serverAttributeMapPath))
            candidates.Add(_serverAttributeMapPath);

        string? found = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);

        if (found != null)
        {
            if (_state.LoadAttributeMap(found))
            {
                RebuildAttrOverlay();
                _statusText = $"AttributeMap carregado automaticamente: {Path.GetFileName(found)}";
            }
        }
    }

    private void SaveAttributeMapEverywhere(bool saveWorkingCopy)
    {
        _lastAttrMapSavedClientCount = 0;
        _lastAttrMapSavedServer = false;
        _lastAttrMapSavedWorkingCopy = false;

        if (_state.AttributeMap == null)
        {
            string[] candidates = Array.Empty<string>();
            string root = ResolveClientRoot();
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                candidates = new[]
                {
                    Path.Combine(root, "Env",  "AttributeMap.dat"),
                    Path.Combine(root, "UI",   "AttributeMap.dat"),
                    Path.Combine(root, "Mesh", "AttributeMap.dat"),
                    Path.Combine(root, "AttributeMap.dat"),
                };
            }
            if (!string.IsNullOrWhiteSpace(_state.AttributeMapPath))
                candidates = candidates.Length == 0 ? new[] { _state.AttributeMapPath } : new[] { _state.AttributeMapPath }.Concat(candidates).ToArray();

            string? pick = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(pick))
                _state.LoadAttributeMap(pick);
        }
        if (_state.AttributeMap == null)
            throw new Exception("AttributeMap.dat nao esta carregado. Abra AttrMap e clique em Carregar.");

        if (saveWorkingCopy && !string.IsNullOrWhiteSpace(_state.AttributeMapPath))
        {
            EnsureParentDir(_state.AttributeMapPath);
            BackupIfExists(_state.AttributeMapPath);
            File.WriteAllBytes(_state.AttributeMapPath, _state.AttributeMap.Data);
            _lastAttrMapSavedWorkingCopy = true;
        }

        if (_updateClientAttributeMapOnSave)
            _lastAttrMapSavedClientCount = SaveAttributeMapToClientTargets();

        if (_updateServerAttributeMapOnSave)
            _lastAttrMapSavedServer = SaveAttributeMapToServerTarget();
    }

    private void EnsureBrowserPreview(string field)
    {
        if (string.IsNullOrWhiteSpace(_state.EnvFolder) || !Directory.Exists(_state.EnvFolder)) return;
        if (!string.Equals(_browserPreviewField, field, StringComparison.OrdinalIgnoreCase))
        {
            _browserPreviewField = field;
            _browserPreviewTrn = null;
            _browserPreviewDat = null;
        }

        string trnPath = Path.Combine(_state.EnvFolder, field + ".trn");
        if (File.Exists(trnPath) && _browserPreviewTrn == null &&
            !(_browserPreviewTrnTask != null && string.Equals(_browserPreviewTaskField, field, StringComparison.OrdinalIgnoreCase)))
        {
            _browserPreviewTaskField = field;
            _browserPreviewTrnTask = Task.Run(() =>
            {
                try { return TrnFile.Load(trnPath); }
                catch { return null; }
            });
        }

        string datPath = Path.Combine(_state.EnvFolder, field + ".dat");
        if (File.Exists(datPath) && _browserPreviewDat == null &&
            !(_browserPreviewDatTask != null && string.Equals(_browserPreviewDatTaskField, field, StringComparison.OrdinalIgnoreCase)))
        {
            _browserPreviewDatTaskField = field;
            _browserPreviewDatTask = Task.Run(() =>
            {
                try { return DatFile.Load(datPath); }
                catch { return null; }
            });
        }
    }

    private void ConfigureBrowserPreviewCamera(bool adjustForObjects = false)
    {
        if (_browserPreviewTrn == null) return;

        // Centro no meio do mapa
        float centerH = 4f;
        // Se o TRN está carregado, usar a altura média do mapa para posicionar melhor a câmera
        if (_browserPreviewTrn.Tiles != null && _browserPreviewTrn.Tiles.Length > 0)
        {
            float maxH = 0f, minH = 0f;
            foreach (var t in _browserPreviewTrn.Tiles) { if (t.Height > maxH) maxH = t.Height; if (t.Height < minH) minH = t.Height; }
            centerH = (maxH + minH) * 0.5f * ObjectRenderer.HEIGHT_SCALE;
        }

        // Ajuste extra de altura quando tem objetos (torres, prédios podem ser bem altos)
        if (adjustForObjects && _browserPreviewDat != null && _browserPreviewDat.Records.Count > 0)
        {
            // Heurística: objetos tipo edificio ficam por volta de 40 unidades de altura
            // Sobe a câmera para mostrar mais
            centerH += 15f;
        }

        _browserPreviewCam.Target = new OpenTK.Mathematics.Vector3(64f, centerH, 64f);
        _browserPreviewCam.Yaw = 225f;
        _browserPreviewCam.Pitch = 45f;   // ângulo mais íngreme = mostra mais do mapa
        _browserPreviewCam.Fov = 50f;

        float pitchRad = OpenTK.Mathematics.MathHelper.DegreesToRadians(_browserPreviewCam.Pitch);
        float halfFov  = OpenTK.Mathematics.MathHelper.DegreesToRadians(_browserPreviewCam.Fov * 0.5f);
        float diagHalf = MathF.Sqrt(2f) * 64f; // mapa 128x128 em XZ
        float denom = MathF.Max(0.05f, MathF.Sin(pitchRad) * MathF.Tan(halfFov));
        _browserPreviewCam.Distance = Math.Clamp(diagHalf / denom * 0.55f, 55f, 700f);
    }

    private void ConfigureBrowserPreviewObjects()
    {
        if (_browserPreviewDat == null) return;

        string root = ResolveClientRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        string? meshListPath = (!string.IsNullOrWhiteSpace(_state.MeshListPath) && File.Exists(_state.MeshListPath))
            ? _state.MeshListPath
            : MeshListReader.FindMeshList(root);

        string? commonPath = MeshListReader.FindCommonMeshList(root);
        DateTime meshMtimeUtc = (meshListPath != null && File.Exists(meshListPath))
            ? File.GetLastWriteTimeUtc(meshListPath)
            : default;
        DateTime commonMtimeUtc = (commonPath != null && File.Exists(commonPath))
            ? File.GetLastWriteTimeUtc(commonPath)
            : default;

        bool reload =
            _browserPreviewMeshList == null ||
            !string.Equals(_browserPreviewMeshListRoot, root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_browserPreviewMeshListPath, meshListPath ?? "", StringComparison.OrdinalIgnoreCase) ||
            _browserPreviewMeshListMtimeUtc != meshMtimeUtc ||
            !string.Equals(_browserPreviewCommonMeshListPath, commonPath ?? "", StringComparison.OrdinalIgnoreCase) ||
            _browserPreviewCommonMeshListMtimeUtc != commonMtimeUtc;

        if (reload)
        {
            _browserPreviewMeshListRoot = root;
            _browserPreviewMeshListPath = meshListPath ?? "";
            _browserPreviewMeshListMtimeUtc = meshMtimeUtc;
            _browserPreviewCommonMeshListPath = commonPath ?? "";
            _browserPreviewCommonMeshListMtimeUtc = commonMtimeUtc;
            _browserPreviewMeshList = MeshListReader.LoadMerged(root, meshListPath);
        }

        _browserPreviewMeshList ??= new Dictionary<int, string>();
        _browserPreviewObjRenderer.Configure(root, _browserPreviewMeshList, _browserPreviewDat.Records, null);

        // Reposicionar câmera levando em conta que agora temos objetos
        ConfigureBrowserPreviewCamera(adjustForObjects: true);
    }

    private bool SaveAttributeMapToServerTarget()
    {
        if (_state.AttributeMap == null) return false;
        if (string.IsNullOrWhiteSpace(_serverAttributeMapPath)) return false;

        EnsureParentDir(_serverAttributeMapPath);
        BackupIfExists(_serverAttributeMapPath);
        File.WriteAllBytes(_serverAttributeMapPath, _state.AttributeMap.Data);
        return true;
    }

    private int SaveAttributeMapToClientTargets()
    {
        if (_state.AttributeMap == null) return 0;
        string root = ResolveClientRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;

        var targets = new List<string>();

        AddIfDirExists(targets, Path.Combine(root, "Env", "AttributeMap.dat"));
        AddIfDirExists(targets, Path.Combine(root, "UI", "AttributeMap.dat"));
        AddIfDirExists(targets, Path.Combine(root, "Mesh", "AttributeMap.dat"));

        string rootFile = Path.Combine(root, "AttributeMap.dat");
        if (File.Exists(rootFile) || Directory.Exists(root))
            targets.Add(rootFile);

        if (!string.IsNullOrWhiteSpace(_state.AttributeMapPath))
            targets.Add(_state.AttributeMapPath);

        int count = 0;
        foreach (var p in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            EnsureParentDir(p);
            BackupIfExists(p);
            File.WriteAllBytes(p, _state.AttributeMap.Data);
            count++;
        }
        return count;
    }

    private static void AddIfDirExists(List<string> list, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            list.Add(filePath);
        else if (File.Exists(filePath))
            list.Add(filePath);
    }

    private string ResolveClientRoot()
    {
        if (!string.IsNullOrWhiteSpace(_clientRoot) && Directory.Exists(_clientRoot))
        {
            string cr = _clientRoot;
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(cr));
            if (string.Equals(name, "Env", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "UI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Mesh", StringComparison.OrdinalIgnoreCase))
            {
                string? p = Directory.GetParent(cr)?.FullName;
                if (!string.IsNullOrWhiteSpace(p)) cr = p!;
            }
            if (Directory.Exists(Path.Combine(cr, "Env"))) return cr;
        }
        if (!string.IsNullOrWhiteSpace(_state.EnvFolder) && Directory.Exists(_state.EnvFolder))
            return Directory.GetParent(_state.EnvFolder)?.FullName ?? "";
        return "";
    }

    private static void EnsureParentDir(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static void BackupIfExists(string path)
    {
        if (!File.Exists(path)) return;
        var bak = path + ".bak";
        try { File.Copy(path, bak, overwrite: true); }
        catch { }
    }

    private void RemapHighObjTypesForClient()
    {
        if (_state.Dat == null) return;

        var used = new HashSet<int>(_state.MeshNameById.Keys);
        for (int i = 0; i < _state.Dat.Records.Count; i++)
            used.Add((int)_state.Dat.Records[i].ObjType);

        var remap = new Dictionary<int, int>();

        for (int i = 0; i < _state.Dat.Records.Count; i++)
        {
            var r = _state.Dat.Records[i];
            int oldType = (int)r.ObjType;
            if (oldType < 3048) continue;

            if (!remap.TryGetValue(oldType, out int newType))
            {
                newType = 0;
                for (int id = 2800; id <= 3047; id++)
                {
                    if (!used.Contains(id)) { newType = id; break; }
                }
                if (newType == 0)
                for (int id = 1; id <= 2799; id++)
                {
                    if (!used.Contains(id)) { newType = id; break; }
                }
                if (newType == 0) continue;

                used.Add(newType);
                remap[oldType] = newType;

                if (_state.MeshNameById.TryGetValue(oldType, out var path))
                {
                    _state.MeshNameById[newType] = path;
                    _state.MeshNameById.Remove(oldType);

                    if (!string.IsNullOrWhiteSpace(_state.MeshListPath) && File.Exists(_state.MeshListPath))
                        MeshConverter.ReplaceObjTypeInMeshList(_state.MeshListPath, oldType, newType, path);

                    if (!string.IsNullOrEmpty(_clientRoot))
                    {
                        string uiMeshList = Path.Combine(_clientRoot, "UI", "MeshList.txt");
                        if (File.Exists(uiMeshList))
                            MeshConverter.ReplaceObjTypeInMeshList(uiMeshList, oldType, newType, path);
                    }
                }
            }

            r.ObjType = (uint)newType;
            _state.Dat.Records[i] = r;
        }

        if (remap.Count > 0)
        {
            if (remap.TryGetValue(_addObjType, out int newAdd)) _addObjType = newAdd;
        }
    }

    private void DoUndo()
    {
        if (_state.Undo())
        {
            _terrain?.MarkDirty();
            _sceneDirty = true;
            _attrOverlayDirty = true;
            _statusText = "Desfazer realizado.";
        }
    }

    private void DoRedo()
    {
        if (_state.Redo())
        {
            _terrain?.MarkDirty();
            _sceneDirty = true;
            _attrOverlayDirty = true;
            _statusText = "Refazer realizado.";
        }
    }

    private void DeleteSelectedObject()
    {
        if (_state.Dat == null) return;
        int sel = _state.SelectedObjectIndex;
        if (sel < 0 || sel >= _state.Dat.Records.Count) return;

        var rec = _state.Dat.Records[sel];
        string name = _state.MeshNameById.TryGetValue((int)rec.ObjType, out var meshName)
            ? TextLists.GetMeshDisplayName(meshName)
            : $"ObjType {rec.ObjType}";

        _state.PushUndoObjects();
        _state.Dat.Records.RemoveAt(sel);
        _state.SelectedObjectIndex = Math.Min(sel, _state.Dat.Records.Count - 1);
        _objRenderer.SelectedObjectIndex = _state.SelectedObjectIndex;
        _sceneDirty = true;
        _statusText = $"Deletado: {name} (#{sel})";
    }

    private void CopySelectedObject()
    {
        if (_state.Dat == null) return;
        int sel = _state.SelectedObjectIndex;
        if (sel < 0 || sel >= _state.Dat.Records.Count) return;

        _objClipboard = _state.Dat.Records[sel];
        _hasObjClipboard = true;
        _objClipboardHeightOffset = 0f;

        if (_state.Trn != null)
        {
            int tileX = Math.Clamp((int)(_objClipboard.PosX / TerrainRenderer.TILE_SIZE), 0, 63);
            int tileY = Math.Clamp((int)(_objClipboard.PosY / TerrainRenderer.TILE_SIZE), 0, 63);
            float terrainH = _state.Trn.Tiles[tileX + tileY * 64].Height;
            _objClipboardHeightOffset = _objClipboard.Height - terrainH;
        }

        string name = _state.MeshNameById.TryGetValue((int)_objClipboard.ObjType, out var mn)
            ? TextLists.GetMeshDisplayName(mn) : $"ObjType {_objClipboard.ObjType}";
        _statusText = $"Copiado: {name} (#{sel})";
    }

    private void PasteClipboardObjectAtCamera()
    {
        if (!_hasObjClipboard) return;
        if (_state.Dat == null) return;
        if (_state.Trn == null) return;

        _state.PushUndoObjects();

        float worldX = _cam.Target.X;
        float worldZ = _cam.Target.Z;

        float posX = Math.Clamp(worldX, 0f, 127f);
        float posY = Math.Clamp(worldZ, 0f, 127f);

        int tileX = Math.Clamp((int)(worldX / TerrainRenderer.TILE_SIZE), 0, 63);
        int tileY = Math.Clamp((int)(worldZ / TerrainRenderer.TILE_SIZE), 0, 63);
        float terrainH = _state.Trn.Tiles[tileX + tileY * 64].Height;

        var newRec = _objClipboard;
        newRec.PosX = posX;
        newRec.PosY = posY;
        newRec.Height = terrainH + _objClipboardHeightOffset;

        _state.Dat.Records.Add(newRec);
        int newIdx = _state.Dat.Records.Count - 1;
        _state.SelectedObjectIndex = newIdx;
        _objRenderer.SelectedObjectIndex = newIdx;
        _sceneDirty = true;
        _state.ActiveTool = EditorTool.Move;

        string name = _state.MeshNameById.TryGetValue((int)newRec.ObjType, out var mn)
            ? TextLists.GetMeshDisplayName(mn) : $"ObjType {newRec.ObjType}";
        string notif = $"+ Objeto colado: {name}  (#{newIdx})  pos ({posX:F0}, {posY:F0})";
        ShowNotif(notif, 4f);
        _statusText = notif;
    }

    private void OpenNewMap()
    {
        OpenNewMap(null);
    }

    private void OpenNewMap(string? forceField)
    {
        if (string.IsNullOrWhiteSpace(_state.EnvFolder) || !Directory.Exists(_state.EnvFolder))
        {
            PickClientRoot();
            return;
        }

        _newMapFreeSlots = FindFreeFieldSlots(_state.EnvFolder, 200).ToArray();
        _newMapFreeSlotIndex = 0;
        _newMapAutoSlot = string.IsNullOrWhiteSpace(forceField);
        _newMapCreateMinimap = true;
        _newMapPatchServerHeightmap = File.Exists(_serverHeightmapPath);

        _newMapFieldName = !string.IsNullOrWhiteSpace(forceField)
            ? forceField!
            : (_newMapFreeSlots.Length > 0 ? _newMapFreeSlots[0] : FindNextFieldName(_state.EnvFolder));
        _newMapMapNameTouched = false;
        _newMapMapName = _newMapFieldName;
        if (_newMapFreeSlots.Length > 0)
        {
            int found = Array.FindIndex(_newMapFreeSlots, s => string.Equals(s, _newMapFieldName, StringComparison.OrdinalIgnoreCase));
            _newMapFreeSlotIndex = found >= 0 ? found : 0;
        }

        if (ParseFieldCoords(_newMapFieldName, out int r, out int c))
        {
            _newMapEnvX = Math.Clamp(r, 0, 255);
            _newMapEnvY = Math.Clamp(c, 0, 255);
        }
        else
        {
            _newMapEnvX = 0;
            _newMapEnvY = 0;
        }
        _newMapOverwrite = false;
        _requestOpenNewMapModal = true;
    }

    private void DrawPopupHost()
    {
        ImGui.SetNextWindowPos(new Vector2(-10000, -10000), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(1, 1), ImGuiCond.Always);
        ImGui.Begin("##popups", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                               ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                               ImGuiWindowFlags.NoSavedSettings |
                               ImGuiWindowFlags.NoBackground);
        if (_requestOpenNewMapModal)
        {
            ImGui.OpenPopup("Novo mapa##modal");
            _requestOpenNewMapModal = false;
        }
        DrawNewMapModal();
        ImGui.End();
    }

    private void DrawNewMapModal()
    {
        ImGui.SetNextWindowSize(new Vector2(860, 560), ImGuiCond.Once);
        ImGui.SetNextWindowSizeConstraints(new Vector2(760, 520), new Vector2(1280, 900));
        bool open = true;
        _newMapModalOpen = false;
        if (ImGui.BeginPopupModal("Novo mapa##modal", ref open))
        {
            _newMapModalOpen = true;
            ImGui.TextDisabled("Criar mapa novo (automatico): voce so escolhe o nome.");
            ImGui.Separator();

            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("MapName##newmapname", ref _newMapMapName, 64))
                _newMapMapNameTouched = true;

            ImGui.Spacing();

            bool auto = _newMapAutoSlot;
            if (ImGui.Checkbox("Escolher ID automatico (recomendado)", ref auto))
                _newMapAutoSlot = auto;

            if (_newMapFreeSlots.Length > 0)
            {
                int idx = _newMapFreeSlotIndex;
                ImGui.SetNextItemWidth(220f);
                if (ImGui.Combo("Slots vazios##newmapslots", ref idx, _newMapFreeSlots, _newMapFreeSlots.Length))
                {
                    _newMapFreeSlotIndex = idx;
                    _newMapAutoSlot = true;
                    _newMapFieldName = _newMapFreeSlots[Math.Clamp(_newMapFreeSlotIndex, 0, _newMapFreeSlots.Length - 1)];
                    if (!_newMapMapNameTouched) _newMapMapName = _newMapFieldName;
                }
                ImGui.SameLine();
                if (ImGui.Button("Usar este slot##usefreeslot"))
                {
                    _newMapAutoSlot = true;
                    _newMapFieldName = _newMapFreeSlots[Math.Clamp(_newMapFreeSlotIndex, 0, _newMapFreeSlots.Length - 1)];
                    if (!_newMapMapNameTouched) _newMapMapName = _newMapFieldName;
                }
            }

            if (_newMapAutoSlot)
            {
                if (_newMapFreeSlots.Length > 0)
                {
                    string slot = _newMapFreeSlots[Math.Clamp(_newMapFreeSlotIndex, 0, _newMapFreeSlots.Length - 1)];
                    if (!string.Equals(slot, _newMapFieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        _newMapFieldName = slot;
                        if (!_newMapMapNameTouched) _newMapMapName = _newMapFieldName;
                    }
                }
            }
            else
            {
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("Field##newmapfield", ref _newMapFieldName, 32);
            }

            if (ParseFieldCoords(_newMapFieldName, out int r, out int c))
            {
                _newMapEnvX = Math.Clamp(r, 0, 255);
                _newMapEnvY = Math.Clamp(c, 0, 255);
            }

            ImGui.TextDisabled($"Arquivo: {_newMapFieldName}.trn / {_newMapFieldName}.dat");
            ImGui.TextDisabled($"EnvPos: X={_newMapEnvX}  Y={_newMapEnvY}");

            int tpX = _newMapEnvX * 128 + 64;
            int tpY = _newMapEnvY * 128 + 64;
            ImGui.TextDisabled($"Teleport (centro): X={tpX}  Y={tpY}");

            bool mk = _newMapCreateMinimap;
            if (ImGui.Checkbox("Criar minimapa (UI\\mXXYY.wyt)", ref mk))
                _newMapCreateMinimap = mk;

            bool canHmap = File.Exists(_serverHeightmapPath);
            if (canHmap)
                ImGui.TextDisabled($"Heightmap (server): OK  ({Path.GetFileName(_serverHeightmapPath)})");
            else
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.55f, 1f), "Heightmap (server): nao selecionado");

            if (ImGui.Button("Selecionar heightmap.dat...##pickhmap", new Vector2(-1, 0)))
            {
                if (_pickHeightmapTask == null)
                    _pickHeightmapTask = Dialogs.PickFileAsync(
                        "Heightmap|heightmap.dat;*.dat|Todos|*.*",
                        PickStartDir(_serverRoot),
                        "Selecione o heightmap.dat do servidor");
            }

            bool ow = _newMapOverwrite;
            if (ImGui.Checkbox("Sobrescrever se existir", ref ow))
                _newMapOverwrite = ow;

            ImGui.Spacing();
            bool canCreate = File.Exists(_serverHeightmapPath);
            if (!canCreate) ImGui.BeginDisabled();
            if (ImGui.Button("Criar##newmapok", new Vector2(140, 0)))
            {
                if (TryCreateNewMap(_newMapFieldName, _newMapMapName, _newMapEnvX, _newMapEnvY, _newMapOverwrite, _newMapCreateMinimap, true))
                    ImGui.CloseCurrentPopup();
            }
            if (!canCreate) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancelar##newmapcancel", new Vector2(140, 0)))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private static string FindNextFieldName(string envFolder)
    {
        var existing = new HashSet<string>(EditorState.ScanFields(envFolder), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= 9999; i++)
        {
            string n = $"Field{i:0000}";
            if (!existing.Contains(n))
                return n;
        }
        return "Field0000";
    }

    private static List<string> FindFreeFieldSlots(string envFolder, int max)
    {
        var existing = new HashSet<string>(EditorState.ScanFields(envFolder), StringComparer.OrdinalIgnoreCase);
        var list = new List<string>(Math.Min(max, 256));
        for (int i = 0; i <= 9999 && list.Count < max; i++)
        {
            string n = $"Field{i:0000}";
            if (!existing.Contains(n))
                list.Add(n);
        }
        return list;
    }

    private static bool IsAsciiPrintable(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 32 || c > 126) return false;
        }
        return true;
    }

    private bool TryCreateNewMap(string fieldName, string mapName, int envX, int envY, bool overwrite, bool createMinimap, bool patchHeightmap)
    {
        if (string.IsNullOrWhiteSpace(_state.EnvFolder) || !Directory.Exists(_state.EnvFolder))
        {
            _statusText = "EnvFolder nao configurado.";
            return false;
        }
        if (!File.Exists(_serverHeightmapPath))
        {
            _statusText = "Selecione o heightmap.dat do servidor antes de criar o mapa.";
            return false;
        }
        if (!TryGetHeightmapSize(out int hmSize, out int hmCells))
            return false;
        int maxEnv = hmCells - 1;
        if (envX < 0 || envY < 0 || envX > maxEnv || envY > maxEnv)
        {
            _statusText = $"EnvPos fora do limite (0..{maxEnv}). Escolha outro slot.";
            return false;
        }
        fieldName = fieldName.Trim();
        mapName = string.IsNullOrWhiteSpace(mapName) ? fieldName : mapName.Trim();

        if (!fieldName.StartsWith("Field", StringComparison.OrdinalIgnoreCase))
        {
            _statusText = "Nome deve comecar com 'Field'.";
            return false;
        }
        if (!IsAsciiPrintable(mapName))
        {
            _statusText = "Nome do mapa deve ser ASCII sem acento (ex: 'MapaVip1').";
            return false;
        }

        string trnPath = Path.Combine(_state.EnvFolder, fieldName + ".trn");
        string datPath = Path.Combine(_state.EnvFolder, fieldName + ".dat");
        bool existedTrn = File.Exists(trnPath);
        bool existedDat = File.Exists(datPath);
        if (!overwrite && (File.Exists(trnPath) || File.Exists(datPath)))
        {
            _statusText = "Ja existe. Marque sobrescrever ou escolha outro nome.";
            return false;
        }

        try
        {
            var trn = new TrnFile
            {
                MapName = mapName,
                EnvPosX = (byte)Math.Clamp(envX, 0, 255),
                EnvPosY = (byte)Math.Clamp(envY, 0, 255),
            };
            trn.Save(trnPath);
            new DatFile().Save(datPath);

            string? minimapPath = null;
            _availableFields = EditorState.ScanFields(_state.EnvFolder);
            SelectField(fieldName);
            LoadCurrentMap();
            if (createMinimap)
                minimapPath = TryCreateMinimapWyt(trn);
            if (patchHeightmap)
            {
                bool okH = TryPatchServerHeightmap(trn);
                if (!okH)
                {
                    if (!existedTrn && File.Exists(trnPath)) File.Delete(trnPath);
                    if (!existedDat && File.Exists(datPath)) File.Delete(datPath);
                    if (!string.IsNullOrWhiteSpace(minimapPath) && File.Exists(minimapPath)) File.Delete(minimapPath);
                    _statusText = "Falha ao atualizar heightmap.dat. Mapa nao foi criado.";
                    return false;
                }
            }

            int tpX = trn.EnvPosX * 128 + 64;
            int tpY = trn.EnvPosY * 128 + 64;
            string ok = $"Criado: {fieldName}  EnvPos({trn.EnvPosX},{trn.EnvPosY})  Teleport X={tpX} Y={tpY}";
            _statusText = ok;
            ShowNotif(ok, 6f);
            return true;
        }
        catch (Exception ex)
        {
            _statusText = "Erro ao criar mapa: " + ex.Message;
            return false;
        }
    }

    private string? TryCreateMinimapWyt(TrnFile trn)
    {
        try
        {
            string env = _state.EnvFolder;
            if (string.IsNullOrWhiteSpace(env) || !Directory.Exists(env)) return null;
            string? root = null;
            var dirName = new DirectoryInfo(env).Name;
            if (dirName.Equals("Env", StringComparison.OrdinalIgnoreCase))
                root = Directory.GetParent(env)?.FullName;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                root = _clientRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

            string ui = Path.Combine(root, "UI");
            Directory.CreateDirectory(ui);

            int x = trn.EnvPosX;
            int y = trn.EnvPosY;
            string fn = $"m{x:00}{y:00}.wyt";
            string path = Path.Combine(ui, fn);

            var rgba = new byte[128 * 128 * 4];
            for (int ty = 0; ty < 64; ty++)
            {
                for (int tx = 0; tx < 64; tx++)
                {
                    var tile = trn.Tiles[tx + ty * 64];
                    var c = TerrainRenderer.TileColor(tile.TileIndex);
                    byte r = (byte)(c.X * 255);
                    byte g = (byte)(c.Y * 255);
                    byte b = (byte)(c.Z * 255);
                    int px = tx * 2;
                    int py = ty * 2;
                    for (int yy = 0; yy < 2; yy++)
                    for (int xx = 0; xx < 2; xx++)
                    {
                        int o = ((py + yy) * 128 + (px + xx)) * 4;
                        rgba[o] = r;
                        rgba[o + 1] = g;
                        rgba[o + 2] = b;
                        rgba[o + 3] = 255;
                    }
                }
            }

            WytWriter.Save(path, rgba, 128, 128);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private void PatchServerHeightmapFromCurrent()
    {
        if (_state.Trn == null) return;
        if (TryPatchServerHeightmap(_state.Trn))
            ShowNotif("heightmap.dat atualizado. Reinicie o servidor.", 5f);
    }

    private bool TryGetHeightmapSize(out int size, out int envCells)
    {
        size = 0;
        envCells = 0;

        string path = _serverHeightmapPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _statusText = "heightmap.dat nao configurado/encontrado.";
            return false;
        }

        long len = new FileInfo(path).Length;
        if (len <= 0)
        {
            _statusText = "heightmap.dat vazio.";
            return false;
        }

        double root = Math.Sqrt(len);
        long s = (long)Math.Round(root);
        if (s * s != len)
        {
            _statusText = "heightmap.dat com tamanho invalido (nao e quadrado).";
            return false;
        }

        if (s < 256 || s > 16384)
        {
            _statusText = "heightmap.dat com tamanho fora do esperado.";
            return false;
        }

        if ((s % 128) != 0)
        {
            _statusText = "heightmap.dat invalido (tamanho nao multiplo de 128).";
            return false;
        }

        size = (int)s;
        envCells = size / 128;
        return true;
    }

    private bool TryPatchServerHeightmap(TrnFile trn)
    {
        try
        {
            string path = _serverHeightmapPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _statusText = "heightmap.dat nao configurado/encontrado.";
                return false;
            }

            if (!TryGetHeightmapSize(out int S, out _))
                return false;

            string bak = path + ".bak";
            if (!File.Exists(bak))
                File.Copy(path, bak);

            byte[] data = File.ReadAllBytes(path);

            int baseX = trn.EnvPosX * 128;
            int baseY = trn.EnvPosY * 128;
            if (baseX < 0 || baseY < 0 || baseX + 127 >= S || baseY + 127 >= S)
            {
                _statusText = "EnvPos fora do range do heightmap.";
                return false;
            }

            for (int ty = 0; ty < 64; ty++)
            {
                for (int tx = 0; tx < 64; tx++)
                {
                    sbyte h = trn.Tiles[tx + ty * 64].Height;
                    if (h == 127) h = 126;
                    byte hb = unchecked((byte)h);
                    int gx0 = baseX + tx * 2;
                    int gy0 = baseY + ty * 2;
                    int o0 = (gy0 * S + gx0);
                    int o1 = o0 + 1;
                    int o2 = o0 + S;
                    int o3 = o2 + 1;
                    data[o0] = hb;
                    data[o1] = hb;
                    data[o2] = hb;
                    data[o3] = hb;
                }
            }

            File.WriteAllBytes(path, data);
            _statusText = $"heightmap.dat atualizado: EnvPos({trn.EnvPosX},{trn.EnvPosY})";
            return true;
        }
        catch (Exception ex)
        {
            _statusText = "Erro ao atualizar heightmap: " + ex.Message;
            return false;
        }
    }

    private void PickClientRoot()
    {
        if (_pickClientTask == null)
            _pickClientTask = Dialogs.PickFolderAsync(_clientRoot, "Selecione a pasta raiz do cliente WYD");
    }

    private void AutoConfigure(string root)
    {
        if (!Directory.Exists(root)) return;
        var env = Path.Combine(root, "Env");
        if (Directory.Exists(env)) _state.EnvFolder = env;

        var meshTxt = Path.Combine(root, "MeshList.txt");
        if (!File.Exists(meshTxt)) meshTxt = Path.Combine(root, "Mesh", "MeshList.txt");
        if (File.Exists(meshTxt)) _state.MeshListPath = meshTxt;

        var etxt = Path.Combine(root, "Env", "EnvTextureList3.txt");
        if (File.Exists(etxt)) _state.EnvTextureListPath = etxt;
        else { var ebin = Path.Combine(root, "Env", "EnvTextureList3.bin"); if (File.Exists(ebin)) _state.EnvTextureListPath = ebin; }

        // AttributeMap.dat — na raiz do cliente ou na pasta Env
        var attrDat = Path.Combine(root, "AttributeMap.dat");
        if (!File.Exists(attrDat)) attrDat = Path.Combine(root, "Env", "AttributeMap.dat");
        if (File.Exists(attrDat)) _state.AttributeMapPath = attrDat;

        _availableFields = Array.Empty<string>();
        StartFieldScan();
    }

    private void StartFieldScan()
    {
        var env = _state.EnvFolder;
        if (string.IsNullOrWhiteSpace(env) || !Directory.Exists(env)) return;
        _statusText   = "Escaneando mapas...";
        _scanTask = Task.Run(() => EditorState.ScanFields(env));
    }

    private void HandleKeyboardShortcuts()
    {
        var kb = KeyboardState;
        bool ctrl = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
        bool shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);

        if (ctrl && kb.IsKeyPressed(Keys.Z)) { if (shift) DoRedo(); else DoUndo(); }
        if (ctrl && kb.IsKeyPressed(Keys.Y)) DoRedo();
        if (ctrl && kb.IsKeyPressed(Keys.S)) TrySave();

        if (!ImGui.GetIO().WantCaptureKeyboard)
        {
            if (ctrl && kb.IsKeyPressed(Keys.C)) CopySelectedObject();
            if (ctrl && kb.IsKeyPressed(Keys.V)) PasteClipboardObjectAtCamera();
        }

        // Atalhos de ferramenta
        if (!ctrl && kb.IsKeyPressed(Keys.Q)) _state.ActiveTool = EditorTool.Select;
        if (!ctrl && kb.IsKeyPressed(Keys.W)) _state.ActiveTool = EditorTool.Move;
        if (!ctrl && kb.IsKeyPressed(Keys.E)) _state.ActiveTool = EditorTool.Rotate;
        if (!ctrl && kb.IsKeyPressed(Keys.R)) _state.ActiveTool = EditorTool.Scale;
        if (!ctrl && kb.IsKeyPressed(Keys.T)) _state.ActiveTool = EditorTool.Level;
        if (!ctrl && kb.IsKeyPressed(Keys.P)) _state.ActiveTool = EditorTool.PaintTexture;
        EnforceToolAccess();

        // Escape: limpa seleção por área
        if (kb.IsKeyPressed(Keys.Escape))
        {
            _objRenderer.AreaSelectedObjects.Clear();
            _state.SelectedObjectIndex = -1;
            _sceneDirty = true;
        }

        // Del: deleta objeto selecionado (só quando ImGui não está capturando teclado)
        if (!ImGui.GetIO().WantCaptureKeyboard && kb.IsKeyPressed(Keys.Delete))
            DeleteSelectedObject();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Pump tarefas async
    // ────────────────────────────────────────────────────────────────────────

    private void PumpAsync()
    {
        if (_browserPreviewTrnTask?.IsCompleted == true)
        {
            var trn = SafeGet(_browserPreviewTrnTask);
            var field = _browserPreviewTaskField;
            _browserPreviewTrnTask = null;
            _browserPreviewTaskField = "";
            if (!string.IsNullOrWhiteSpace(field) &&
                string.Equals(field, _browserPreviewField, StringComparison.OrdinalIgnoreCase))
            {
                _browserPreviewTrn = trn;
                if (_browserPreviewTerrain != null && trn != null)
                {
                    _browserPreviewTerrain.SetTrn(trn);

                    // Configurar tile cache para a prévia
                    if (_state.TileNameById.Count > 0)
                    {
                        // Mapa já carregado no editor → reutiliza o cache existente
                        _browserPreviewTerrain.SetTileCache(_tileCache);
                    }
                    else if (!string.IsNullOrWhiteSpace(_state.EnvFolder) && Directory.Exists(_state.EnvFolder))
                    {
                        // Sem mapa carregado → busca o arquivo de textura no EnvFolder e configura o cache
                        string envFolder = _state.EnvFolder;
                        string? texListPath =
                            (!string.IsNullOrWhiteSpace(_state.EnvTextureListPath) && File.Exists(_state.EnvTextureListPath))
                            ? _state.EnvTextureListPath
                            : TileTextureCache.FindEnvTextureList(envFolder);

                        if (texListPath != null)
                        {
                            // LoadTileNameById é internal em EditorState; carregamos via TileTextureCache
                            _tileCache.Configure(envFolder, texListPath);
                            _browserPreviewTerrain.SetTileCache(_tileCache);
                        }
                    }
                }
                ConfigureBrowserPreviewCamera();
            }
        }

        if (_browserPreviewDatTask?.IsCompleted == true)
        {
            var dat = SafeGet(_browserPreviewDatTask);
            var field = _browserPreviewDatTaskField;
            _browserPreviewDatTask = null;
            _browserPreviewDatTaskField = "";
            if (!string.IsNullOrWhiteSpace(field) &&
                string.Equals(field, _browserPreviewField, StringComparison.OrdinalIgnoreCase))
            {
                _browserPreviewDat = dat;
                ConfigureBrowserPreviewObjects();
            }
        }

        if (_pickClientTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickClientTask); _pickClientTask = null;
            if (!string.IsNullOrWhiteSpace(r)) { _clientRoot = r; AutoConfigure(r); }
        }
        if (_pickServerTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickServerTask); _pickServerTask = null;
            if (!string.IsNullOrWhiteSpace(r)) _serverRoot = r;
        }
        if (_pickHeightmapTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickHeightmapTask); _pickHeightmapTask = null;
            if (!string.IsNullOrWhiteSpace(r)) _serverHeightmapPath = r;
        }
        if (_pickServerAttrTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickServerAttrTask); _pickServerAttrTask = null;
            if (!string.IsNullOrWhiteSpace(r)) _serverAttributeMapPath = r;
        }
        if (_pickEnvTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickEnvTask); _pickEnvTask = null;
            if (!string.IsNullOrWhiteSpace(r)) { _state.EnvFolder = r; _availableFields = Array.Empty<string>(); StartFieldScan(); }
        }
        if (_pickTrnTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickTrnTask); _pickTrnTask = null;
            if (!string.IsNullOrWhiteSpace(r)) _state.TrnPath = r;
        }
        if (_pickDatTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickDatTask); _pickDatTask = null;
            if (!string.IsNullOrWhiteSpace(r)) _state.DatPath = r;
        }
        if (_pickMeshTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickMeshTask); _pickMeshTask = null;
            if (!string.IsNullOrWhiteSpace(r)) _state.MeshListPath = r;
        }
        if (_pickEnvTexTask?.IsCompleted == true)
        {
            var r = SafeGet(_pickEnvTexTask); _pickEnvTexTask = null;
            if (!string.IsNullOrWhiteSpace(r)) _state.EnvTextureListPath = r;
        }
        if (_pickExportFolderTask?.IsCompleted == true)
        {
            var dest = SafeGet(_pickExportFolderTask); _pickExportFolderTask = null;
            if (!string.IsNullOrWhiteSpace(dest) && Directory.Exists(dest) &&
                !string.IsNullOrWhiteSpace(_exportFieldPending) &&
                Directory.Exists(_state.EnvFolder))
            {
                string srcTrn = Path.Combine(_state.EnvFolder, _exportFieldPending + ".trn");
                string srcDat = Path.Combine(_state.EnvFolder, _exportFieldPending + ".dat");
                string dstTrn = Path.Combine(dest, _exportFieldPending + ".trn");
                string dstDat = Path.Combine(dest, _exportFieldPending + ".dat");
                if (File.Exists(srcTrn)) File.Copy(srcTrn, dstTrn, true);
                if (File.Exists(srcDat)) File.Copy(srcDat, dstDat, true);
                _statusText = $"Exportado: {_exportFieldPending} para {dest}";
            }
            _exportFieldPending = "";
        }
        if (_pickTextureTask?.IsCompleted == true)
        {
            var picked = SafeGet(_pickTextureTask); _pickTextureTask = null;
            if (!string.IsNullOrWhiteSpace(picked) && Directory.Exists(_state.EnvFolder))
            {
                string destDir = Path.Combine(_state.EnvFolder, "Texture");
                Directory.CreateDirectory(destDir);
                string dest = Path.Combine(destDir, Path.GetFileName(picked));
                if (!File.Exists(dest))
                {
                    File.Copy(picked, dest);
                    _statusText = $"Textura copiada para: {dest}";
                }
                else
                {
                    _statusText = $"Ja existe: {dest}";
                }
                ForceTextureScan();
                _bottomTab = BottomTab.Textures;
            }
        }
        if (_scanTask?.IsCompleted == true)
        {
            var r = SafeGet(_scanTask); _scanTask = null;
            _availableFields = r ?? Array.Empty<string>();
            _statusText = _availableFields.Length > 0
                ? $"{_availableFields.Length} mapas encontrados."
                : "Nenhum mapa encontrado na pasta Env.";
            if (_availableFields.Length > 0 && string.IsNullOrWhiteSpace(_selectedField))
                SelectField(_availableFields[0]);
        }
        // File picker de objeto externo (aberto fora do modal também)
        if (_pickObjFileTask?.IsCompleted == true)
        {
            string? picked = SafeGet(_pickObjFileTask); _pickObjFileTask = null;
            if (!string.IsNullOrEmpty(picked)) ResolveAndAddObjectFile(picked);
        }
    }

    private T? SafeGet<T>(Task<T> t)
    {
        try { return t.Result; }
        catch (Exception e) { _statusText = e.Message; return default; }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Helpers de UI
    // ────────────────────────────────────────────────────────────────────────

    private bool ToolBtn(string label, bool active, bool disabled = false)
    {
        if (disabled) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.35f);
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.00f, 0.40f, 0.80f, 1f));
        bool clicked = ImGui.Button(label + "##tb") && !disabled;
        if (active) ImGui.PopStyleColor();
        if (disabled) ImGui.PopStyleVar();
        ImGui.SameLine();
        return clicked;
    }

    private void ToolToggle(string label, EditorTool tool)
    {
        bool active = _state.ActiveTool == tool;
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.00f, 0.40f, 0.80f, 1f));
        if (ImGui.Button(label + "##tb")) _state.ActiveTool = tool;
        if (active) ImGui.PopStyleColor();
        ImGui.SameLine();
    }

    private void TabToggle(string label, BottomTab tab, bool disabled)
    {
        bool active = _bottomTab == tab;
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.00f, 0.40f, 0.80f, 1f));
        if (disabled) ImGui.BeginDisabled();
        if (ImGui.Button(label + "##btab") && !disabled)
        {
            _bottomTab = tab;
            if (tab == BottomTab.Textures)
            {
                _showTexturesModal = true;
                ImGui.OpenPopup("Texturas##modal");
            }
        }
        if (disabled)
        {
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
            }
            ImGui.EndDisabled();
        }
        if (active) ImGui.PopStyleColor();
    }

    private static void Separator3D()
    {
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
    }

    private static void SmallLabel(string t)
    {
        ImGui.TextDisabled(t);
    }

    private static void TreeLeaf(string label)
    {
        ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
    }

    private static void KV(string key, string val)
    {
        ImGui.TextDisabled(key + ":");
        ImGui.SameLine(90);
        ImGui.Text(val);
    }

    // ── Cor RGBA para tile index (uint ABGR para ImGui) ─────────────────────
    private static uint TileImguiColor(int id)
    {
        uint x = (uint)id;
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        byte r = (byte)(70 + (x & 0x7F));
        byte g = (byte)(70 + ((x >> 8) & 0x7F));
        byte b = (byte)(70 + ((x >> 16) & 0x7F));
        if (id == 0) { r = 50; g = 130; b = 60; }
        if (id == 2) { r = 180; g = 160; b = 100; }
        if (id == 4) { r = 50; g = 90; b = 180; }
        return 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    private static uint ImGuiColor(float r, float g, float b) =>
        0xFF000000u | ((uint)(b * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(r * 255);

    // ────────────────────────────────────────────────────────────────────────
    //  AttributeMap helpers
    // ────────────────────────────────────────────────────────────────────────

    private static string GetAttrName(byte attr)
    {
        foreach (var (mask, name, _) in AttributeMapFile.AttributeTypes)
            if (attr == mask) return name;

        if (attr == 0) return "Normal";

        var parts = new List<string>(4);
        foreach (var (mask, name, _) in AttributeMapFile.AttributeTypes)
        {
            if (mask == 0) continue;
            if ((attr & mask) != 0) parts.Add(name);
        }
        return parts.Count > 0 ? string.Join(" + ", parts) : $"attr={attr}";
    }

    private void RebuildAttrOverlay()
    {
        if (_state.AttributeMap == null) return;

        const int S = 64;
        int ex = _state.Trn != null ? _state.Trn.EnvPosX : 0;
        int ey = _state.Trn != null ? _state.Trn.EnvPosY : 0;

        // Quando a ferramenta AttributeMap está ativa, mostra tiles "Normal" com alpha baixo
        // para que o usuário tenha feedback visual que o overlay está funcionando.
        // Sem isso, todos os tiles Normal (alpha=0) ficam invisíveis e parece que não carregou.
        bool showNormal = _state.ActiveTool == EditorTool.AttributeMap;
        var pixels = BuildAttrOverlayPixels(ex, ey, S, showNormal);

        if (_attrOverlayTex == 0)
        {
            _attrOverlayTex = GL.GenTexture();
            GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, _attrOverlayTex);
            GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D,
                OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMinFilter,
                (int)OpenTK.Graphics.OpenGL4.TextureMinFilter.Nearest);
            GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D,
                OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMagFilter,
                (int)OpenTK.Graphics.OpenGL4.TextureMagFilter.Nearest);
        }
        else
        {
            GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, _attrOverlayTex);
        }

        GL.TexImage2D(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0,
            OpenTK.Graphics.OpenGL4.PixelInternalFormat.Rgba8,
            S, S, 0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
            OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, pixels);
        GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Gera os pixels RGBA8 do overlay do AttributeMap para o campo atual.
    /// Quando showNormal=true, tiles com attr=0 (Normal) recebem alpha=25
    /// em vez de 0, tornando-os levemente visíveis como grade verde tênue.
    /// </summary>
    private byte[] BuildAttrOverlayPixels(int envPosX, int envPosY, int S, bool showNormal)
    {
        if (_state.AttributeMap == null) return Array.Empty<byte>();

        var pixels = new byte[S * S * 4];
        float baseX = envPosX << 7;
        float baseY = envPosY << 7;

        for (int oy = 0; oy < S; oy++)
        {
            for (int ox = 0; ox < S; ox++)
            {
                float lx = (ox + 0.5f) / S * AttributeMapFile.FIELD_SIZE;
                float ly = (oy + 0.5f) / S * AttributeMapFile.FIELD_SIZE;
                byte attr = _state.AttributeMap.GetAtWorldGlobal(baseX + lx, baseY + ly);

                uint col = AttributeMapFile.AttrToColor(attr);
                int off = (oy * S + ox) * 4;
                pixels[off]     = (byte)(col & 0xFF);
                pixels[off + 1] = (byte)((col >> 8) & 0xFF);
                pixels[off + 2] = (byte)((col >> 16) & 0xFF);
                // Normal (attr=0): invisível se showNormal=false; levemente verde se showNormal=true
                pixels[off + 3] = (attr == 0) ? (showNormal ? (byte)25 : (byte)0) : (byte)180;
            }
        }

        return pixels;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Estilo ImGui (tema escuro WYD)
    // ────────────────────────────────────────────────────────────────────────

    private static void ApplyImGuiStyle()
    {
        var style = ImGui.GetStyle();

        // ── Geometria — cantos bem arredondados, look moderno ────────────────
        style.WindowRounding    = 8f;
        style.ChildRounding     = 6f;
        style.FrameRounding     = 6f;
        style.GrabRounding      = 6f;
        style.ScrollbarRounding = 8f;
        style.PopupRounding     = 6f;
        style.TabRounding       = 6f;
        style.WindowBorderSize  = 0f;   // sem borda de janela (parece mais leve)
        style.FrameBorderSize   = 0f;
        style.ChildBorderSize   = 0f;
        style.PopupBorderSize   = 1f;

        // ── Espaçamento ──────────────────────────────────────────────────────
        style.WindowPadding     = new Vector2(10, 8);
        style.FramePadding      = new Vector2(7, 4);
        style.ItemSpacing       = new Vector2(7, 5);
        style.ItemInnerSpacing  = new Vector2(5, 4);
        style.IndentSpacing     = 16f;
        style.ScrollbarSize     = 9f;
        style.GrabMinSize       = 8f;

        // ── Paleta FoxMap Studio ──────────────────────────────────────────────
        // Base: azul-marinho profundo (quase preto)
        // Acento: azul elétrico  #0077FF
        // Hover:  azul brilhante #0099FF
        // Active: ciano  #00BBFF
        var c = style.Colors;

        // Fundos
        c[(int)ImGuiCol.WindowBg]             = new Vector4(0.047f, 0.043f, 0.090f, 1.00f);
        c[(int)ImGuiCol.ChildBg]              = new Vector4(0.033f, 0.030f, 0.065f, 1.00f);
        c[(int)ImGuiCol.PopupBg]              = new Vector4(0.060f, 0.055f, 0.115f, 0.98f);

        // Bordas
        c[(int)ImGuiCol.Border]               = new Vector4(0.06f, 0.20f, 0.44f, 0.50f);
        c[(int)ImGuiCol.BorderShadow]         = new Vector4(0f, 0f, 0f, 0f);

        // Inputs / frames
        c[(int)ImGuiCol.FrameBg]              = new Vector4(0.07f, 0.065f, 0.140f, 1.00f);
        c[(int)ImGuiCol.FrameBgHovered]       = new Vector4(0.00f, 0.22f, 0.46f, 0.70f);
        c[(int)ImGuiCol.FrameBgActive]        = new Vector4(0.00f, 0.32f, 0.65f, 1.00f);

        // Barra de título
        c[(int)ImGuiCol.TitleBg]              = new Vector4(0.033f, 0.030f, 0.065f, 1.00f);
        c[(int)ImGuiCol.TitleBgActive]        = new Vector4(0.04f,  0.13f,  0.28f,  1.00f);
        c[(int)ImGuiCol.TitleBgCollapsed]     = new Vector4(0.03f,  0.05f,  0.12f,  0.75f);
        c[(int)ImGuiCol.MenuBarBg]            = new Vector4(0.033f, 0.030f, 0.065f, 1.00f);

        // Scrollbar — fina e sutil
        c[(int)ImGuiCol.ScrollbarBg]          = new Vector4(0.020f, 0.018f, 0.040f, 0.60f);
        c[(int)ImGuiCol.ScrollbarGrab]        = new Vector4(0.00f,  0.28f,  0.58f,  0.80f);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.00f,  0.42f,  0.80f,  1.00f);
        c[(int)ImGuiCol.ScrollbarGrabActive]  = new Vector4(0.00f,  0.58f,  1.00f,  1.00f);

        // Checkmark / slider
        c[(int)ImGuiCol.CheckMark]            = new Vector4(0.00f, 0.75f, 1.00f, 1.00f);
        c[(int)ImGuiCol.SliderGrab]           = new Vector4(0.00f, 0.48f, 0.88f, 1.00f);
        c[(int)ImGuiCol.SliderGrabActive]     = new Vector4(0.00f, 0.65f, 1.00f, 1.00f);

        // Botões — base escura discreta, acento no hover/active
        c[(int)ImGuiCol.Button]               = new Vector4(0.06f, 0.10f, 0.22f, 1.00f);
        c[(int)ImGuiCol.ButtonHovered]        = new Vector4(0.00f, 0.35f, 0.72f, 1.00f);
        c[(int)ImGuiCol.ButtonActive]         = new Vector4(0.00f, 0.50f, 1.00f, 1.00f);

        // Headers (TreeNode, CollapsingHeader, Selectable)
        c[(int)ImGuiCol.Header]               = new Vector4(0.05f, 0.16f, 0.34f, 1.00f);
        c[(int)ImGuiCol.HeaderHovered]        = new Vector4(0.00f, 0.35f, 0.72f, 1.00f);
        c[(int)ImGuiCol.HeaderActive]         = new Vector4(0.00f, 0.48f, 0.95f, 1.00f);

        // Separadores
        c[(int)ImGuiCol.Separator]            = new Vector4(0.06f, 0.20f, 0.42f, 0.70f);
        c[(int)ImGuiCol.SeparatorHovered]     = new Vector4(0.00f, 0.50f, 1.00f, 0.90f);
        c[(int)ImGuiCol.SeparatorActive]      = new Vector4(0.00f, 0.65f, 1.00f, 1.00f);

        // Resize grip
        c[(int)ImGuiCol.ResizeGrip]           = new Vector4(0.00f, 0.35f, 0.75f, 0.40f);
        c[(int)ImGuiCol.ResizeGripHovered]    = new Vector4(0.00f, 0.50f, 1.00f, 0.75f);
        c[(int)ImGuiCol.ResizeGripActive]     = new Vector4(0.00f, 0.65f, 1.00f, 1.00f);

        // Tabs — aba ativa tem destaque azul, inativas são muito discretas
        c[(int)ImGuiCol.Tab]                  = new Vector4(0.040f, 0.060f, 0.125f, 1.00f);
        c[(int)ImGuiCol.TabHovered]           = new Vector4(0.00f,  0.35f,  0.72f,  1.00f);
        c[(int)ImGuiCol.TabActive]            = new Vector4(0.04f,  0.20f,  0.44f,  1.00f);
        c[(int)ImGuiCol.TabUnfocused]         = new Vector4(0.033f, 0.040f, 0.090f, 1.00f);
        c[(int)ImGuiCol.TabUnfocusedActive]   = new Vector4(0.045f, 0.120f, 0.260f, 1.00f);

        // Texto
        c[(int)ImGuiCol.Text]                 = new Vector4(0.86f, 0.92f, 1.00f, 1.00f);
        c[(int)ImGuiCol.TextDisabled]         = new Vector4(0.30f, 0.40f, 0.56f, 1.00f);
        c[(int)ImGuiCol.TextSelectedBg]       = new Vector4(0.00f, 0.38f, 0.78f, 0.55f);

        // Plot / progress
        c[(int)ImGuiCol.PlotLines]            = new Vector4(0.00f, 0.68f, 1.00f, 1.00f);
        c[(int)ImGuiCol.PlotHistogram]        = new Vector4(0.00f, 0.50f, 0.88f, 1.00f);

        // DragDrop / modal / nav
        c[(int)ImGuiCol.DragDropTarget]       = new Vector4(0.00f, 0.78f, 1.00f, 0.90f);
        c[(int)ImGuiCol.NavHighlight]         = new Vector4(0.00f, 0.68f, 1.00f, 1.00f);
        c[(int)ImGuiCol.ModalWindowDimBg]     = new Vector4(0.00f, 0.00f, 0.08f, 0.65f);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Splash / logo de fundo
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tenta carregar "splash.png" da pasta do exe como textura GL.
    /// Retorna 0 se o arquivo nao existir (silencioso).
    /// </summary>
    private static int TryLoadSplash()
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "splash.png");
            if (!File.Exists(path)) return 0;

            using var bmp   = new System.Drawing.Bitmap(path);
            using var bmp32 = bmp.Clone(
                new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb) as System.Drawing.Bitmap;
            if (bmp32 == null) return 0;

            var bd = bmp32.LockBits(
                new System.Drawing.Rectangle(0, 0, bmp32.Width, bmp32.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(bd.Stride) * bmp32.Height;
            var data  = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, data, 0, bytes);
            bmp32.UnlockBits(bd);

            // BGRA → RGBA
            for (int i = 0; i < data.Length; i += 4)
            { byte b = data[i]; data[i] = data[i + 2]; data[i + 2] = b; }

            // Flip Y para OpenGL
            int rowBytes = bmp32.Width * 4;
            var tmp = new byte[rowBytes];
            for (int y = 0; y < bmp32.Height / 2; y++)
            {
                int top = y * rowBytes, bot = (bmp32.Height - 1 - y) * rowBytes;
                System.Buffer.BlockCopy(data, top, tmp, 0, rowBytes);
                System.Buffer.BlockCopy(data, bot, data, top, rowBytes);
                System.Buffer.BlockCopy(tmp, 0, data, bot, rowBytes);
            }

            int tex = GL.GenTexture();
            GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, tex);
            GL.TexImage2D(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0,
                OpenTK.Graphics.OpenGL4.PixelInternalFormat.Rgba8,
                bmp32.Width, bmp32.Height, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, data);
            GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D,
                OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMinFilter,
                (int)OpenTK.Graphics.OpenGL4.TextureMinFilter.Linear);
            GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D,
                OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMagFilter,
                (int)OpenTK.Graphics.OpenGL4.TextureMagFilter.Linear);
            GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0);
            return tex;
        }
        catch { return 0; }
    }
}
