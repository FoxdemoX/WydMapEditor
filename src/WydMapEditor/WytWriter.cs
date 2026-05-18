using System;
using System.IO;

namespace WydMapEditor;

public static class WytWriter
{
    public static void Save(string path, byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
        if (rgba.Length < width * height * 4) throw new ArgumentException(nameof(rgba));

        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        bw.Write(0u);

        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((byte)2);
        bw.Write((short)0);
        bw.Write((short)0);
        bw.Write((byte)0);
        bw.Write((short)0);
        bw.Write((short)0);
        bw.Write((short)width);
        bw.Write((short)height);
        bw.Write((byte)32);
        bw.Write((byte)0x28);

        int rowBytes = width * 4;
        var row = new byte[rowBytes];

        for (int y = 0; y < height; y++)
        {
            int src = y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int i = src + x * 4;
                int o = x * 4;
                row[o] = rgba[i + 2];
                row[o + 1] = rgba[i + 1];
                row[o + 2] = rgba[i];
                row[o + 3] = rgba[i + 3];
            }
            bw.Write(row);
        }
    }
}

