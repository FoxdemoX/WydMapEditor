using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using System.Drawing;
using System.Drawing.Imaging;

namespace WydMapEditor;

public static class Program
{
    public static void Main(string[] args)
    {
        try
        {
            RunInternal();
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show("Erro crítico ao abrir o FoxMap Studio:\n" + ex.Message + "\n\nO programa tentará gerar um log.", "Erro Crítico", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "FoxMapStudio_CrashLog.txt"), ex.ToString()); } catch { }
        }
    }

    private static void RunInternal()
    {
        var icon = TryLoadWindowIcon();
        var native = new NativeWindowSettings
        {
            Title = "FoxMap Studio V1",
            Size = new OpenTK.Mathematics.Vector2i(1600, 900),
            Icon = icon,
        };

        var gameSettings = new GameWindowSettings
        {
            UpdateFrequency = 60
        };

        using var window = new MainWindow(gameSettings, native);
        window.VSync = VSyncMode.On;
        window.Run();
    }

    private static WindowIcon? TryLoadWindowIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "FoxMapStudio.ico");
            if (!File.Exists(path)) return null;

            var imgs = new List<OpenTK.Windowing.Common.Input.Image>(3);
            AddSize(imgs, path, 256, 256);
            AddSize(imgs, path, 32, 32);
            AddSize(imgs, path, 16, 16);

            if (imgs.Count == 0) return null;
            return new WindowIcon(imgs.ToArray());
        }
        catch { return null; }
    }

    private static void AddSize(List<OpenTK.Windowing.Common.Input.Image> imgs, string path, int w, int h)
    {
        using var ico = new Icon(path, w, h);
        using var bmp = ico.ToBitmap();
        if (bmp.Width <= 0 || bmp.Height <= 0) return;
        if (imgs.Any(i => i.Width == bmp.Width && i.Height == bmp.Height)) return;
        imgs.Add(ToOpenTkImage(bmp));
    }

    private static OpenTK.Windowing.Common.Input.Image ToOpenTkImage(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int len = data.Stride * data.Height;
            var raw = new byte[len];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, len);

            var rgba = new byte[bmp.Width * bmp.Height * 4];
            int src = 0, dst = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                src = y * data.Stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    byte b = raw[src++];
                    byte g = raw[src++];
                    byte r = raw[src++];
                    byte a = raw[src++];
                    rgba[dst++] = r;
                    rgba[dst++] = g;
                    rgba[dst++] = b;
                    rgba[dst++] = a;
                }
            }

            return new OpenTK.Windowing.Common.Input.Image(bmp.Width, bmp.Height, rgba);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
