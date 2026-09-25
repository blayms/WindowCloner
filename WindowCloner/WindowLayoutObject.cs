using System.Globalization;

namespace WindowCloner
{
    public class WindowLayoutObject
    {
        public string name = string.Empty;
        public string windowName = string.Empty;
        public string processName = string.Empty;
        public int locationX;
        public int locationY;
        public int rectX;
        public int rectY;
        public int width;
        public int height;
        public int rectW;
        public int rectH;
        public double scaleX = 1.0;
        public double scaleY = 1.0;
        public int bestSuitedForWidth;
        public int bestSuitedForHeight;

        public Rectangle GetRectangle(Welcome.WindowItem? window)
        {
            if (window != null)
            {
                NativeMethods.GetWindowRect(window.Handle, out NativeMethods.RECT wndRect);

                int sw = wndRect.Right - wndRect.Left;
                int sh = wndRect.Bottom - wndRect.Top;

                int screenLeft = 0;
                int screenTop = 0;

                int localX = rectX - screenLeft;
                int localY = rectY - screenTop;

                int x = Math.Max(0, Math.Min(localX, sw - 1));
                int y = Math.Max(0, Math.Min(localY, sh - 1));

                int maxWidth = Math.Max(1, sw - x);
                int maxHeight = Math.Max(1, sh - y);

                int cw = rectW > 0 ? rectW : width;
                int ch = rectH > 0 ? rectH : height;

                int w = Math.Max(1, Math.Min(cw, maxWidth));
                int h = Math.Max(1, Math.Min(ch, maxHeight));

                return new Rectangle(x, y, w, h);
            }

            int fallbackW = rectW > 0 ? rectW : width;
            int fallbackH = rectH > 0 ? rectH : height;
            return new Rectangle(rectX, rectY, fallbackW, fallbackH);
        }

        public static WindowLayoutObject? FromString(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            string[] parts = line.Split('=');
            if (parts.Length != 2)
            {
                return null;
            }

            string name = parts[0];
            string[] p = parts[1].Split(';');

            if (p.Length == 10)
            {
                if (!int.TryParse(p[2], out int lx)) return null;
                if (!int.TryParse(p[3], out int ly)) return null;
                if (!int.TryParse(p[4], out int rx)) return null;
                if (!int.TryParse(p[5], out int ry)) return null;
                if (!int.TryParse(p[6], out int w)) return null;
                if (!int.TryParse(p[7], out int h)) return null;
                if (!int.TryParse(p[8], out int bw)) return null;
                if (!int.TryParse(p[9], out int bh)) return null;

                return new WindowLayoutObject
                {
                    name = name,
                    windowName = p[0],
                    processName = p[1],
                    locationX = lx,
                    locationY = ly,
                    rectX = rx,
                    rectY = ry,
                    width = w,
                    height = h,
                    rectW = w,
                    rectH = h,
                    scaleX = 1.0,
                    scaleY = 1.0,
                    bestSuitedForWidth = bw,
                    bestSuitedForHeight = bh
                };
            }

            if (p.Length != 14)
            {
                return null;
            }

            if (!int.TryParse(p[2], out int locX)) return null;
            if (!int.TryParse(p[3], out int locY)) return null;
            if (!int.TryParse(p[4], out int rX)) return null;
            if (!int.TryParse(p[5], out int rY)) return null;
            if (!int.TryParse(p[6], out int w2)) return null;
            if (!int.TryParse(p[7], out int h2)) return null;
            if (!int.TryParse(p[8], out int rW)) return null;
            if (!int.TryParse(p[9], out int rH)) return null;
            if (!double.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out double sx)) return null;
            if (!double.TryParse(p[11], NumberStyles.Float, CultureInfo.InvariantCulture, out double sy)) return null;
            if (!int.TryParse(p[12], out int bfw)) return null;
            if (!int.TryParse(p[13], out int bfh)) return null;

            return new WindowLayoutObject
            {
                name = name,
                windowName = p[0],
                processName = p[1],
                locationX = locX,
                locationY = locY,
                rectX = rX,
                rectY = rY,
                width = w2,
                height = h2,
                rectW = rW,
                rectH = rH,
                scaleX = sx,
                scaleY = sy,
                bestSuitedForWidth = bfw,
                bestSuitedForHeight = bfh
            };
        }

        public override string ToString()
        {
            return $"{name.Trim()}={windowName.Trim()};{processName.Trim()};" +
                   $"{locationX};{locationY};{rectX};{rectY};{width};{height};" +
                   $"{rectW};{rectH};" +
                   $"{scaleX.ToString(CultureInfo.InvariantCulture)};" +
                   $"{scaleY.ToString(CultureInfo.InvariantCulture)};" +
                   $"{bestSuitedForWidth};{bestSuitedForHeight}";
        }
    }
}