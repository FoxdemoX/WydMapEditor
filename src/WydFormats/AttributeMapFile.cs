using System.Text;

namespace WydFormats;

/// <summary>
/// Lê e escreve o AttributeMap.dat do WYD.
/// É uma grade 1024×1024 de bytes, onde cada byte armazena flags (bitmask) do servidor:
///   1   = Village
///   2   = CantGo (bloqueio/parede)
///   4   = CantSummon
///   8   = House
///   16  = Teleport
///   32  = Guild
///   64  = PvP
///   128 = Newbie
/// </summary>
public sealed class AttributeMapFile
{
    public const int SIZE = 1024;
    public const int WORLD_SIZE = SIZE * 4;
    public const int FIELD_SIZE = 128;

    /// <summary>Grid 1024×1024. Index = [x + y * SIZE].</summary>
    public byte[] Data { get; } = new byte[SIZE * SIZE];

    // ── Nomes amigáveis para os bits de atributo ──────────────────────────────
    public static readonly (byte Mask, string Name, uint Color)[] AttributeTypes =
    {
        (0,   "Normal",                 0xFF22AA44),
        (1,   "Cidade (Village)",       0xFF0080FF),
        (2,   "Bloqueio (CantGo)",      0xFF404040),
        (4,   "Sem Summon",             0xFFFFC800),
        (8,   "House",                  0xFF3C69A0),
        (16,  "Teleport",               0xFF00E5FF),
        (32,  "Guild",                  0xFFFF3CA0),
        (64,  "PvP",                    0xFF3232FF),
        (65,  "Cidade + PvP",           0xFFAA44FF),
        (128, "Newbie",                 0xFF66FF66),
    };

    // ── Load ─────────────────────────────────────────────────────────────────

