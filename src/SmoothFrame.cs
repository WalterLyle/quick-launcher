using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickLauncher
{
    // A click-through, alpha-blended rim covers the binary clipping edge on Windows 10.
    // The real controls remain in the normal window, preserving native input and drag/drop.
    internal sealed class SmoothFrame : Form
    {
        const int MarginSize = 3;
        Bitmap image;
        Size renderedSize;
        float renderedScale;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80000 | 0x80 | 0x20 | 0x08000000;
                return parameters;
            }
        }
        internal SmoothFrame()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }
        internal static Bitmap Render(Size size, float scale = 1f, float radius = 16, bool border = false)
        {
            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var path = Theme.Round(new RectangleF(.5f, .5f, size.Width - 1, size.Height - 1), radius * scale + MarginSize))
                using (var brush = new SolidBrush(Theme.Main))
                {
                    graphics.FillPath(brush, path);
                    if (border) using (var pen = new Pen(Theme.Border)) graphics.DrawPath(pen, path);
                }
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using (var hole = Theme.Round(new RectangleF(6, 6, size.Width - 12, size.Height - 12), Math.Max(1, radius * scale - MarginSize)))
                using (var brush = new SolidBrush(Color.Transparent)) graphics.FillPath(brush, hole);
            }
            return bitmap;
        }
        internal void Synchronize(RoundedForm owner)
        {
            if (owner.IsDisposed || !owner.Visible || owner.WindowState != FormWindowState.Normal || owner.Opacity == 0)
            {
                Hide();
                return;
            }
            Rectangle bounds = Rectangle.Inflate(owner.Bounds, MarginSize, MarginSize);
            float scale = owner.DeviceScale;
            if (image == null || renderedSize != bounds.Size || renderedScale != scale)
            {
                if (image != null) image.Dispose();
                image = Render(bounds.Size, scale);
                renderedSize = bounds.Size;
                renderedScale = scale;
            }
            Bounds = bounds;
            if (!Visible) Show(owner);
            PaintLayer(bounds.Location);
        }
        void PaintLayer(Point location)
        {
            PaintBitmap(this, image, location, 255);
        }
        internal static void PaintBitmap(Form window, Bitmap image, Point location, byte alpha)
        {
            PaintBitmap(window.Handle, image, location, alpha);
        }
        internal static void PaintBitmap(IntPtr window, Bitmap image, Point location, byte alpha)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                bitmap = image.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, bitmap);
                NativePoint destination = new NativePoint { X = location.X, Y = location.Y };
                NativePoint source = new NativePoint();
                NativeSize size = new NativeSize { Width = image.Width, Height = image.Height };
                Blend blend = new Blend { Alpha = alpha, Format = 1 };
                if (!UpdateLayeredWindow(window, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null) { image.Dispose(); image = null; }
            base.Dispose(disposing);
        }
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct NativeSize { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { internal byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDC,
            ref NativePoint destination, ref NativeSize size, IntPtr sourceDC, ref NativePoint source, int colorKey, ref Blend blend, int flags);
    }

    // A native popup avoids Form activation bookkeeping, which dismisses ContextMenuStrip menus.
    internal sealed class PopupSmoothFrame : NativeWindow, IDisposable
    {
        Bitmap image;
        float renderedScale;
        internal bool IsFrameVisible { get { return Handle != IntPtr.Zero && IsWindowVisible(Handle); } }
        internal PopupSmoothFrame()
        {
            CreateHandle(new CreateParams { Caption = "QuickLauncher menu edge", Style = unchecked((int)0x80000000),
                ExStyle = 0x80000 | 0x80 | 0x20 | 0x08000000 | 0x8 });
        }
        internal void Synchronize(Control menu)
        {
            if (!menu.Visible) { ShowWindow(Handle, 0); return; }
            Rectangle bounds = Rectangle.Inflate(menu.RectangleToScreen(menu.ClientRectangle), 3, 3);
            float scale = menu.DeviceDpi / 96f;
            if (image == null || image.Size != bounds.Size || renderedScale != scale)
            {
                if (image != null) image.Dispose();
                image = SmoothFrame.Render(bounds.Size, scale, 12, true);
                renderedScale = scale;
            }
            SmoothFrame.PaintBitmap(Handle, image, bounds.Location, 255);
            SetWindowPos(Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40);
        }
        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
            if (image != null) { image.Dispose(); image = null; }
        }
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }

    internal sealed class CapsuleToast : Form
    {
        readonly Form host;
        readonly Bitmap image;
        readonly Timer animation = new Timer { Interval = 16 };
        readonly System.Diagnostics.Stopwatch elapsed = new System.Diagnostics.Stopwatch();
        readonly float scale;
        internal const int Duration = 3000;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80000 | 0x80 | 0x20 | 0x08000000;
                return parameters;
            }
        }
        internal CapsuleToast(Form owner, string message)
        {
            host = owner;
            Text = message;
            scale = owner.DeviceDpi / 96f;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            int maxWidth = Math.Max(1, owner.ClientSize.Width - (int)(64 * scale));
            image = Render(message, owner.Font, scale, maxWidth);
            Size = image.Size;
            animation.Tick += delegate { Animate(); };
        }
        internal static Bitmap Render(string message, Font uiFont, float scale, int maxWidth)
        {
            // Measure and render with the same pixel font; do not mix GDI measurement with GDI+ drawing.
            using (var font = new Font(uiFont.FontFamily, uiFont.SizeInPoints * 96f / 72f * scale, uiFont.Style, GraphicsUnit.Pixel))
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None })
            using (var measure = new Bitmap(1, 1))
            using (Graphics metrics = Graphics.FromImage(measure))
            {
                metrics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int padding = (int)Math.Ceiling(24 * scale);
                int width = Math.Min(maxWidth, (int)Math.Ceiling(metrics.MeasureString(message, font, new SizeF(100000, 100000), format).Width) + padding * 2);
                int textWidth = Math.Max(1, width - padding * 2);
                SizeF measured = metrics.MeasureString(message, font, new SizeF(textWidth, 100000), format);
                int height = Math.Max((int)Math.Ceiling(42 * scale), (int)Math.Ceiling(measured.Height + 20 * scale));
                // Supersampling keeps both the transparent capsule edge and text smooth at fractional DPI.
                const int sample = 3;
                using (var high = new Bitmap(width * sample, height * sample, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(high))
                    using (var path = Theme.Round(new RectangleF(.5f, .5f, width - 1, height - 1), Math.Min(height / 2f, 24 * scale)))
                    using (var fill = new SolidBrush(Theme.Main))
                    using (var text = new SolidBrush(Color.White))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.ScaleTransform(sample, sample);
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        graphics.FillPath(fill, path);
                        graphics.DrawString(message, font, text, new RectangleF(padding, 10 * scale, textWidth, height - 20 * scale), format);
                    }
                    var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                    using (Graphics graphics = Graphics.FromImage(result))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(high, new Rectangle(0, 0, width, height), 0, 0, high.Width, high.Height, GraphicsUnit.Pixel);
                    }
                    return result;
                }
            }
        }
        internal void Present()
        {
            Show(host);
            elapsed.Start();
            Animate();
            animation.Start();
        }
        void Animate()
        {
            if (host.IsDisposed || !host.Visible || host.WindowState == FormWindowState.Minimized || elapsed.ElapsedMilliseconds >= Duration)
            {
                Close();
                return;
            }
            double progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / Duration);
            double fade = Math.Max(0, (elapsed.Elapsed.TotalMilliseconds - 500) / (Duration - 500));
            Point position = host.PointToScreen(new Point((host.ClientSize.Width - Width) / 2,
                host.ClientSize.Height / 2 - Height / 2 - (int)(24 * scale * progress)));
            Location = position;
            SmoothFrame.PaintBitmap(this, image, position, (byte)Math.Round(255 * (1 - fade)));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { animation.Dispose(); image.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
