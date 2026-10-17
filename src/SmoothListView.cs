using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickLauncher
{
    // Keep ListView's item/selection model, but give the viewport a single paint and pixel scroll owner.
    internal sealed partial class DarkListView
    {
        readonly Timer scrollAnimation = new Timer { Interval = 15 };
        readonly ToolTip itemTip = new ToolTip();
        int pixelOffset, scrollTarget, selectionAnchor = -1, tipIndex = -1;
        bool synchronizingScroll, marquee;
        bool viewportUpdatePending;
        Point marqueeStart, marqueeEnd;
        internal int PixelOffset { get { return pixelOffset; } }
        internal int PixelRowHeight
        {
            get
            {
                float scale = DeviceDpi / 96f;
                return Math.Max(1, Math.Max((int)Math.Round((SmallImageList == null ? 0 : SmallImageList.ImageSize.Height) * scale),
                    Font.Height + (int)Math.Round(10 * scale)));
            }
        }
        int MaxPixelOffset { get { return (int)Math.Max(0L, Math.Min(int.MaxValue, (long)Items.Count * PixelRowHeight - ClientSize.Height)); } }
        Rectangle PixelRowBounds(int index) { return new Rectangle(0, index * PixelRowHeight - pixelOffset, ClientSize.Width, PixelRowHeight); }
        void QueueViewportUpdate()
        {
            if (!IsHandleCreated || IsDisposed || Disposing || viewportUpdatePending) return;
            viewportUpdatePending = true;
            BeginInvoke((MethodInvoker)delegate
            {
                viewportUpdatePending = false;
                if (IsDisposed || Disposing) return;
                SynchronizePixelScroll();
                Invalidate();
            });
        }
        void SynchronizePixelScroll()
        {
            if (scrollBar == null || scrollBar.IsDisposed || synchronizingScroll) return;
            synchronizingScroll = true;
            try
            {
                pixelOffset = Math.Min(pixelOffset, MaxPixelOffset);
                scrollTarget = Math.Min(scrollTarget, MaxPixelOffset);
                scrollBar.Synchronize(MaxPixelOffset, Math.Max(1, ClientSize.Height), pixelOffset);
            }
            finally { synchronizingScroll = false; }
        }
        void SetPixelOffset(int position)
        {
            scrollAnimation.Stop();
            scrollTarget = Math.Max(0, Math.Min(MaxPixelOffset, position));
            pixelOffset = scrollTarget;
            SynchronizePixelScroll();
            itemTip.Hide(this);
            Invalidate();
        }
        void SmoothWheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines == 0) return;
            double distance = (lines < 0 ? ClientSize.Height : lines * PixelRowHeight) * delta / 120.0;
            scrollTarget = Math.Max(0, Math.Min(MaxPixelOffset, scrollTarget - (int)Math.Round(distance)));
            if (scrollTarget != pixelOffset) scrollAnimation.Start();
            itemTip.Hide(this);
        }
        void AnimateScroll(object sender, EventArgs e)
        {
            int distance = scrollTarget - pixelOffset;
            if (Math.Abs(distance) <= 1) { pixelOffset = scrollTarget; scrollAnimation.Stop(); }
            else pixelOffset += Math.Sign(distance) * Math.Max(1, (int)Math.Round(Math.Abs(distance) * .35));
            SynchronizePixelScroll();
            Invalidate();
        }
        void EnsurePixelRowVisible(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            int top = index * PixelRowHeight;
            if (top < pixelOffset) SetPixelOffset(top);
            else if (top + PixelRowHeight > pixelOffset + ClientSize.Height)
                SetPixelOffset(top + PixelRowHeight - ClientSize.Height);
        }
        bool HandlePixelListMessage(ref Message message)
        {
            int index = unchecked((int)message.WParam.ToInt64());
            if (message.Msg >= 0x200 && message.Msg <= 0x206)
            {
                int x = unchecked((short)message.LParam.ToInt64());
                int y = unchecked((short)(message.LParam.ToInt64() >> 16));
                MouseButtons button = message.Msg >= 0x204 ? MouseButtons.Right : MouseButtons.Left;
                var mouse = new MouseEventArgs(button, 1, x, y, 0);
                if (message.Msg == 0x200)
                {
                    button = (index & 1) != 0 ? MouseButtons.Left : (index & 2) != 0 ? MouseButtons.Right : MouseButtons.None;
                    OnMouseMove(new MouseEventArgs(button, 0, x, y, 0));
                }
                else if (message.Msg == 0x201 || message.Msg == 0x204)
                {
                    if (button == MouseButtons.Left) Capture = true;
                    OnMouseDown(mouse);
                }
                else if (message.Msg == 0x202 || message.Msg == 0x205)
                {
                    OnMouseUp(mouse);
                    Capture = false;
                    if (ClientRectangle.Contains(x, y))
                    {
                        OnMouseClick(mouse);
                        if (button == MouseButtons.Left) OnClick(EventArgs.Empty);
                        else if (ContextMenuStrip != null) ContextMenuStrip.Show(this, new Point(x, y));
                    }
                }
                else { OnDoubleClick(EventArgs.Empty); OnMouseDoubleClick(new MouseEventArgs(button, 2, x, y, 0)); }
                message.Result = IntPtr.Zero;
                return true;
            }
            switch (message.Msg)
            {
                case 0x14: message.Result = new IntPtr(1); return true; // No native background erase between complete frames.
                case 0xF: Invalidate(); break; // Expand native partial-row invalidations to the whole buffered viewport.
                case 0x1027: message.Result = new IntPtr(pixelOffset / PixelRowHeight); return true; // GETTOPINDEX
                case 0x1028: message.Result = new IntPtr(Math.Max(1, ClientSize.Height / PixelRowHeight)); return true;
                case 0x1013: EnsurePixelRowVisible(index); message.Result = new IntPtr(1); return true;
                case 0x1014: SetPixelOffset(pixelOffset + unchecked((int)message.LParam.ToInt64())); message.Result = new IntPtr(1); return true;
                case 0x100E: // GETITEMRECT: preserve the public item geometry API in pixel-scrolling mode.
                    if (index < 0 || index >= Items.Count || message.LParam == IntPtr.Zero) break;
                    Rectangle row = PixelRowBounds(index);
                    Marshal.Copy(new[] { row.Left, row.Top, row.Right, row.Bottom }, 0, message.LParam, 4);
                    message.Result = new IntPtr(1);
                    return true;
            }
            return false;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (scrollBar == null) base.OnPaintBackground(e);
            // The complete background and all visible rows are committed together in OnPaint.
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (scrollBar == null) { base.OnPaint(e); return; }
            SynchronizePixelScroll();
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            using (var frame = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                frame.SetResolution(DeviceDpi, DeviceDpi);
                using (Graphics graphics = Graphics.FromImage(frame))
                {
                    graphics.Clear(BackColor);
                    int first = pixelOffset / PixelRowHeight;
                    int last = Math.Min(Items.Count, (pixelOffset + ClientSize.Height + PixelRowHeight - 1) / PixelRowHeight);
                    for (int index = first; index < last; index++)
                    {
                        ListViewItem item = Items[index];
                        // Native insertion can request a paint before WinForms registers the item object.
                        if (item == null) { QueueViewportUpdate(); continue; }
                        Rectangle row = PixelRowBounds(index);
                        if (item.Selected) using (var fill = new SolidBrush(Theme.Hover)) graphics.FillRectangle(fill, row);
                        int left = 0;
                        for (int display = 0; display < Columns.Count; display++)
                            foreach (ColumnHeader column in Columns)
                                if (column.DisplayIndex == display)
                                {
                                    if (column.Index < item.SubItems.Count)
                                    {
                                        Rectangle cell = new Rectangle(left, row.Top, column.Width, row.Height);
                                        var saved = graphics.Save();
                                        graphics.SetClip(cell, System.Drawing.Drawing2D.CombineMode.Intersect);
                                        DrawCellText(graphics, item.SubItems[column.Index].Text, Font, cell, item.ForeColor, DeviceDpi);
                                        graphics.Restore(saved);
                                    }
                                    left += column.Width;
                                }
                    }
                    if (marquee) using (var pen = new Pen(Theme.Muted)) graphics.DrawRectangle(pen, MarqueeBounds());
                }
                e.Graphics.DrawImage(frame, ClientRectangle, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel);
            }
        }
        int RowAt(Point point)
        {
            if (!ClientRectangle.Contains(point)) return -1;
            int index = (point.Y + pixelOffset) / PixelRowHeight;
            return index >= 0 && index < Items.Count ? index : -1;
        }
        void SelectPixelRow(int index, bool control, bool shift, bool rightClick)
        {
            if (rightClick && index >= 0 && Items[index].Selected) return;
            if (!MultiSelect) { control = false; shift = false; }
            if (!control) foreach (ListViewItem item in Items) item.Selected = false;
            if (index < 0) { Invalidate(); return; }
            if (shift && selectionAnchor >= 0 && selectionAnchor < Items.Count)
            {
                for (int row = Math.Min(index, selectionAnchor); row <= Math.Max(index, selectionAnchor); row++) Items[row].Selected = true;
            }
            else { Items[index].Selected = control && !rightClick ? !Items[index].Selected : true; selectionAnchor = index; }
            Items[index].Focused = true;
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (scrollBar == null) { base.OnMouseDown(e); return; }
            Focus();
            scrollAnimation.Stop(); scrollTarget = pixelOffset;
            int index = RowAt(e.Location);
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                SelectPixelRow(index, (ModifierKeys & Keys.Control) != 0, (ModifierKeys & Keys.Shift) != 0, e.Button == MouseButtons.Right);
            if (e.Button == MouseButtons.Left && index < 0 && MultiSelect)
            {
                marquee = true; marqueeStart = marqueeEnd = new Point(e.X, e.Y + pixelOffset); Capture = true;
            }
            base.OnMouseDown(e);
        }
        Rectangle MarqueeBounds()
        {
            return Rectangle.FromLTRB(Math.Min(marqueeStart.X, marqueeEnd.X), Math.Min(marqueeStart.Y, marqueeEnd.Y) - pixelOffset,
                Math.Max(marqueeStart.X, marqueeEnd.X), Math.Max(marqueeStart.Y, marqueeEnd.Y) - pixelOffset);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (scrollBar == null) return;
            if (marquee)
            {
                marqueeEnd = new Point(e.X, e.Y + pixelOffset);
                Rectangle bounds = MarqueeBounds();
                for (int index = 0; index < Items.Count; index++) Items[index].Selected = bounds.IntersectsWith(PixelRowBounds(index));
                Invalidate();
            }
            int hovered = RowAt(e.Location);
            if (hovered != tipIndex)
            {
                tipIndex = hovered;
                itemTip.SetToolTip(this, hovered < 0 ? "" : Items[hovered].ToolTipText);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (marquee && e.Button == MouseButtons.Left) { marquee = false; Capture = false; Invalidate(); }
            base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { marquee = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
        protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e) { base.OnColumnWidthChanged(e); if (scrollBar != null) Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); SynchronizePixelScroll(); Invalidate(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); SynchronizePixelScroll(); Invalidate(); }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return scrollBar != null && (key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown) || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (scrollBar != null && Items.Count > 0)
            {
                int current = FocusedItem == null ? 0 : FocusedItem.Index;
                int next = current;
                int page = Math.Max(1, ClientSize.Height / PixelRowHeight);
                switch (e.KeyCode)
                {
                    case Keys.Up: next--; break;
                    case Keys.Down: next++; break;
                    case Keys.Home: next = 0; break;
                    case Keys.End: next = Items.Count - 1; break;
                    case Keys.PageUp: next -= page; break;
                    case Keys.PageDown: next += page; break;
                    case Keys.A:
                        if (e.Control && MultiSelect) { foreach (ListViewItem item in Items) item.Selected = true; Invalidate(); e.Handled = true; }
                        break;
                }
                if (next != current || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
                {
                    next = Math.Max(0, Math.Min(Items.Count - 1, next));
                    if (e.Control && !e.Shift) Items[next].Focused = true;
                    else SelectPixelRow(next, e.Control, e.Shift, false);
                    EnsurePixelRowVisible(next);
                    e.Handled = true;
                }
            }
            base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { scrollAnimation.Dispose(); itemTip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