    public static AttributeMapFile Load(string path)
    {
        var am = new AttributeMapFile();
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read = fs.Read(am.Data, 0, am.Data.Length);
        // Se menor que 1MB, o restante já está em zero
        return am;
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    public void Save(string path)
    {
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(Data, 0, Data.Length);
    }

    // ── Acesso por posição de tile TRN (64×64) ────────────────────────────────
    // Cada tile TRN = 16×16 células do AttributeMap

    public byte GetAtTile(int tileX, int tileY)
    {
        int ax = tileX * 16 + 8;
        int ay = tileY * 16 + 8;
        ax = Math.Clamp(ax, 0, SIZE - 1);
        ay = Math.Clamp(ay, 0, SIZE - 1);
        return Data[ax + ay * SIZE];
    }

    public byte GetAtFieldTile(int envPosX, int envPosY, int tileX, int tileY)
    {
        int wx = (envPosX << 7) + tileX * 2 + 1;
        int wy = (envPosY << 7) + tileY * 2 + 1;
        int ax = (wx >> 2) & 0x3FF;
        int ay = (wy >> 2) & 0x3FF;
        return Data[ax + ay * SIZE];
    }

    public void SetAtTile(int tileX, int tileY, byte value, int brushRadius = 1)
    {
        int cx = tileX * 16 + 8;
        int cy = tileY * 16 + 8;
        int r  = brushRadius * 16;

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int ax = cx + dx, ay = cy + dy;
                if (ax < 0 || ax >= SIZE || ay < 0 || ay >= SIZE) continue;
                Data[ax + ay * SIZE] = value;
            }
        }
    }

    public void SetMaskAtTile(int tileX, int tileY, byte mask, bool enabled, int brushRadius = 1)
    {
        int cx = tileX * 16 + 8;
        int cy = tileY * 16 + 8;
        int r  = brushRadius * 16;

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int ax = cx + dx, ay = cy + dy;
                if (ax < 0 || ax >= SIZE || ay < 0 || ay >= SIZE) continue;
                int idx = ax + ay * SIZE;
                byte v = Data[idx];
                if (mask == 0)
                    Data[idx] = enabled ? (byte)0 : v;
                else
                    Data[idx] = enabled ? (byte)(v | mask) : (byte)(v & ~mask);
            }
        }
    }

    public void SetMaskAtTiles(int centerTileX, int centerTileY, byte mask, bool enabled, int brushRadiusTiles = 0)
    {
        int br = Math.Max(0, brushRadiusTiles);
        int minTx = Math.Clamp(centerTileX - br, 0, 63);
        int maxTx = Math.Clamp(centerTileX + br, 0, 63);
        int minTy = Math.Clamp(centerTileY - br, 0, 63);
        int maxTy = Math.Clamp(centerTileY + br, 0, 63);

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                int baseX = tx * 16;
                int baseY = ty * 16;

                for (int oy = 0; oy < 16; oy++)
                {
                    int ay = baseY + oy;
                    int row = ay * SIZE;
                    for (int ox = 0; ox < 16; ox++)
                    {
                        int ax = baseX + ox;
                        int idx = ax + row;
                        byte v = Data[idx];
                        if (mask == 0)
                            Data[idx] = enabled ? (byte)0 : v;
                        else
                            Data[idx] = enabled ? (byte)(v | mask) : (byte)(v & ~mask);
                    }
                }
            }
        }
    }

    public void SetMaskAtFieldTiles(int envPosX, int envPosY, int centerTileX, int centerTileY, byte mask, bool enabled, int brushRadiusTiles = 0)
    {
        int br = Math.Max(0, brushRadiusTiles);
        int minTx = Math.Clamp(centerTileX - br, 0, 63);
        int maxTx = Math.Clamp(centerTileX + br, 0, 63);
        int minTy = Math.Clamp(centerTileY - br, 0, 63);
        int maxTy = Math.Clamp(centerTileY + br, 0, 63);

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                int wx = (envPosX << 7) + tx * 2 + 1;
                int wy = (envPosY << 7) + ty * 2 + 1;
                int ax = (wx >> 2) & 0x3FF;
                int ay = (wy >> 2) & 0x3FF;
                int idx = ax + ay * SIZE;
                byte v = Data[idx];
                if (mask == 0)
                    Data[idx] = enabled ? (byte)0 : v;
                else
                    Data[idx] = enabled ? (byte)(v | mask) : (byte)(v & ~mask);
            }
        }
    }

    // ── Acesso por coordenada de mundo direta ─────────────────────────────────
    // worldX / worldZ são coordenadas em unidades WYD (posição do TRN = 0..127)
    // O jogo usa pos / 4 para indexar o AttributeMap (posição 0..511 → attr 0..1023)

    public byte GetAtWorld(float worldX, float worldZ)
    {
        int ax = Math.Clamp((int)(worldX * 8f), 0, SIZE - 1);
        int ay = Math.Clamp((int)(worldZ * 8f), 0, SIZE - 1);
        return Data[ax + ay * SIZE];
    }

    public byte GetAtWorldGlobal(float worldX, float worldZ)
    {
        int wx = Math.Clamp((int)MathF.Floor(worldX), 0, WORLD_SIZE - 1);
        int wy = Math.Clamp((int)MathF.Floor(worldZ), 0, WORLD_SIZE - 1);
        int ax = (wx >> 2) & 0x3FF;
        int ay = (wy >> 2) & 0x3FF;
        return Data[ax + ay * SIZE];
    }

    public byte[] BuildFieldOverlayRgba(int envPosX, int envPosY, int overlayW = 64, int overlayH = 64)
    {
        var pixels = new byte[overlayW * overlayH * 4];

        float baseX = (envPosX << 7);
        float baseY = (envPosY << 7);

        for (int oy = 0; oy < overlayH; oy++)
        {
            for (int ox = 0; ox < overlayW; ox++)
            {
                float lx = (ox + 0.5f) / overlayW * FIELD_SIZE;
                float ly = (oy + 0.5f) / overlayH * FIELD_SIZE;
                byte attr = GetAtWorldGlobal(baseX + lx, baseY + ly);

                uint col = AttrToColor(attr);
                int off = (oy * overlayW + ox) * 4;
                pixels[off] = (byte)(col & 0xFF);
                pixels[off + 1] = (byte)((col >> 8) & 0xFF);
                pixels[off + 2] = (byte)((col >> 16) & 0xFF);
                pixels[off + 3] = (attr == 0) ? (byte)0 : (byte)180;
            }
        }

        return pixels;
    }

    // ── Gera mapa RGBA8 64×64 para exibição como overlay ─────────────────────

    public byte[] BuildOverlayRgba(int overlayW = 64, int overlayH = 64)
    {
        var pixels = new byte[overlayW * overlayH * 4];
        int scaleX = SIZE / overlayW;
        int scaleY = SIZE / overlayH;

        for (int oy = 0; oy < overlayH; oy++)
        {
            for (int ox = 0; ox < overlayW; ox++)
            {
                // Amostrar centro do bloco
                int ax = ox * scaleX + scaleX / 2;
                int ay = oy * scaleY + scaleY / 2;
                byte attr = Data[ax + ay * SIZE];

                uint col = AttrToColor(attr);
                int  off = (oy * overlayW + ox) * 4;
                pixels[off]     = (byte)(col & 0xFF);          // R
                pixels[off + 1] = (byte)((col >> 8)  & 0xFF);  // G
                pixels[off + 2] = (byte)((col >> 16) & 0xFF);  // B
                pixels[off + 3] = (attr == 0) ? (byte)0 : (byte)180;
            }
        }

        return pixels;
    }

    public static uint AttrToColor(byte attr)
    {
        for (int i = 0; i < AttributeTypes.Length; i++)
            if (AttributeTypes[i].Mask == attr) return AttributeTypes[i].Color;

        if ((attr & 2) != 0)   return AttributeTypes[2].Color;
        if ((attr & 16) != 0)  return AttributeTypes[5].Color;
        if ((attr & 8) != 0)   return AttributeTypes[4].Color;
        if ((attr & 64) != 0 && (attr & 1) != 0) return AttributeTypes[8].Color;
        if ((attr & 64) != 0)  return AttributeTypes[7].Color;
        if ((attr & 32) != 0)  return AttributeTypes[6].Color;
        if ((attr & 1)  != 0)  return AttributeTypes[1].Color;
        if ((attr & 128) != 0) return AttributeTypes[9].Color;
        if ((attr & 4)  != 0)  return AttributeTypes[3].Color;
        return AttributeTypes[0].Color;
    }
}
