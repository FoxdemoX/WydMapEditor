namespace WydFormats;

/// <summary>
/// Decodifica arquivos .wys do WYD Global.
/// Formato: 1 byte header descartado + DDS com primeiros 3 bytes obfuscados.
///   • Se byte[84] do payload == '2' (0x32) → DXT1
///   • Caso contrário                        → DXT3
/// Retorna pixels RGBA8 (width × height × 4 bytes, linha de cima primeiro).
/// </summary>
public static class WysLoader
{
    // ── Ponto de entrada ─────────────────────────────────────────────────────

    public static (byte[] pixels, int width, int height)? Decode(string path)
    {
        try   { return Decode(File.ReadAllBytes(path)); }
        catch { return null; }
    }

    public static (byte[] pixels, int width, int height)? Decode(byte[] raw)
    {
        if (raw == null || raw.Length < 130) return null;
        try
        {
            // Pular 1 byte de cabeçalho proprietário
            var dds = new byte[raw.Length - 1];
            Buffer.BlockCopy(raw, 1, dds, 0, dds.Length);

            // Restaurar magic "DDS " (primeiros 3 bytes obfuscados no arquivo)
            dds[0] = 0x44; // 'D'
            dds[1] = 0x44; // 'D'
            dds[2] = 0x53; // 'S'
            // dds[3] = 0x20 (espaço) já correto no arquivo original

            if (dds[3] != 0x20) return null; // não é DDS válido

            int height = BitConverter.ToInt32(dds, 12);
            int width  = BitConverter.ToInt32(dds, 16);
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return null;

            // dds[84] = primeiro byte do FourCC obfuscado
            // '2' (0x32) = DXT1, qualquer outro = DXT3
            bool isDXT1 = dds[84] == (byte)'2';

            // Dados DXT começam em offset 128 (4 magic + 124 bytes DDS_HEADER)
            const int DataOffset = 128;
            if (dds.Length <= DataOffset) return null;

            byte[] pixels = isDXT1
                ? DecodeDXT1(dds, DataOffset, width, height)
                : DecodeDXT3(dds, DataOffset, width, height);

            return (pixels, width, height);
        }
        catch { return null; }
    }

    // ── DXT1 ─────────────────────────────────────────────────────────────────

    private static byte[] DecodeDXT1(byte[] dds, int offset, int w, int h)
    {
        var pixels = new byte[w * h * 4];
        int blocksX = (w + 3) / 4;
        int blocksY = (h + 3) / 4;

        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++, offset += 8)
                DecodeDXT1Block(dds, offset, pixels, bx * 4, by * 4, w, h, punchAlpha: false);

