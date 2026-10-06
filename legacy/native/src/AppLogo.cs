using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CodexMonitor
{
    public static class AppLogo
    {
        static readonly Bitmap source = Load();
        static readonly Bitmap rounded = Render(256);
        static Bitmap Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexMonitor.Logo.png"))
            {
                if (stream == null) throw new InvalidOperationException("缺少内嵌 Logo 资源");
                using (var image = Image.FromStream(stream)) return new Bitmap(image);
            }
        }
        public static Bitmap Render(int pixels)
        {
            // Supersample the rounded clipping edge, keeping the corners genuinely
            // transparent rather than painting a background-colored substitute.
            int high = pixels * 4;
            using (var large = new Bitmap(high, high, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(large))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var clip = PanelStyle.Round(new RectangleF(0, 0, high, high), high * .22f))
                {
                    g.SetClip(clip);
                    int side = Math.Min(source.Width, source.Height);
                    g.DrawImage(source, new Rectangle(0, 0, high, high), new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                }
                var output = new Bitmap(pixels, pixels, PixelFormat.Format32bppArgb);
                using (var small = Graphics.FromImage(output))
                {
                    small.Clear(Color.Transparent);
                    small.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    small.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    small.DrawImage(large, new Rectangle(0, 0, pixels, pixels));
                }
                return output;
            }
        }
        public static void Draw(Graphics g, RectangleF bounds)
        {
            var state = g.Save();
            try { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(rounded, bounds); }
            finally { g.Restore(state); }
        }
        public static Icon CreateIcon(int pixels)
        {
            using (var bitmap = Render(pixels))
            {
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        public static void ExportIcon(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
                using (var bitmap = Render(size))
                using (var stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); images.Add(stream.ToArray()); }
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
                }
                foreach (var bytes in images) writer.Write(bytes);
            }
        }
    }
}
