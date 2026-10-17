using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickLauncher
{
    internal static class Theme
    {
        internal static readonly Color Main = ColorTranslator.FromHtml("#3C3F41");
        internal static readonly Color Surface = ColorTranslator.FromHtml("#2B2B2B");
        internal static readonly Color Text = Color.White;
        internal static readonly Color Muted = Color.FromArgb(199, 201, 203);
        internal static readonly Color Border = Color.FromArgb(84, 88, 91);
        internal static readonly Color Hover = Color.FromArgb(78, 83, 87);
        static readonly PrivateFontCollection Fonts = new PrivateFontCollection();
        static IntPtr fontMemory;
        internal static readonly string FontName = LoadFont();
        internal static readonly Cursor SystemHand = LoadSystemHand();

        static string LoadFont()
        {
            using (Stream stream = typeof(Theme).Assembly.GetManifestResourceStream("QuickLauncher.SmileySans"))
            using (var memory = new MemoryStream())
            {
                if (stream == null) throw new InvalidOperationException("Bundled Smiley Sans font is missing.");
                stream.CopyTo(memory);
                byte[] bytes = memory.ToArray();
                // Both GDI (TextRenderer/native inputs) and GDI+ need the process-local font.
                fontMemory = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, fontMemory, bytes.Length);
                Fonts.AddMemoryFont(fontMemory, bytes.Length);
                uint count = 0;
                if (AddFontMemResourceEx(fontMemory, (uint)bytes.Length, IntPtr.Zero, ref count) == IntPtr.Zero || count == 0)
                    throw new InvalidOperationException("Unable to load bundled Smiley Sans font.");
                return Fonts.Families[0].Name;
            }
        }
        internal static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            FontFamily family = Fonts.Families[0];
            return new Font(family, size, family.IsStyleAvailable(style) ? style : FontStyle.Regular, GraphicsUnit.Point);
        }
        static Cursor LoadSystemHand()
        {
            IntPtr handle = LoadCursor(IntPtr.Zero, new IntPtr(32649));
            return handle == IntPtr.Zero ? Cursors.Hand : new Cursor(handle);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr data, uint length, IntPtr reserved, ref uint count);

        internal static GraphicsPath Round(RectangleF rectangle, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
            var path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class SystemHandLinkLabel : LinkLabel
    {
        internal SystemHandLinkLabel() { LinkBehavior = LinkBehavior.AlwaysUnderline; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (PointInLink(e.X, e.Y) != null) Cursor.Current = Theme.SystemHand;
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != 0x20) return;
            Point pointer = PointToClient(Cursor.Position);
            if (PointInLink(pointer.X, pointer.Y) == null) return;
            Cursor.Current = Theme.SystemHand;
            message.Result = new IntPtr(1);
        }
    }

    internal sealed class RoundedButton : Button
    {
        bool hovered;
        bool pressed;
        internal bool Primary;

        internal RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = Theme.Text;
            BackColor = Theme.Surface;
            Cursor = Theme.SystemHand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Main : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Theme.Main : pressed ? Theme.Surface : hovered ? Theme.Hover : BackColor;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9 * e.Graphics.DpiX / 96f))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Primary && Enabled ? Color.FromArgb(145, 151, 155) : Theme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(139, 143, 146),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.HidePrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Theme.Muted, fill);
        }
    }

    internal sealed class ClearCheckBox : CheckBox
    {
        internal ClearCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            Size size = base.GetPreferredSize(proposedSize);
            float scale = IsHandleCreated ? DeviceDpi / 96f : 1f;
            return new Size(size.Width + (int)(10 * scale), Math.Max(size.Height, (int)(22 * scale)));
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Main : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96f;
            float top = (Height - 18 * scale) / 2;
            Color ink = Enabled ? ForeColor : SystemColors.GrayText;
            using (var box = Theme.Round(new RectangleF(scale, top, 18 * scale, 18 * scale), 3 * scale))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var border = new Pen(Theme.Muted, 1.5f * scale))
            {
                e.Graphics.FillPath(fill, box);
                e.Graphics.DrawPath(border, box);
            }
            if (Checked)
            {
                using (var pen = new Pen(Enabled ? Theme.Text : ink, 3 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    e.Graphics.DrawLines(pen, new[] {
                        new PointF(5 * scale, top + 9 * scale),
                        new PointF(9 * scale, top + 13 * scale),
                        new PointF(15 * scale, top + 5 * scale)
                    });
            }
            // Use the same center as the box, without extra native checkbox glyph padding.
            var textBounds = new Rectangle((int)(27 * scale), 0, Math.Max(0, Width - (int)(27 * scale)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.HidePrefix | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, textBounds, ink, BackColor);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        internal RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Padding = new Padding(10);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Main : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 13 * e.Graphics.DpiX / 96f))
            using (var brush = new SolidBrush(BackColor))
            using (var border = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class RoundedContextMenu : ContextMenuStrip
    {
        PopupSmoothFrame smoothFrame;
        internal bool HasSmoothFrame { get { return smoothFrame != null && smoothFrame.IsFrameVisible; } }
        internal RoundedContextMenu()
        {
            Font = Theme.UiFont(10.5F);
            BackColor = Theme.Main;
            ForeColor = Theme.Text;
            ShowImageMargin = false;
            ShowCheckMargin = false;
            DropShadowEnabled = false;
            Padding = new Padding(6);
            Renderer = new RoundedMenuRenderer();
        }
        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            float scale = DeviceDpi / 96f;
            Padding = new Padding((int)Math.Round(6 * scale));
            int itemWidth = (int)Math.Round(160 * scale);
            foreach (ToolStripItem item in Items)
                if (!(item is ToolStripSeparator))
                    itemWidth = Math.Max(itemWidth, TextRenderer.MeasureText(item.Text, Font).Width + (int)(32 * scale));
            foreach (ToolStripItem item in Items)
            {
                item.AutoSize = false;
                item.Size = new Size(itemWidth,
                    (int)((item is ToolStripSeparator ? 9 : 36) * scale));
            }
            PerformLayout();
            UpdateMenuRegion();
            base.OnOpening(e);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateMenuRegion();
            UpdateSmoothFrame();
        }
        void UpdateMenuRegion()
        {
            if (Width < 2 || Height < 2) return;
            Region old = Region;
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), 12 * DeviceDpi / 96f))
                Region = new Region(path);
            if (old != null) old.Dispose();
        }
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            try
            {
                smoothFrame = new PopupSmoothFrame();
                UpdateSmoothFrame();
            }
            catch (System.ComponentModel.Win32Exception) { DisposeSmoothFrame(); }
            catch (InvalidOperationException) { DisposeSmoothFrame(); }
            Invalidate();
        }
        void UpdateSmoothFrame()
        {
            if (smoothFrame == null) return;
            try { smoothFrame.Synchronize(this); }
            catch (System.ComponentModel.Win32Exception) { DisposeSmoothFrame(); }
            catch (InvalidOperationException) { DisposeSmoothFrame(); }
        }
        void DisposeSmoothFrame()
        {
            if (smoothFrame == null) return;
            smoothFrame.Dispose();
            smoothFrame = null;
        }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); UpdateSmoothFrame(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateSmoothFrame(); }
        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            DisposeSmoothFrame();
            base.OnClosed(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeSmoothFrame();
            base.Dispose(disposing);
        }
    }

    internal sealed class RoundedMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.Main);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var menu = e.ToolStrip as RoundedContextMenu;
            if (menu != null && menu.HasSmoothFrame) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 12 * e.ToolStrip.DeviceDpi / 96f))
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            using (var brush = new SolidBrush(Theme.Hover))
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            int inset = (int)(10 * e.ToolStrip.DeviceDpi / 96f);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
                new Rectangle(inset, 0, Math.Max(0, e.Item.Width - 2 * inset), e.Item.Height),
                e.Item.Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Surface, Math.Max(1, e.ToolStrip.DeviceDpi / 96f)))
                e.Graphics.DrawLine(pen, 6, e.Item.Height / 2, e.Item.Width - 7, e.Item.Height / 2);
        }
    }

    internal sealed class RoundedScrollBar : Control
    {
        int maximum, pageSize = 1, value;
        bool dragging, hovered;
        int dragOffset;
        int wheelRemainder;
        internal event MouseEventHandler ScrollWheel;
        internal event EventHandler ValueChanged;
        internal int Value { get { return value; } }
        internal Rectangle ThumbBounds
        {
            get
            {
                int inset = Math.Max(2, (int)Math.Round(3 * DeviceDpi / 96f));
                int track = Math.Max(1, Height - 2 * inset);
                int height = Math.Min(track, Math.Max((int)(24 * DeviceDpi / 96f),
                    (int)Math.Round(track * (double)pageSize / Math.Max(1, maximum + pageSize))));
                int top = inset + (maximum == 0 ? 0 : (int)Math.Round((track - height) * (double)value / maximum));
                return new Rectangle(inset, top, Math.Max(1, Width - 2 * inset), height);
            }
        }
        internal RoundedScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            TabStop = false;
            AccessibleName = "列表垂直滚动条";
            AccessibleRole = AccessibleRole.ScrollBar;
        }
        internal void Synchronize(int max, int page, int position)
        {
            maximum = Math.Max(0, max);
            pageSize = Math.Max(1, page);
            value = Math.Max(0, Math.Min(maximum, position));
            Visible = maximum > 0;
            Invalidate();
        }
        void SetValue(int next)
        {
            next = Math.Max(0, Math.Min(maximum, next));
            if (next == value) return;
            value = next;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Rectangle thumb = ThumbBounds;
            if (thumb.Contains(e.Location))
            {
                dragOffset = e.Y - thumb.Top;
                dragging = true;
                Capture = true;
                Invalidate();
            }
            else SetValue(value + (e.Y < thumb.Top ? -pageSize : pageSize));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            hovered = true;
            if (dragging)
            {
                Rectangle thumb = ThumbBounds;
                int inset = Math.Max(2, (int)Math.Round(3 * DeviceDpi / 96f));
                int travel = Height - 2 * inset - thumb.Height;
                if (travel > 0) SetValue((int)Math.Round((e.Y - dragOffset - inset) * (double)maximum / travel));
            }
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; Invalidate(); }
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (ScrollWheel != null) { ScrollWheel(this, e); return; }
            base.OnMouseWheel(e);
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines == 0) return;
            wheelRemainder += e.Delta;
            int steps = wheelRemainder / 120;
            wheelRemainder %= 120;
            SetValue(value - steps * (lines < 0 ? pageSize : lines));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle thumb = ThumbBounds;
            if (thumb.Width < 2 || thumb.Height < 2) return;
            using (var path = Theme.Round(new RectangleF(thumb.X + .5f, thumb.Y + .5f, thumb.Width - 1, thumb.Height - 1), thumb.Width / 2f))
            using (var brush = new SolidBrush(dragging || hovered ? Theme.Muted : Color.FromArgb(125, 130, 134)))
                e.Graphics.FillPath(brush, path);
        }
    }

    internal sealed partial class DarkListView : ListView
    {
        bool adjustingScroll;
        RoundedScrollBar scrollBar;
        internal void AttachScrollBar(RoundedScrollBar bar)
        {
            scrollBar = bar;
            Scrollable = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.UserMouse | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            bar.ValueChanged += delegate { SetPixelOffset(bar.Value); };
            bar.ScrollWheel += delegate(object sender, MouseEventArgs e) { SmoothWheel(e.Delta); };
            scrollAnimation.Tick += AnimateScroll;
            SynchronizePixelScroll();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (scrollBar == null) { base.OnMouseWheel(e); return; }
            SmoothWheel(e.Delta);
        }
        internal DarkListView()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowTheme(Handle, "DarkMode_Explorer", null);
            SynchronizePixelScroll();
        }
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr handle, string application, string classes);
        [DllImport("user32.dll")]
        static extern bool ShowScrollBar(IntPtr handle, int bar, bool show);
        [DllImport("user32.dll")]
        static extern int GetScrollPos(IntPtr handle, int bar);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        protected override void WndProc(ref Message message)
        {
            if (scrollBar != null && HandlePixelListMessage(ref message)) return;
            base.WndProc(ref message);
            // Columns fit the viewport; native list-view scroll ranges can lag behind a resize.
            if (scrollBar == null && !adjustingScroll && (message.Msg == 0x5 || message.Msg == 0xF))
            {
                adjustingScroll = true;
                try
                {
                    ShowScrollBar(Handle, 0, false);
                }
                finally { adjustingScroll = false; }
            }
            if (scrollBar != null && (message.Msg == 0x5 || message.Msg == 0x1007 || message.Msg == 0x1008 || message.Msg == 0x1009
                || message.Msg == 0x102B || message.Msg == 0x101E || message.Msg == 0x104D))
            {
                QueueViewportUpdate();
            }
        }
        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            DrawCellText(e.Graphics, e.Header.Text, Font, e.Bounds, Theme.Muted);
        }
        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            if (View != View.Details) { e.DrawDefault = true; return; }
            // Native hover redraws can issue DrawItem without DrawSubItem. Always paint the complete row.
            Rectangle row = GetItemRect(e.Item.Index, ItemBoundsPortion.Entire);
            row.X = 0;
            row.Width = ClientSize.Width;
            using (var brush = new SolidBrush(e.Item.Selected ? Theme.Hover : BackColor))
                e.Graphics.FillRectangle(brush, row);
            for (int column = 0; column < Columns.Count && column < e.Item.SubItems.Count; column++)
                DrawCell(e.Graphics, e.Item, column);
        }
        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            DrawCell(e.Graphics, e.Item, e.ColumnIndex);
        }
        void DrawCell(Graphics graphics, ListViewItem item, int column)
        {
            // The native first-subitem bounds change by a few pixels on hover (icon/label insets).
            // Derive every cell from the full row and column widths so its text origin stays fixed.
            Rectangle bounds = GetItemRect(item.Index, ItemBoundsPortion.Entire);
            bounds.X = -GetScrollPos(Handle, 0);
            foreach (ColumnHeader header in Columns)
                if (header.DisplayIndex < Columns[column].DisplayIndex) bounds.X += header.Width;
            bounds.Width = Columns[column].Width;
            Color fill = item.Selected ? Theme.Hover : BackColor;
            // Hover custom-draw HDCs can report 96 DPI while normal paints report the monitor DPI.
            // Render into a bitmap with an explicit DPI so both glyphs and padding remain identical.
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                bitmap.SetResolution(DeviceDpi, DeviceDpi);
                using (Graphics cell = Graphics.FromImage(bitmap))
                {
                    cell.Clear(fill);
                    DrawCellText(cell, item.SubItems[column].Text, Font, new Rectangle(Point.Empty, bitmap.Size), item.ForeColor, DeviceDpi);
                }
                graphics.DrawImage(bitmap, bounds, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel);
            }
        }
        internal static void DrawCellText(Graphics graphics, string text, Font font, Rectangle bounds, Color color, int dpi = 0)
        {
            // Native hover notifications can supply a 96-DPI HDC on a scaled monitor.
            int padding = (int)Math.Round(10 * (dpi > 0 ? dpi : graphics.DpiX) / 96f);
            TextRenderer.DrawText(graphics, text, font, Rectangle.Inflate(bounds, -padding, 0), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        }
    }

    public class RoundedForm : Form
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                // Borderless windows still need native system styles for taskbar minimize/restore.
                if (MinimizeBox) parameters.Style |= 0x00080000 | 0x00020000;
                if (MaximizeBox) parameters.Style |= 0x00010000;
                return parameters;
            }
        }
        protected readonly Panel Body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Main };
        readonly Label captionText;
        readonly Panel captionIcon;
        Image captionArtwork;
        Size captionArtworkSize;
        readonly Button minimize;
        readonly Button maximize;
        readonly Panel caption;
        SmoothFrame smoothFrame;
        bool captionDragPending;
        Point captionDragStart;

        public RoundedForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Main;
            ForeColor = Theme.Text;
            // Keep a real window edge exposed so native resize hit-testing works around child panels.
            Padding = new Padding(7);
            caption = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Main };
            captionText = new Label { AutoSize = true, Location = new Point(54, 14), ForeColor = Theme.Muted, Font = Theme.UiFont(9F) };
            captionIcon = new Panel { Name = "CaptionIcon", Location = new Point(14, 5), Size = new Size(32, 32) };
            captionIcon.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Main);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Theme.Round(new RectangleF(.5f, .5f, captionIcon.Width - 1, captionIcon.Height - 1), 8 * captionIcon.DeviceDpi / 96f))
                using (var backdrop = new SolidBrush(Color.FromArgb(111, 116, 120))) e.Graphics.FillPath(backdrop, path);
                if (Icon == null) return;
                if (captionArtwork == null || captionArtworkSize != captionIcon.Size)
                {
                    int frameSize = 256;
                    foreach (int available in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
                        if (available >= Math.Max(captionIcon.Width, captionIcon.Height)) { frameSize = available; break; }
                    using (Stream stream = typeof(RoundedForm).Assembly.GetManifestResourceStream("QuickLauncher.AppIcon"))
                    using (var artworkIcon = new Icon(stream, new Size(frameSize, frameSize)))
                    {
                        if (captionArtwork != null) captionArtwork.Dispose();
                        captionArtwork = artworkIcon.ToBitmap();
                        captionArtworkSize = captionIcon.Size;
                    }
                }
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
                e.Graphics.DrawImage(captionArtwork, captionIcon.ClientRectangle, 0, 0, captionArtwork.Width, captionArtwork.Height, GraphicsUnit.Pixel);
            };
            caption.Controls.Add(captionText);
            caption.Controls.Add(captionIcon);
            var close = CaptionButton("×", "关闭", delegate { Close(); });
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(157, 65, 65);
            maximize = CaptionButton("□", "最大化或还原", delegate { ToggleMaximize(); });
            minimize = CaptionButton("−", "最小化", delegate { WindowState = FormWindowState.Minimized; });
            caption.Controls.Add(minimize);
            caption.Controls.Add(maximize);
            caption.Controls.Add(close);
            caption.MouseDown += DragWindow;
            captionText.MouseDown += DragWindow;
            captionIcon.MouseDown += DragWindow;
            foreach (Control surface in new Control[] { caption, captionText, captionIcon })
            {
                surface.MouseMove += ContinueCaptionDrag;
                surface.MouseUp += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left) return;
                    captionDragPending = false;
                    ((Control)sender).Capture = false;
                };
                surface.MouseCaptureChanged += delegate(object sender, EventArgs e) { if (!((Control)sender).Capture) captionDragPending = false; };
            }
            caption.DoubleClick += delegate { ToggleMaximize(); };
            captionText.DoubleClick += delegate { ToggleMaximize(); };
            Controls.Add(Body);
            Controls.Add(caption);
        }
        Button CaptionButton(string text, string name, EventHandler click)
        {
            var button = new Button { Text = text, AccessibleName = name, Dock = DockStyle.Right, Width = 42,
                FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Main, TabStop = false,
                Font = new Font("Segoe UI", 12F), Cursor = Theme.SystemHand };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Theme.Hover;
            button.Click += click;
            return button;
        }
        void ToggleMaximize()
        {
            if (!MaximizeBox) return;
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }
        void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            if (WindowState == FormWindowState.Maximized)
            {
                captionDragStart = ((Control)sender).PointToScreen(e.Location);
                captionDragPending = true;
                ((Control)sender).Capture = true;
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }
        void ContinueCaptionDrag(object sender, MouseEventArgs e)
        {
            if (!captionDragPending || e.Button != MouseButtons.Left) return;
            Point pointer = ((Control)sender).PointToScreen(e.Location);
            if (!TryRestoreCaptionDrag(captionDragStart, pointer)) return;
            captionDragPending = false;
            ReleaseCapture();
            // Hand the remaining drag to Windows, retaining snapping and normal window movement.
            SendMessage(Handle, 0xA1, new IntPtr(2), new IntPtr((pointer.Y << 16) | (pointer.X & 0xFFFF)));
        }
        internal bool TryRestoreCaptionDrag(Point pressed, Point pointer)
        {
            if (WindowState != FormWindowState.Maximized) return false;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(pointer.X - pressed.X) < Math.Max(1, threshold.Width / 2)
                && Math.Abs(pointer.Y - pressed.Y) < Math.Max(1, threshold.Height / 2)) return false;
            double horizontalAnchor = Math.Max(0, Math.Min(1, (pressed.X - Left) / (double)Math.Max(1, Width)));
            int verticalAnchor = Math.Max(0, Math.Min(caption.Bottom, pressed.Y - Top));
            WindowState = FormWindowState.Normal;
            Location = new Point(pointer.X - (int)Math.Round(Width * horizontalAnchor), pointer.Y - verticalAnchor);
            return true;
        }
        protected override void OnLoad(EventArgs e)
        {
            captionText.Text = Text;
            captionIcon.Invalidate();
            minimize.Visible = MinimizeBox;
            maximize.Visible = MaximizeBox;
            base.OnLoad(e);
        }
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (captionText != null) captionText.Text = Text;
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Opacity > 0)
            {
                smoothFrame = new SmoothFrame();
                UpdateSmoothFrame();
            }
        }
        void UpdateSmoothFrame()
        {
            if (smoothFrame == null || smoothFrame.IsDisposed) return;
            try { smoothFrame.Synchronize(this); }
            catch (System.ComponentModel.Win32Exception) { DisableSmoothFrame(); }
            catch (InvalidOperationException) { DisableSmoothFrame(); }
        }
        void DisableSmoothFrame()
        {
            SmoothFrame failed = smoothFrame;
            smoothFrame = null;
            if (failed != null) failed.Dispose();
        }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); UpdateSmoothFrame(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateSmoothFrame(); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            int border = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
            int nonClientRendering = 1;
            DwmSetWindowAttribute(Handle, 2, ref nonClientRendering, sizeof(int));
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            Region old = Region;
            if (WindowState == FormWindowState.Maximized) Region = null;
            else using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), 16 * DeviceScale)) Region = new Region(path);
            if (old != null) old.Dispose();
            Invalidate(true);
            UpdateSmoothFrame();
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Fill the entire frame, including the resize margin, with our own dark surface.
            e.Graphics.Clear(Theme.Main);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), 16 * DeviceScale - 1))
            using (var pen = new Pen(Theme.Surface)) e.Graphics.DrawPath(pen, path);
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != 0x84 || message.Result != new IntPtr(1) || WindowState != FormWindowState.Normal || !MaximizeBox) return;
            Point point = PointToClient(new Point(unchecked((short)message.LParam.ToInt64()), unchecked((short)(message.LParam.ToInt64() >> 16))));
            int grip = Math.Max(6, (int)(8 * DeviceScale));
            bool left = point.X < grip, right = point.X >= Width - grip, top = point.Y < grip, bottom = point.Y >= Height - grip;
            int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1;
            message.Result = new IntPtr(hit);
        }
        internal float DeviceScale { get { return IsHandleCreated ? DeviceDpi / 96f : 1f; } }
        protected override void Dispose(bool disposing)
        {
            if (disposing && smoothFrame != null) { smoothFrame.Dispose(); smoothFrame = null; }
            if (disposing && captionArtwork != null) { captionArtwork.Dispose(); captionArtwork = null; }
            base.Dispose(disposing);
        }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