        return pixels;
    }

    // ── DXT3 ─────────────────────────────────────────────────────────────────

    private static byte[] DecodeDXT3(byte[] dds, int offset, int w, int h)
    {
        var pixels = new byte[w * h * 4];
        int blocksX = (w + 3) / 4;
        int blocksY = (h + 3) / 4;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++, offset += 16)
            {
                // Primeiros 8 bytes = alpha explícito (4 bits por pixel)
                ulong alphaBlock = BitConverter.ToUInt64(dds, offset);

                // Próximos 8 bytes = bloco DXT1 de cor (sem punch-through)
                DecodeDXT1Block(dds, offset + 8, pixels, bx * 4, by * 4, w, h, punchAlpha: false);

                // Sobrescrever alpha (nibble de 4 bits → 8 bits)
                for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 4; px++)
                    {
                        int pixX = bx * 4 + px;
                        int pixY = by * 4 + py;
                        if (pixX >= w || pixY >= h) continue;
                        int shift = (py * 4 + px) * 4;
                        byte a4 = (byte)((alphaBlock >> shift) & 0xFu);
                        pixels[(pixY * w + pixX) * 4 + 3] = (byte)(a4 * 17); // 0xF→255
                    }
            }
        }
        return pixels;
    }

    // ── Bloco DXT1 (compartilhado entre DXT1 e DXT3) ────────────────────────

    private static void DecodeDXT1Block(byte[] src, int off,
        byte[] dst, int dstX, int dstY, int imgW, int imgH, bool punchAlpha)
    {
        // Duas cores em RGB565 (little-endian)
        ushort c0 = (ushort)(src[off]     | (src[off + 1] << 8));
        ushort c1 = (ushort)(src[off + 2] | (src[off + 3] << 8));
        uint indices = (uint)(src[off + 4] | (src[off + 5] << 8) |
                              (src[off + 6] << 16) | (src[off + 7] << 24));

        // Expandir RGB565 → RGB888
        Span<byte> r = stackalloc byte[4];
        Span<byte> g = stackalloc byte[4];
        Span<byte> b = stackalloc byte[4];
        Span<byte> a = stackalloc byte[4];

        r[0] = (byte)((c0 >> 11 & 0x1F) * 255 / 31);
        g[0] = (byte)((c0 >>  5 & 0x3F) * 255 / 63);
        b[0] = (byte)((c0       & 0x1F) * 255 / 31);
        a[0] = 255;

        r[1] = (byte)((c1 >> 11 & 0x1F) * 255 / 31);
        g[1] = (byte)((c1 >>  5 & 0x3F) * 255 / 63);
        b[1] = (byte)((c1       & 0x1F) * 255 / 31);
        a[1] = 255;

        if (c0 > c1)
        {
            // 4 cores opacas
            r[2] = (byte)((2 * r[0] + r[1] + 1) / 3);
            g[2] = (byte)((2 * g[0] + g[1] + 1) / 3);
            b[2] = (byte)((2 * b[0] + b[1] + 1) / 3);
            a[2] = 255;
            r[3] = (byte)((r[0] + 2 * r[1] + 1) / 3);
            g[3] = (byte)((g[0] + 2 * g[1] + 1) / 3);
            b[3] = (byte)((b[0] + 2 * b[1] + 1) / 3);
            a[3] = 255;
        }
        else
        {
            // 3 cores + transparente (punch-through)
            r[2] = (byte)((r[0] + r[1]) / 2);
            g[2] = (byte)((g[0] + g[1]) / 2);
            b[2] = (byte)((b[0] + b[1]) / 2);
            a[2] = 255;
            r[3] = 0; g[3] = 0; b[3] = 0;
            a[3] = punchAlpha ? (byte)0 : (byte)255;
        }

        for (int py = 0; py < 4; py++)
        {
            for (int px = 0; px < 4; px++)
            {
                int pixX = dstX + px;
                int pixY = dstY + py;
                if (pixX >= imgW || pixY >= imgH) continue;

                int idx    = (int)((indices >> ((py * 4 + px) * 2)) & 3u);
                int dstOff = (pixY * imgW + pixX) * 4;
                dst[dstOff]     = r[idx];
                dst[dstOff + 1] = g[idx];
                dst[dstOff + 2] = b[idx];
                dst[dstOff + 3] = a[idx];
            }
        }
    }

    // ── Escala bilinear simples (caso a textura não seja 128×128) ────────────
    public static byte[] ScaleTo(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        float sx = (float)srcW / dstW;
        float sy = (float)srcH / dstH;

        for (int dy = 0; dy < dstH; dy++)
        {
            for (int dx = 0; dx < dstW; dx++)
            {
                float fx = dx * sx;
                float fy = dy * sy;
                int   x0 = Math.Clamp((int)fx,         0, srcW - 1);
                int   y0 = Math.Clamp((int)fy,         0, srcH - 1);
                int   x1 = Math.Clamp((int)fx + 1,     0, srcW - 1);
                int   y1 = Math.Clamp((int)fy + 1,     0, srcH - 1);
                float wx = fx - (int)fx;
                float wy = fy - (int)fy;

                for (int c = 0; c < 4; c++)
                {
                    float v = src[(y0 * srcW + x0) * 4 + c] * (1 - wx) * (1 - wy)
                            + src[(y0 * srcW + x1) * 4 + c] * wx       * (1 - wy)
                            + src[(y1 * srcW + x0) * 4 + c] * (1 - wx) * wy
                            + src[(y1 * srcW + x1) * 4 + c] * wx       * wy;
                    dst[(dy * dstW + dx) * 4 + c] = (byte)Math.Clamp((int)v, 0, 255);
                }
            }
        }
        return dst;
    }
}
