namespace WydFormats;

public static class WysWriter
{
    public static byte[] EncodeFromRgba(byte[] rgba, int width, int height)
    {
        if (rgba == null) throw new ArgumentNullException(nameof(rgba));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
        if (rgba.Length < width * height * 4) throw new ArgumentException("Buffer RGBA inválido.");

        bool hasAlpha = false;
        for (int i = 3; i < width * height * 4; i += 4)
        {
            if (rgba[i] != 255) { hasAlpha = true; break; }
        }

        int w = (width + 3) & ~3;
        int h = (height + 3) & ~3;
        byte[] src = rgba;
        if (w != width || h != height)
        {
            src = PadToMultipleOf4(rgba, width, height, w, h);
            width = w;
            height = h;
        }

        byte[] dxt = hasAlpha ? EncodeDxt3(src, width, height) : EncodeDxt1(src, width, height);
        byte[] dds = BuildDds(width, height, hasAlpha ? (byte)'3' : (byte)'2', dxt.Length);

        var wys = new byte[1 + dds.Length + dxt.Length];
        wys[0] = 0;
        Buffer.BlockCopy(dds, 0, wys, 1, dds.Length);
        Buffer.BlockCopy(dxt, 0, wys, 1 + dds.Length, dxt.Length);
        return wys;
    }

    private static byte[] PadToMultipleOf4(byte[] rgba, int w, int h, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        for (int y = 0; y < dstH; y++)
        {
            int sy = y < h ? y : (h - 1);
            for (int x = 0; x < dstW; x++)
            {
                int sx = x < w ? x : (w - 1);
                int si = (sy * w + sx) * 4;
                int di = (y * dstW + x) * 4;
                dst[di + 0] = rgba[si + 0];
                dst[di + 1] = rgba[si + 1];
                dst[di + 2] = rgba[si + 2];
                dst[di + 3] = rgba[si + 3];
            }
        }
        return dst;
    }

    private static byte[] BuildDds(int width, int height, byte fourCC0, int linearSize)
    {
        const uint DDSD_CAPS = 0x1;
        const uint DDSD_HEIGHT = 0x2;
        const uint DDSD_WIDTH = 0x4;
        const uint DDSD_PIXELFORMAT = 0x1000;
        const uint DDSD_LINEARSIZE = 0x80000;
        const uint DDPF_FOURCC = 0x4;
        const uint DDSCAPS_TEXTURE = 0x1000;

        byte[] dds = new byte[128];
        dds[0] = (byte)'D';
        dds[1] = (byte)'D';
        dds[2] = (byte)'S';
        dds[3] = 0x20;

        WriteU32(dds, 4, 124);
        WriteU32(dds, 8, DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_LINEARSIZE);
        WriteU32(dds, 12, (uint)height);
        WriteU32(dds, 16, (uint)width);
        WriteU32(dds, 20, (uint)linearSize);
        WriteU32(dds, 24, 0);
        WriteU32(dds, 28, 0);

        WriteU32(dds, 76, 32);
        WriteU32(dds, 80, DDPF_FOURCC);
        dds[84] = fourCC0;
        dds[85] = (byte)'X';
        dds[86] = (byte)'T';
        dds[87] = fourCC0 == (byte)'2' ? (byte)'1' : (byte)'3';
        WriteU32(dds, 88, 0);
        WriteU32(dds, 92, 0);
        WriteU32(dds, 96, 0);
        WriteU32(dds, 100, 0);
        WriteU32(dds, 104, 0);

        WriteU32(dds, 108, DDSCAPS_TEXTURE);
        WriteU32(dds, 112, 0);
        WriteU32(dds, 116, 0);
        WriteU32(dds, 120, 0);
        WriteU32(dds, 124, 0);

        return dds;
    }

    private static void WriteU32(byte[] buf, int off, uint v)
    {
        buf[off + 0] = (byte)(v & 0xFF);
        buf[off + 1] = (byte)((v >> 8) & 0xFF);
        buf[off + 2] = (byte)((v >> 16) & 0xFF);
        buf[off + 3] = (byte)((v >> 24) & 0xFF);
    }

