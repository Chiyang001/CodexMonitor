using System;
using System.Drawing;

namespace CodexMonitor
{
    public static class WindowLayout
    {
        public const int DefaultWidth = 208;
        public const int DefaultHeight = 122;
        public static float ContentScale(Size size)
        {
            return Math.Max(0.1f, Math.Min(size.Width / (float)DefaultWidth, size.Height / (float)DefaultHeight));
        }
        // Win32 non-client resize hit values. Only the narrow outer border resizes;
        // the contents remain draggable and header buttons remain clickable.
        public static int ResizeHit(Point point, Size size, int border)
        {
            bool left = point.X < border, right = point.X >= size.Width - border;
            bool top = point.Y < border, bottom = point.Y >= size.Height - border;
            if (top && left) return 13;
            if (top && right) return 14;
            if (bottom && left) return 16;
            if (bottom && right) return 17;
            if (left) return 10;
            if (right) return 11;
            if (top) return 12;
            if (bottom) return 15;
            return 1;
        }
    }
}
