using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZhongWenSnap
{
    internal static class OverlayDragProbe
    {
        [DllImport("user32.dll")]
        private static extern bool ValidateRect(IntPtr hWnd, IntPtr rect);

        [DllImport("user32.dll")]
        private static extern int GetUpdateRgn(IntPtr hWnd, IntPtr region, bool erase);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [STAThread]
        private static int Main()
        {
            const int width = 1920, height = 1080;
            using (var screenshot = new Bitmap(width, height))
            using (var overlay = new CaptureOverlay(screenshot, new Rectangle(0, 0, width, height)))
            {
                using (var graphics = Graphics.FromImage(screenshot))
                    graphics.Clear(Color.FromArgb(255, 80, 90, 100));
                overlay.Opacity = 0;
                overlay.Show();
                Application.DoEvents();
                var handle = overlay.Handle;
                var mouseDown = typeof(CaptureOverlay).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                var mouseMove = typeof(CaptureOverlay).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
                var paint = typeof(CaptureOverlay).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                mouseDown.Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0) });
                mouseMove.Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 500, 400, 0) });
                using (var before = new Bitmap(width, height))
                {
                    Paint(overlay, paint, before, null);
                    ValidateRect(handle, IntPtr.Zero);
                    mouseMove.Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 501, 401, 0) });
                    var regionHandle = CreateRectRgn(0, 0, 0, 0);
                    try
                    {
                        var updateType = GetUpdateRgn(handle, regionHandle, false);
                        using (var region = Region.FromHrgn(regionHandle))
                        using (var matrix = new Matrix())
                        {
                            long pixels = 0;
                            foreach (var rect in region.GetRegionScans(matrix))
                                pixels += (long)rect.Width * (long)rect.Height;
                            Console.WriteLine("Update region: type={0}, pixels={1:N0} of {2:N0}",
                                updateType, pixels, (long)width * height);
                            using (var partial = (Bitmap)before.Clone())
                            using (var expected = new Bitmap(width, height))
                            {
                                Paint(overlay, paint, partial, region);
                                Paint(overlay, paint, expected, null);
                                if (!SamePixels(partial, expected))
                                {
                                    Console.WriteLine("Partial repaint differs from a full repaint.");
                                    return 1;
                                }
                                var fullMs = MeasurePaint(overlay, paint, expected, null);
                                var partialMs = MeasurePaint(overlay, paint, partial, region);
                                Console.WriteLine("20 paints: full={0} ms, partial={1} ms", fullMs, partialMs);
                            }
                            return pixels > 50000 ? 1 : 0;
                        }
                    }
                    finally { DeleteObject(regionHandle); }
                }
            }
        }

        private static void Paint(CaptureOverlay overlay, MethodInfo paint, Bitmap target, Region clip)
        {
            using (var graphics = Graphics.FromImage(target))
            {
                if (clip != null) graphics.SetClip(clip, CombineMode.Replace);
                paint.Invoke(overlay, new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, target.Width, target.Height)) });
            }
        }

        private static long MeasurePaint(CaptureOverlay overlay, MethodInfo paint, Bitmap target, Region clip)
        {
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) Paint(overlay, paint, target, clip);
            timer.Stop();
            return timer.ElapsedMilliseconds;
        }

        private static bool SamePixels(Bitmap first, Bitmap second)
        {
            var rect = new Rectangle(0, 0, first.Width, first.Height);
            var a = first.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var b = second.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = Math.Abs(a.Stride) * first.Height;
                var aPixels = new byte[bytes];
                var bPixels = new byte[bytes];
                Marshal.Copy(a.Scan0, aPixels, 0, bytes);
                Marshal.Copy(b.Scan0, bPixels, 0, bytes);
                for (var i = 0; i < bytes; i++)
                    if (aPixels[i] != bPixels[i]) return false;
                return true;
            }
            finally
            {
                first.UnlockBits(a);
                second.UnlockBits(b);
            }
        }
    }
}