    private static byte[] EncodeDxt1(byte[] rgba, int width, int height)
    {
        int blocksX = width / 4;
        int blocksY = height / 4;
        byte[] dst = new byte[blocksX * blocksY * 8];
        int di = 0;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                EncodeColorBlock(rgba, width, bx * 4, by * 4, forceFourColor: true, out ushort c0, out ushort c1, out uint idx);
                dst[di + 0] = (byte)(c0 & 0xFF);
                dst[di + 1] = (byte)(c0 >> 8);
                dst[di + 2] = (byte)(c1 & 0xFF);
                dst[di + 3] = (byte)(c1 >> 8);
                dst[di + 4] = (byte)(idx & 0xFF);
                dst[di + 5] = (byte)((idx >> 8) & 0xFF);
                dst[di + 6] = (byte)((idx >> 16) & 0xFF);
                dst[di + 7] = (byte)((idx >> 24) & 0xFF);
                di += 8;
            }
        }
        return dst;
    }

    private static byte[] EncodeDxt3(byte[] rgba, int width, int height)
    {
        int blocksX = width / 4;
        int blocksY = height / 4;
        byte[] dst = new byte[blocksX * blocksY * 16];
        int di = 0;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                ulong alpha = 0;
                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        int sx = bx * 4 + px;
                        int sy = by * 4 + py;
                        int si = (sy * width + sx) * 4;
                        byte a = rgba[si + 3];
                        ulong a4 = (ulong)(a >> 4);
                        int shift = (py * 4 + px) * 4;
                        alpha |= (a4 & 0xFu) << shift;
                    }
                }

                dst[di + 0] = (byte)(alpha & 0xFF);
                dst[di + 1] = (byte)((alpha >> 8) & 0xFF);
                dst[di + 2] = (byte)((alpha >> 16) & 0xFF);
                dst[di + 3] = (byte)((alpha >> 24) & 0xFF);
                dst[di + 4] = (byte)((alpha >> 32) & 0xFF);
                dst[di + 5] = (byte)((alpha >> 40) & 0xFF);
                dst[di + 6] = (byte)((alpha >> 48) & 0xFF);
                dst[di + 7] = (byte)((alpha >> 56) & 0xFF);

                EncodeColorBlock(rgba, width, bx * 4, by * 4, forceFourColor: true, out ushort c0, out ushort c1, out uint idx);
                dst[di + 8] = (byte)(c0 & 0xFF);
                dst[di + 9] = (byte)(c0 >> 8);
                dst[di + 10] = (byte)(c1 & 0xFF);
                dst[di + 11] = (byte)(c1 >> 8);
                dst[di + 12] = (byte)(idx & 0xFF);
                dst[di + 13] = (byte)((idx >> 8) & 0xFF);
                dst[di + 14] = (byte)((idx >> 16) & 0xFF);
                dst[di + 15] = (byte)((idx >> 24) & 0xFF);
                di += 16;
            }
        }
        return dst;
    }

    private static void EncodeColorBlock(byte[] rgba, int strideW, int x0, int y0, bool forceFourColor,
        out ushort c0, out ushort c1, out uint indices)
    {
        byte minR = 255, minG = 255, minB = 255;
        byte maxR = 0, maxG = 0, maxB = 0;

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int si = ((y0 + y) * strideW + (x0 + x)) * 4;
                byte r = rgba[si + 0];
                byte g = rgba[si + 1];
                byte b = rgba[si + 2];
                if (r < minR) minR = r;
                if (g < minG) minG = g;
                if (b < minB) minB = b;
                if (r > maxR) maxR = r;
                if (g > maxG) maxG = g;
                if (b > maxB) maxB = b;
            }
        }

        ushort max565 = To565(maxR, maxG, maxB);
        ushort min565 = To565(minR, minG, minB);
        c0 = max565;
        c1 = min565;
        if (forceFourColor && c0 == c1)
        {
            if (c0 > 0) c1 = (ushort)(c0 - 1);
            else c0 = 1;
        }
        if (forceFourColor && c0 < c1)
        {
            ushort t = c0; c0 = c1; c1 = t;
        }

        Span<byte> pr = stackalloc byte[4];
        Span<byte> pg = stackalloc byte[4];
        Span<byte> pb = stackalloc byte[4];
        From565(c0, out pr[0], out pg[0], out pb[0]);
        From565(c1, out pr[1], out pg[1], out pb[1]);

        pr[2] = (byte)((2 * pr[0] + pr[1] + 1) / 3);
        pg[2] = (byte)((2 * pg[0] + pg[1] + 1) / 3);
        pb[2] = (byte)((2 * pb[0] + pb[1] + 1) / 3);
        pr[3] = (byte)((pr[0] + 2 * pr[1] + 1) / 3);
        pg[3] = (byte)((pg[0] + 2 * pg[1] + 1) / 3);
        pb[3] = (byte)((pb[0] + 2 * pb[1] + 1) / 3);

        indices = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int si = ((y0 + y) * strideW + (x0 + x)) * 4;
                int r = rgba[si + 0];
                int g = rgba[si + 1];
                int b = rgba[si + 2];
                int best = 0;
                int bestD = int.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    int dr = r - pr[k];
                    int dg = g - pg[k];
                    int db = b - pb[k];
                    int d = dr * dr + dg * dg + db * db;
                    if (d < bestD) { bestD = d; best = k; }
                }
                int bit = (y * 4 + x) * 2;
                indices |= (uint)(best & 3) << bit;
            }
        }
    }

    private static ushort To565(byte r, byte g, byte b)
    {
        int rr = (r * 31 + 127) / 255;
        int gg = (g * 63 + 127) / 255;
        int bb = (b * 31 + 127) / 255;
        return (ushort)((rr << 11) | (gg << 5) | bb);
    }

    private static void From565(ushort c, out byte r, out byte g, out byte b)
    {
        int rr = (c >> 11) & 0x1F;
        int gg = (c >> 5) & 0x3F;
        int bb = c & 0x1F;
        r = (byte)(rr * 255 / 31);
        g = (byte)(gg * 255 / 63);
        b = (byte)(bb * 255 / 31);
    }
}

