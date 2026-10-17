using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickLauncher
{
    static class LauncherTests
    {
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")]
        static extern int DwmFlush();
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        static void DisableTestWindowAnimation(Form form)
        {
            int disabled = 1;
            DwmSetWindowAttribute(form.Handle, 3, ref disabled, sizeof(int));
        }
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        static void CheckFileDropTargets(Control control, string session, string config, ref int count)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            string file = Path.Combine(session, "drop-" + count + ".txt");
            File.WriteAllText(file, "drop source");
            var data = new DataObject(DataFormats.FileDrop, new[] { file });
            var enter = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragEnter", flags).Invoke(control, new object[] { enter });
            Check(control.AllowDrop && enter.Effect == DragDropEffects.Copy, "窗口区域不接受文件：" + control.GetType().Name);
            typeof(Control).GetMethod("OnDragDrop", flags).Invoke(control, new object[] { enter });
            count++;
            Check(Launcher.Load(config).Count == count && File.ReadAllText(file) == "drop source", "窗口区域拖入失败或修改了源文件");
            var text = new DragEventArgs(new DataObject(DataFormats.Text, "text"), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
            typeof(Control).GetMethod("OnDragEnter", flags).Invoke(control, new object[] { text });
            Check(text.Effect == DragDropEffects.None, "非文件拖入未被忽略");
            foreach (Control child in control.Controls) CheckFileDropTargets(child, session, config, ref count);
        }
        static T Wait<T>(Task<T> task)
        {
            while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            return task.GetAwaiter().GetResult();
        }
        static void Wait(Task task)
        {
            while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            task.GetAwaiter().GetResult();
        }
        static Bitmap CaptureListRow(ListView list, int index)
        {
            Rectangle row = list.GetItemRect(index);
            row.X = 0;
            row.Width = list.ClientSize.Width;
            var bitmap = new Bitmap(row.Width, row.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(list.PointToScreen(row.Location), Point.Empty, row.Size);
            return bitmap;
        }
        static void CheckSamePixels(Bitmap before, Bitmap after, string message)
        {
            for (int y = 0; y < before.Height; y++)
                for (int x = 0; x < before.Width; x++)
                    Check(before.GetPixel(x, y) == after.GetPixel(x, y), message);
        }
        static void CheckMenuHitTargets(string work)
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            using (Bitmap rim = SmoothFrame.Render(new Size((int)(220 * scale), (int)(110 * scale)), scale, 12, true))
            {
                Check(rim.GetPixel(0, 0).A == 0 && rim.GetPixel(rim.Width / 2, rim.Height / 2).A == 0,
                    "菜单平滑边框的外侧或内部未透明");
                bool partialAlpha = false;
                for (int y = 0; y < (int)(20 * scale); y++)
                    for (int x = 0; x < (int)(20 * scale); x++)
                    {
                        int alpha = rim.GetPixel(x, y).A;
                        if (alpha > 0 && alpha < 255) partialAlpha = true;
                    }
                Check(partialAlpha, "菜单圆角缺少半透明抗锯齿边缘：" + scale);
                if (scale == 1.25f) rim.Save(Path.Combine(work, "menu-smooth-rim-preview.png"));
            }
            using (var menu = new RoundedContextMenu())
            {
                bool clicked = false;
                menu.Items.Add("打开编辑窗口");
                menu.Items.Add(new ToolStripSeparator());
                var exit = menu.Items.Add("退出", null, delegate { clicked = true; });
                menu.Show(new Point(80, 80));
                Application.DoEvents();
                Check(menu.HasSmoothFrame, "菜单打开后未显示平滑边框");
                using (var screen = new Bitmap(menu.Width + 6, menu.Height + 6))
                using (Graphics graphics = Graphics.FromImage(screen))
                {
                    graphics.CopyFromScreen(menu.Left - 3, menu.Top - 3, 0, 0, screen.Size);
                    screen.Save(Path.Combine(work, "menu-smooth-screen-preview.png"));
                }
                Check(exit.Width == menu.Items[0].Width, "短菜单项没有铺满整行");
                Point right = new Point(menu.ClientSize.Width - menu.Padding.Right - 3, exit.Bounds.Top + exit.Height / 2);
                Check(menu.GetItemAt(right) == exit, "菜单项右侧空白区域无法命中");
                IntPtr position = new IntPtr((right.Y << 16) | right.X);
                // Verify rendering separately from hit-testing; synthetic mouse moves are ignored
                // by ToolStrip's global menu filter when a self-test has no active owner window.
                exit.Select();
                Application.DoEvents();
                using (var bitmap = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    Check(bitmap.GetPixel(right.X, exit.Bounds.Top + 3).ToArgb() == Theme.Hover.ToArgb(), "菜单高亮没有覆盖整行右侧");
                    bitmap.Save(Path.Combine(work, "menu-full-row-preview.png"));
                }
                SendMessage(menu.Handle, 0x201, new IntPtr(1), position);
                SendMessage(menu.Handle, 0x202, IntPtr.Zero, position);
                Application.DoEvents();
                Check(clicked, "点击菜单项右侧空白区域没有执行操作");
                Check(!menu.HasSmoothFrame, "菜单关闭后残留平滑边框");
                menu.Show(new Point(100, 100));
                Application.DoEvents();
                Check(menu.HasSmoothFrame, "菜单重新打开后未恢复平滑边框");
                menu.Close();
                Check(!menu.HasSmoothFrame, "取消菜单后残留平滑边框");
            }
        }
        static void MouseEvent(Control control, string method, MouseButtons button, int x, int y, int delta = 0)
        {
            typeof(Control).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(control, new object[] { new MouseEventArgs(button, 1, x, y, delta) });
            Application.DoEvents();
        }
        static void CheckViewportFrame(DarkListView list, string stage)
        {
            DwmFlush();
            using (var screen = new Bitmap(list.ClientSize.Width, list.ClientSize.Height))
            using (var expected = new Bitmap(list.ClientSize.Width, list.ClientSize.Height))
            {
                using (Graphics graphics = Graphics.FromImage(screen))
                    graphics.CopyFromScreen(list.PointToScreen(Point.Empty), Point.Empty, screen.Size);
                list.DrawToBitmap(expected, new Rectangle(Point.Empty, expected.Size));
                string work = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tests", "work"));
                screen.Save(Path.Combine(work, "viewport-screen.png"));
                expected.Save(Path.Combine(work, "viewport-expected.png"));
                CheckSamePixels(expected, screen, "屏幕可见内容不完整或出现黑块：" + stage);
                int fullRows = 0;
                for (int index = 0; index < list.Items.Count; index++)
                {
                    Rectangle row = list.GetItemRect(index);
                    if (row.Top < 0 || row.Bottom > list.ClientSize.Height) continue;
                    int textPixels = 0;
                    for (int y = row.Top; y < row.Bottom; y++)
                        for (int x = 0; x < Math.Min(200, screen.Width); x++)
                        {
                            Color pixel = screen.GetPixel(x, y);
                            if (pixel.R > 200 && pixel.G > 200 && pixel.B > 200) textPixels++;
                        }
                    Check(textPixels > 10, "可见行缺少文字：" + stage + " row=" + index);
                    fullRows++;
                }
                Check(fullRows > 0, "重绘检查没有覆盖任何完整可见行");
            }
        }
        static void CheckRoundedScrolling(string work)
        {
            using (var host = new Form { ClientSize = new Size(600, 260), ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.CenterScreen })
            using (var list = new DarkListView { Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None, Font = Theme.UiFont(10.5F) })
            using (var bar = new RoundedScrollBar { Dock = DockStyle.Right, Width = 18, Visible = false })
            {
                list.Columns.Add("名称", 550);
                host.Controls.Add(list);
                host.Controls.Add(bar);
                list.AttachScrollBar(bar);
                for (int i = 0; i < 80; i++) list.Items.Add("滚动测试项目 " + i);
                DisableTestWindowAnimation(host);
                host.Show();
                host.Hide();
                host.Show();
                Application.DoEvents();
                Check(bar.Visible && bar.Value == 0, "长列表未显示圆角滚动条");
                CheckViewportFrame(list, "首次打开");
                var paintMethod = typeof(DarkListView).GetMethod("OnPaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Bitmap dpiReference = null;
                try
                {
                    foreach (int dpi in new[] { 96, 120, 144, 192 })
                    using (var rendered = new Bitmap(list.ClientSize.Width, list.ClientSize.Height))
                    {
                        rendered.SetResolution(dpi, dpi);
                        using (Graphics graphics = Graphics.FromImage(rendered))
                            paintMethod.Invoke(list, new object[] { new PaintEventArgs(graphics, list.ClientRectangle) });
                        if (dpiReference == null) dpiReference = (Bitmap)rendered.Clone();
                        else CheckSamePixels(dpiReference, rendered, "整块视口绘制在不同 DPI 下出现偏移：" + dpi);
                    }
                }
                finally { if (dpiReference != null) dpiReference.Dispose(); }
                SendMessage(list.Handle, 0x200, IntPtr.Zero, new IntPtr((15 << 16) | 50));
                Application.DoEvents();
                list.Update();
                CheckViewportFrame(list, "鼠标悬停");
                Rectangle thumb = bar.ThumbBounds;
                MouseEvent(bar, "OnMouseDown", MouseButtons.Left, bar.Width / 2, thumb.Top + 3);
                MouseEvent(bar, "OnMouseMove", MouseButtons.Left, bar.Width / 2, bar.Height + 100);
                MouseEvent(bar, "OnMouseUp", MouseButtons.Left, bar.Width / 2, bar.Height + 100);
                Check(list.TopItem.Index > 0 && list.Items[79].Bounds.Bottom <= list.ClientSize.Height,
                    "拖动滑块到底部未显示最后一项：top=" + list.TopItem.Index + " bottom=" + list.Items[79].Bounds.Bottom + " height=" + list.ClientSize.Height + " bar=" + bar.Value);
                Check(bar.Value == list.PixelOffset, "拖动后滑块与列表位置不一致");
                thumb = bar.ThumbBounds;
                MouseEvent(bar, "OnMouseDown", MouseButtons.Left, bar.Width / 2, thumb.Top + 3);
                MouseEvent(bar, "OnMouseMove", MouseButtons.Left, bar.Width / 2, -100);
                MouseEvent(bar, "OnMouseUp", MouseButtons.Left, bar.Width / 2, -100);
                Check(list.TopItem.Index == 0 && bar.Value == 0, "拖动滑块到顶部失败");
                SendMessage(list.Handle, 0x1014, IntPtr.Zero, new IntPtr(1));
                Check(list.PixelOffset == 1 && list.Items[0].Bounds.Top == -1 && bar.Value == 1, "滚动仍然按整行跳动，不能移动单个像素");
                SendMessage(list.Handle, 0x1014, IntPtr.Zero, new IntPtr(-1));
                MouseEvent(list, "OnMouseWheel", MouseButtons.None, 50, 50, -120);
                var smoothTimer = Stopwatch.StartNew();
                bool intermediateFrame = false;
                int previousOffset = list.PixelOffset;
                while (smoothTimer.ElapsedMilliseconds < 350)
                {
                    Application.DoEvents();
                    if (list.PixelOffset > 0 && list.PixelOffset % list.PixelRowHeight != 0) intermediateFrame = true;
                    Check(list.PixelOffset >= previousOffset, "滚轮动画反向跳动");
                    previousOffset = list.PixelOffset;
                    System.Threading.Thread.Sleep(5);
                }
                if (SystemInformation.MouseWheelScrollLines != 0)
                    Check(list.TopItem.Index > 0 && bar.Value == list.PixelOffset && intermediateFrame, "鼠标滚轮缺少逐像素平滑过渡或未同步滚动条");
                list.Items[79].EnsureVisible();
                Application.DoEvents();
                Check(bar.Value == list.PixelOffset && bar.Value > 0, "键盘定位使用的原生滚动未同步滑块");
                for (int step = 0; step < 20; step++)
                {
                    SendMessage(list.Handle, 0x1014, IntPtr.Zero, new IntPtr(step % 2 == 0 ? -37 : 17));
                    Application.DoEvents();
                    list.Update();
                    CheckViewportFrame(list, "快速反复滚动 " + step);
                }
                using (var preview = new Bitmap(host.Width, host.Height))
                {
                    host.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                    preview.Save(Path.Combine(work, "scrollbar-preview.png"));
                }
                host.ClientSize = new Size(600, 400);
                Application.DoEvents();
                Check(bar.Value == list.PixelOffset, "调整窗口大小后滑块未同步");
                CheckViewportFrame(list, "调整窗口大小");
                host.Hide(); host.Show(); Application.DoEvents();
                CheckViewportFrame(list, "隐藏后重新打开");
                SendMessage(list.Handle, 0x1014, IntPtr.Zero, new IntPtr(1 - list.PixelOffset));
                Point click = new Point(60, list.PixelRowHeight * 2 + 2);
                IntPtr location = new IntPtr((click.Y << 16) | click.X);
                SendMessage(list.Handle, 0x201, new IntPtr(1), location);
                SendMessage(list.Handle, 0x202, IntPtr.Zero, location);
                Check(list.SelectedIndices.Count == 1 && list.SelectedIndices[0] == 2, "像素滚动后点击选择的行不正确");
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(Control).GetMethod("OnKeyDown", flags).Invoke(list, new object[] { new KeyEventArgs(Keys.Shift | Keys.Down) });
                Check(list.Items[2].Selected && list.Items[3].Selected, "像素列表 Shift 多选失败");
                MouseEvent(list, "OnMouseDown", MouseButtons.Right, 60, list.Items[2].Bounds.Top + 3);
                Check(list.Items[2].Selected && list.Items[3].Selected, "右键选中行破坏了多选");
                SendMessage(list.Handle, 0x100, new IntPtr((int)Keys.End), IntPtr.Zero);
                Application.DoEvents();
                Check(list.Items[79].Selected && list.Items[79].Bounds.Bottom <= list.ClientSize.Height, "键盘 End 未选择并显示末项");
                CheckViewportFrame(list, "键盘定位");
                list.Items.Clear();
                Application.DoEvents();
                Check(!bar.Visible && bar.Value == 0, "清空列表后未隐藏滚动条");
                list.Items.Add("单个项目");
                Application.DoEvents();
                Check(!bar.Visible, "短列表仍显示滚动条");
            }
        }
        static void CheckMaximizedCaptionDrag()
        {
            using (var form = new RoundedForm { ClientSize = new Size(760, 500), Opacity = 0, ShowInTaskbar = false })
            {
                form.Show();
                Application.DoEvents();
                Size restoredSize = form.Size;
                foreach (double anchor in new[] { .15, .5, .85 })
                {
                    form.WindowState = FormWindowState.Maximized;
                    Application.DoEvents();
                    Point press = new Point(form.Left + (int)Math.Round(form.Width * anchor), form.Top + 22);
                    Check(!form.TryRestoreCaptionDrag(press, press) && form.WindowState == FormWindowState.Maximized,
                        "单击标题栏错误地还原窗口");
                    Point pointer = new Point(press.X + 25, press.Y + 90);
                    Check(form.TryRestoreCaptionDrag(press, pointer) && form.WindowState == FormWindowState.Normal,
                        "拖动最大化标题栏没有还原窗口");
                    Check(form.Size == restoredSize, "拖动还原后窗口尺寸错误");
                    Check(Math.Abs((pointer.X - form.Left) / (double)form.Width - anchor) < .01 && form.Top == pointer.Y - 22,
                        "拖动还原后窗口未保持鼠标锚点");
                }
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var title = (Control)typeof(RoundedForm).GetField("caption", flags).GetValue(form);
                // Normal-state native movement is handled by Windows; test double-click separately.
                typeof(Control).GetMethod("OnDoubleClick", flags).Invoke(title, new object[] { EventArgs.Empty });
                Check(form.WindowState == FormWindowState.Maximized, "双击标题栏无法最大化");
                typeof(Control).GetMethod("OnDoubleClick", flags).Invoke(title, new object[] { EventArgs.Empty });
                Check(form.WindowState == FormWindowState.Normal, "双击标题栏无法还原");
            }
        }
        static void CheckLauncherScrollLayout(string session, string work)
        {
            string config = Path.Combine(session, "scroll-layout.xml");
            var paths = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 40; i++) paths.Add("https://example.com/scroll/" + i);
            Launcher.Save(config, paths);
            using (var form = new MainForm(config, false, false))
            {
                form.Opacity = 0;
                form.Show();
                Application.DoEvents();
                var list = (ListView)form.Controls.Find("ItemsList", true)[0];
                var bar = (RoundedScrollBar)form.Controls.Find("ItemsScrollBar", true)[0];
                Check(bar.Visible && list.Right <= bar.Left, "主窗口滚动条未显示或遮挡内容");
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(work, "main-scrollbar-preview.png"));
                }
                Application.DoEvents();
                Check(list.ClientSize.Width == list.Width && list.ClientSize.Height == list.Height,
                    "主窗口仍显示原生滚动条：client=" + list.ClientSize + " size=" + list.Size);
            }
        }
        static void CheckLanguageSwitch(string session, string work)
        {
            string config = Path.Combine(session, "language.xml");
            var targets = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 30; i++) targets.Add("https://example.com/language/" + i);
            Launcher.Save(config, targets);
            try
            {
                using (var form = new MainForm(config, false, false))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                    var button = (Button)form.Controls.Find("LanguageSwitchButton", true)[0];
                    var list = (DarkListView)form.Controls.Find("ItemsList", true)[0];
                    var automatic = (CheckBox)form.Controls.Find("AutoOpenCheckBox", true)[0];
                    var startup = (CheckBox)form.Controls.Find("StartWithWindowsCheckBox", true)[0];
                    bool startupWasChecked = startup.Checked;
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var tray = (NotifyIcon)typeof(MainForm).GetField("trayIcon", flags).GetValue(form);
                    Check(button.Text == "Switch to English" && form.Text == "快捷启动器", "默认语言或语言按钮错误");
                    list.Items[20].Selected = true;
                    list.Items[20].EnsureVisible(); Application.DoEvents();
                    int offset = list.PixelOffset;
                    button.PerformClick(); Application.DoEvents();
                    Check(UiText.English && form.Text == "Quick Launcher" && button.Text == "切换到中文", "切换英文未即时更新标题或按钮");
                    Check(list.Columns[0].Text == "Name" && list.Items[20].SubItems[1].Text == "Website"
                        && list.ContextMenuStrip.Items[0].Text == "Open selected items only", "英文列表标题、类型或菜单未翻译");
                    Check(tray.Text == "Quick Launcher" && tray.ContextMenuStrip.Items[0].Text == "Open editor"
                        && tray.ContextMenuStrip.Items[2].Text == "Exit", "托盘文字未随语言切换");
                    Check(list.Items[20].Selected && list.SelectedIndices.Count == 1 && list.PixelOffset == offset,
                        "切换语言丢失选择或改变滚动位置");
                    Check(!automatic.Checked && startup.Checked == startupWasChecked, "切换语言改变了启动设置");
                    Check(Launcher.LoadLanguage(config) == "en" && Launcher.Load(config).SequenceEqual(targets), "语言未保存或破坏了原列表");
                    var credit = (LinkLabel)form.Controls.Find("AuthorCredit", true)[0];
                    Check(credit.Text.StartsWith("Author: ") && credit.Links.Count == 1
                        && (string)credit.Links[0].LinkData == "https://space.bilibili.com/504149815/upload/video", "英文作者说明或链接错误");
                    Check(credit.Text.EndsWith("每日DeepSeek消息") && credit.LinkBehavior == LinkBehavior.AlwaysUnderline
                        && credit is SystemHandLinkLabel, "作者名被翻译或链接未使用常驻下划线和系统手形样式");
                    var linkHitTest = typeof(LinkLabel).GetMethod("PointInLink", flags);
                    Point linkPoint = Point.Empty;
                    bool foundLink = false;
                    for (int x = 0; x < credit.Width; x++)
                    {
                        Point point = new Point(x, Math.Max(0, credit.Height - credit.Font.Height / 2));
                        if (linkHitTest.Invoke(credit, new object[] { point.X, point.Y }) == null) continue;
                        linkPoint = point; foundLink = true; break;
                    }
                    Check(foundLink && linkHitTest.Invoke(credit, new object[] { 1, 1 }) == null, "链接命中区域异常");
                    Cursor previousCursor = Cursor.Current;
                    try
                    {
                        MouseEvent(credit, "OnMouseMove", MouseButtons.None, linkPoint.X, linkPoint.Y);
                        Check(Cursor.Current.Handle == Theme.SystemHand.Handle, "链接悬停未使用 Windows 系统手形光标");
                    }
                    finally { Cursor.Current = previousCursor; }
                    using (var dialog = new WebsiteDialog(form.Font, form.Icon))
                    {
                        dialog.Opacity = 0; dialog.Show(form); Application.DoEvents();
                        Check(dialog.Text == "Add website", "添加网站弹窗未翻译");
                        Check(dialog.Controls.Find("WebsiteAddress", true).Length == 1, "语言切换破坏网站输入");
                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                        {
                            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(work, "english-website-dialog-preview.png"));
                        }
                    }
                    using (var message = new AppMessageDialog(UiText.T("清空启动列表？原文件不会被删除。"), form.Text, true))
                    {
                        message.Opacity = 0; message.Show(form); Application.DoEvents();
                        Check(((Button)message.AcceptButton).Text == "Yes" && ((Button)message.CancelButton).Text == "No", "应用确认弹窗按钮未翻译");
                        using (var bitmap = new Bitmap(message.Width, message.Height))
                        {
                            message.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(work, "english-confirm-preview.png"));
                        }
                    }
                    bool invalid = false;
                    try { Launcher.NormalizeWebsite("invalid address"); }
                    catch (FormatException ex) { invalid = ex.Message.StartsWith("Enter a valid"); }
                    Check(invalid && MainForm.AdditionFeedback(new[] { "A", "B" }, new[] { "C" }) == "Added A, B!\nC has already been added!", "英文校验或反馈提示错误");
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(work, "english-preview.png"));
                    }
                    form.ClientSize = new Size(720, 540); Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(work, "english-small-preview.png"));
                    }
                    using (var reopened = new MainForm(config, false, false))
                        Check(UiText.English && reopened.Text == "Quick Launcher", "重新启动未恢复英文偏好");
                    automatic.Checked = true;
                    Check(Launcher.LoadLanguage(config) == "en", "修改其他设置丢失语言偏好");
                    automatic.Checked = false;
                    button.PerformClick();
                    Check(!UiText.English && form.Text == "快捷启动器" && button.Text == "Switch to English"
                        && tray.ContextMenuStrip.Items[2].Text == "退出" && list.Items[0].SubItems[1].Text == "网站", "切回中文未完整恢复");
                    Check(Launcher.LoadLanguage(config) == "zh-CN" && Launcher.Load(config).SequenceEqual(targets), "切回中文未保存或改动列表");
                    using (var entered = new System.Threading.ManualResetEventSlim(false))
                    using (var release = new System.Threading.ManualResetEventSlim(false))
                    {
                        Task<bool> opening = form.OpenItems(false, delegate { entered.Set(); release.Wait(); }, false);
                        try
                        {
                            var timer = Stopwatch.StartNew();
                            while (!entered.IsSet && timer.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                            Check(entered.IsSet, "模拟打开任务未启动");
                            button.PerformClick();
                            var status = (Label)typeof(MainForm).GetField("status", flags).GetValue(form);
                            Check(UiText.English && status.Text.StartsWith("Opening ") && !opening.IsCompleted,
                                "打开过程中切换语言打断操作或没有翻译进度");
                        }
                        finally { release.Set(); }
                        Check(Wait(opening) && !form.IsDisposed, "切换语言后打开任务未正常完成");
                    }
                    button.PerformClick();
                }
            }
            finally { UiText.SetLanguage(false); }
        }
        static void CheckListHover(string work)
        {
            using (var host = new Form { ClientSize = new Size(660, 140), StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false, TopMost = true })
            using (var list = new DarkListView { Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true, Font = Theme.UiFont(10.5F) })
            using (var images = new ImageList { ImageSize = new Size(1, 38) })
            {
                images.Images.Add(new Bitmap(1, 38));
                list.SmallImageList = images;
                list.Columns.Add("名称", 230);
                list.Columns.Add("类型", 100);
                list.Columns.Add("路径", 280);
                list.Items.Add(new ListViewItem(new[] { "中文文件 hover-test.txt", "文件", @"C:\Example\hover-test.txt" }));
                list.Items.Add(new ListViewItem(new[] { "第二行", "文件夹", @"C:\Example" }));
                host.Controls.Add(list);
                DisableTestWindowAnimation(host);
                host.Show();
                // Override STARTUPINFO's hidden first window when self-test is launched in the background.
                host.Hide();
                host.Show();
                Application.DoEvents();
                foreach (bool selected in new[] { false, true })
                {
                    list.Items[0].Selected = selected;
                    list.Refresh();
                    using (Bitmap before = CaptureListRow(list, 0))
                    {
                        if (selected) Check(before.GetPixel(before.Width - 3, 3).ToArgb() == Theme.Hover.ToArgb(), "多选列表高亮没有铺满整行右侧");
                        foreach (int x in new[] { 40, 260, 400, 40 })
                        {
                            Point point = new Point(x, list.GetItemRect(0).Top + 15);
                            SendMessage(list.Handle, 0x200, IntPtr.Zero, new IntPtr((point.Y << 16) | point.X));
                            Application.DoEvents();
                            list.Update();
                            using (Bitmap after = CaptureListRow(list, 0))
                            {
                                before.Save(Path.Combine(work, "hover-before.png"));
                                after.Save(Path.Combine(work, "hover-after.png"));
                                CheckSamePixels(before, after, "鼠标悬停后列表文字或位置发生变化");
                            }
                        }
                    }
                    // Exercise different native paint-HDC DPIs without changing the user's display settings.
                    Bitmap reference = null;
                    try
                    {
                        foreach (int dpi in new[] { 96, 120, 144, 192 })
                        {
                            using (var rendered = new Bitmap(list.ClientSize.Width, list.GetItemRect(0).Height))
                            {
                                rendered.SetResolution(dpi, dpi);
                                using (Graphics graphics = Graphics.FromImage(rendered))
                                {
                                    var draw = new DrawListViewItemEventArgs(graphics, list.Items[0],
                                        new Rectangle(Point.Empty, rendered.Size), 0, selected ? ListViewItemStates.Selected : 0);
                                    typeof(DarkListView).GetMethod("OnDrawItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                        .Invoke(list, new object[] { draw });
                                }
                                if (reference == null) reference = (Bitmap)rendered.Clone();
                                else CheckSamePixels(reference, rendered, "不同绘图 DPI 导致列表位置或字形改变：" + dpi);
                            }
                        }
                    }
                    finally { if (reference != null) reference.Dispose(); }
                }
            }
        }
        public static int Run()
        {
            string work = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tests", "work"));
            Directory.CreateDirectory(work);
            string report = Path.Combine(work, "result.txt");
            string session = Path.Combine(work, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session);
            try
            {
                Check(Theme.FontName.IndexOf("Smiley", StringComparison.OrdinalIgnoreCase) >= 0 || Theme.FontName.Contains("得意"), "未加载内置得意黑字体");
                CheckListHover(work);
                CheckMenuHitTargets(work);
                CheckRoundedScrolling(work);
                CheckMaximizedCaptionDrag();
                CheckLauncherScrollLayout(session, work);
                CheckLanguageSwitch(session, work);
                UpdaterTests.Run(session);
                string startupTestKey = @"Software\QuickLauncher.Tests\" + Guid.NewGuid().ToString("N");
                try
                {
                    string command = StartupRegistration.Command(Path.Combine(session, "folder with spaces", "QuickLauncher.exe"), true);
                    Check(command.StartsWith("\"") && command.EndsWith("\" --portable"), "开机自启命令路径或便携参数错误");
                    StartupRegistration.SetEnabled(true, command, startupTestKey);
                    Check(StartupRegistration.IsEnabled(command, startupTestKey), "开机自启设置未写入");
                    Check(!StartupRegistration.IsEnabled(command + "other", startupTestKey), "开机自启错误识别其他副本");
                    StartupRegistration.SetEnabled(false, command, startupTestKey);
                    Check(!StartupRegistration.IsEnabled(command, startupTestKey), "取消开机自启未删除设置");
                }
                finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(startupTestKey, false); }
                using (Font toastFont = Theme.UiFont(10.5F))
                using (Bitmap shortToast = CapsuleToast.Render("添加 快捷启动器.exe 成功！", toastFont, 1.25f, 700))
                using (Bitmap longToast = CapsuleToast.Render("添加 一个名字很长很长的文件夹和文件快捷方式测试文字，所有文字都应该完整显示，不能省略或者裁切成功！", toastFont, 1.25f, 400))
                {
                    Check(longToast.Width <= 400 && longToast.Height > shortToast.Height, "长提示未换行以完整显示");
                    shortToast.Save(Path.Combine(work, "toast-preview.png"));
                    longToast.Save(Path.Combine(work, "toast-long-preview.png"));
                }
                using (var toastHost = new MainForm(Path.Combine(session, "toast.xml"), false, false))
                {
                    toastHost.Opacity = 0;
                    toastHost.Show();
                    Check(toastHost.AddWebsite("https://example.com/toast", true), "胶囊提示阻塞添加");
                    var toastField = typeof(MainForm).GetField("additionToast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var firstToast = (Form)toastField.GetValue(toastHost);
                    Check(firstToast.Visible && !firstToast.ShowInTaskbar, "胶囊提示未显示或占用任务栏");
                    int startTop = firstToast.Top;
                    var timer = Stopwatch.StartNew();
                    while (timer.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Check(firstToast.Top < startTop, "胶囊提示未向上移动");
                    Check(!toastHost.AddWebsite("https://example.com/toast", true) && firstToast.IsDisposed, "重复提示未替换旧提示");
                    var lastToast = (Form)toastField.GetValue(toastHost);
                    timer.Restart();
                    while (timer.ElapsedMilliseconds < 2300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Check(!lastToast.IsDisposed, "胶囊提示未保持到三秒时长");
                    while (!lastToast.IsDisposed && timer.ElapsedMilliseconds < 3600) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Check(lastToast.IsDisposed && toastHost.Visible, "胶囊提示未自动消失或关闭了主界面");
                }
                Check(MainForm.AdditionFeedback(new[] { "A", "B" }, new string[0]) == "添加 A、B 成功！", "批量添加提示未合并名称");
                Check(MainForm.AdditionFeedback(new string[0], new[] { "A", "B" }) == "A、B 已添加过啦！", "批量重复提示未合并名称");
                string dropConfig = Path.Combine(session, "drop.xml");
                using (var dropForm = new MainForm(dropConfig, false, false))
                {
                    using (var extra = new Panel())
                    {
                        dropForm.Controls.Add(extra);
                        int count = 0;
                        CheckFileDropTargets(dropForm, session, dropConfig, ref count);
                    }
                }
                string pastedFile = Path.Combine(session, "pasted.txt");
                File.WriteAllText(pastedFile, "clipboard source");
                string pastedConfig = Path.Combine(session, "pasted.xml");
                using (var pasteForm = new MainForm(pastedConfig, false, false))
                {
                    pasteForm.Opacity = 0;
                    pasteForm.Show();
                    var data = new DataObject(DataFormats.FileDrop, new[] { pastedFile, session });
                    pasteForm.PasteFiles(data);
                    Check(Launcher.Load(pastedConfig).Count == 2 && File.ReadAllText(pastedFile) == "clipboard source", "粘贴文件或文件夹失败或改变了原文件");
                    pasteForm.PasteFiles(data);
                    Check(Launcher.Load(pastedConfig).Count == 2, "重复粘贴未去重");
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var toastField = typeof(MainForm).GetField("additionToast", flags);
                    Check(((Form)toastField.GetValue(pasteForm)).Text == "pasted.txt、" + Path.GetFileName(session) + " 已添加过啦！", "粘贴重复反馈错误");
                    var items = (ListView)pasteForm.Controls.Find("ItemsList", true)[0];
                    items.Items[0].Selected = true;
                    typeof(MainForm).GetMethod("RemoveSelected", flags).Invoke(pasteForm, null);
                    Check(Launcher.Load(pastedConfig).Count == 1 && File.Exists(pastedFile), "移除时改变了原文件");
                    Check(((Form)toastField.GetValue(pasteForm)).Text == "移除 pasted.txt 成功！", "移除反馈错误");
                    pasteForm.PasteFiles(new DataObject(DataFormats.Text, "unrelated text"));
                    Check(Launcher.Load(pastedConfig).Count == 1, "非文件剪贴板被误添加");
                }
                string autoConfig = Path.Combine(session, "auto.xml");
                string autoTarget = "https://example.com/startup";
                Launcher.Save(autoConfig, new[] { autoTarget }, true);
                Check(Launcher.LoadAutoOpen(autoConfig), "自动打开设置未保存");
                using (var autoForm = new MainForm(autoConfig, false, false))
                {
                    autoForm.Opacity = 0;
                    autoForm.Show();
                    Application.DoEvents();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var nativeParameters = (CreateParams)typeof(RoundedForm).GetProperty("CreateParams", flags).GetValue(autoForm, null);
                    Check((nativeParameters.Style & 0xA0000) == 0xA0000, "缺少任务栏最小化系统样式");
                    var tray = (NotifyIcon)typeof(MainForm).GetField("trayIcon", flags).GetValue(autoForm);
                    Check(tray.Visible && tray.ContextMenuStrip is RoundedContextMenu && tray.ContextMenuStrip.Items.Count == 3
                        && tray.ContextMenuStrip.Items[0].Text == "打开编辑窗口" && tray.ContextMenuStrip.Items[1] is ToolStripSeparator
                        && tray.ContextMenuStrip.Items[2].Text == "退出", "托盘图标或菜单错误");
                    int opened = 0;
                    Wait(autoForm.RunStartup(delegate(string target) { Check(target == autoTarget, "自动打开目标错误"); opened++; }, false));
                    Check(opened == 1 && !autoForm.Visible && tray.Visible, "自动打开后未收起至托盘");
                    tray.ContextMenuStrip.Items[0].PerformClick();
                    Check(autoForm.Visible && autoForm.ShowInTaskbar, "托盘恢复界面失败");
                    Wait(autoForm.RunStartup(delegate { opened++; }, false));
                    Check(opened == 1, "恢复窗口重复执行启动任务");
                    SendMessage(autoForm.Handle, 0x112, new IntPtr(0xF020), IntPtr.Zero);
                    Application.DoEvents();
                    Check(autoForm.WindowState == FormWindowState.Minimized, "系统最小化指令未生效");
                    autoForm.ShowMainWindow();
                    Check(autoForm.WindowState == FormWindowState.Normal, "最小化后未恢复");
                    var option = (CheckBox)autoForm.Controls.Find("AutoOpenCheckBox", true)[0];
                    option.Checked = false;
                    Check(!Launcher.LoadAutoOpen(autoConfig) && Launcher.Load(autoConfig).Count == 1, "取消勾选未保存或丢失列表");
                    tray.ContextMenuStrip.Show(autoForm, new Point(40, 60));
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(tray.ContextMenuStrip.Width, tray.ContextMenuStrip.Height))
                    {
                        tray.ContextMenuStrip.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(work, "tray-menu-preview.png"));
                    }
                    tray.ContextMenuStrip.Close();
                    tray.ContextMenuStrip.Items[2].PerformClick();
                    Check(autoForm.IsDisposed && !tray.Visible, "托盘退出未关闭程序并释放图标");
                }
                Launcher.Save(autoConfig, new[] { autoTarget }, true);
                using (var failureForm = new MainForm(autoConfig, false, false))
                {
                    failureForm.Opacity = 0;
                    failureForm.Show();
                    Wait(failureForm.RunStartup(delegate { throw new IOException("模拟失败"); }, false));
                    Check(failureForm.Visible, "自动打开失败后未保留界面");
                }
                Launcher.Save(autoConfig, new string[0], true);
                using (var emptyForm = new MainForm(autoConfig, false, false))
                {
                    emptyForm.Opacity = 0;
                    emptyForm.Show();
                    Wait(emptyForm.RunStartup(delegate { throw new Exception("空列表不应启动"); }, false));
                    Check(emptyForm.Visible, "空列表不应收起界面");
                }
                using (Bitmap frame = SmoothFrame.Render(new Size(120, 100)))
                {
                    bool hasBlendedEdge = false;
                    for (int x = 0; x < 25; x++)
                        for (int y = 0; y < 25; y++)
                        {
                            byte alpha = frame.GetPixel(x, y).A;
                            if (alpha > 0 && alpha < 255) hasBlendedEdge = true;
                        }
                    Check(hasBlendedEdge && frame.GetPixel(60, 50).A == 0, "圆角边缘没有半透明过渡或遮挡了主界面");
                }
                using (var frameForm = new MainForm(Path.Combine(session, "frame.xml"), false))
                {
                    frameForm.ShowInTaskbar = false;
                    frameForm.StartPosition = FormStartPosition.Manual;
                    frameForm.Location = new Point(-3000, -3000);
                    frameForm.Show();
                    Application.DoEvents();
                    Check(frameForm.OwnedForms.Length == 1 && frameForm.OwnedForms[0].Visible, "平滑圆角层未显示");
                    Form decoration = frameForm.OwnedForms[0];
                    frameForm.Location = new Point(-2800, -2800);
                    Application.DoEvents();
                    Check(decoration.Left == frameForm.Left - 3 && decoration.Top == frameForm.Top - 3, "平滑圆角层未跟随移动");
                    frameForm.WindowState = FormWindowState.Minimized;
                    Application.DoEvents();
                    Check(!decoration.Visible, "最小化后圆角层残留");
                    frameForm.WindowState = FormWindowState.Normal;
                    Application.DoEvents();
                    Check(decoration.Visible, "还原后圆角层未恢复");
                    var exitButton = (Button)frameForm.Controls.Find("OpenAllAndExitButton", true)[0];
                    Check(exitButton.Text == "全部打开并退出本软件（Space）" && exitButton.Height >= 48, "主按钮文字或尺寸错误");
                    int clicks = 0;
                    exitButton.Enabled = true;
                    exitButton.Click += delegate { clicks++; };
                    var keyMethod = typeof(MainForm).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    object[] keyArguments = { Message.Create(frameForm.Handle, 0x100, new IntPtr((int)Keys.Space), IntPtr.Zero), Keys.Space };
                    Check((bool)keyMethod.Invoke(frameForm, keyArguments) && clicks == 1, "空格快捷键未触发主按钮");
                    exitButton.Enabled = false;
                    keyMethod.Invoke(frameForm, keyArguments);
                    Check(clicks == 1, "禁用的主按钮被快捷键触发");
                    frameForm.Close();
                    Check(decoration.IsDisposed, "关闭窗口后圆角层未释放");
                }
                string releaseJson = "{\"tag_name\":\"v1.1.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"QuickLauncher-1.1.0-windows-setup.exe\",\"state\":\"uploaded\"}]}";
                var currentVersion = new Version(1, 0, 1, 0);
                ReleaseUpdate available = UpdateChecker.Parse(releaseJson, currentVersion);
                Check(available != null && available.DownloadUrl == "https://github.com/WalterLyle/quick-launcher/releases/tag/v1.1.0", "更新版本或下载页面解析失败");
                Check(UpdateChecker.ParseLatestLink(available.DownloadUrl, currentVersion) != null, "版本页面备用检查失败");
                Check(UpdateChecker.ParseLatestLink("https://example.com/releases/tag/v9.0.0", currentVersion) == null, "接受了其他网站的版本链接");
                Check(UpdateChecker.ParseLatestLink("https://github.com/WalterLyle/quick-launcher/releases/tag/v1.0.1", currentVersion) == null, "备用检查误报相同版本");
                Check(UpdateChecker.ParseLatestLink(available.DownloadUrl + "?redirect=other", currentVersion) == null, "接受了异常版本链接");
                Check(UpdateChecker.Parse(releaseJson.Replace("1.1.0", "1.0.1"), currentVersion) == null, "相同版本误报更新");
                Check(UpdateChecker.Parse(releaseJson.Replace("1.1.0", "1.0.0"), currentVersion) == null, "旧版本误报更新");
                Check(UpdateChecker.Parse(releaseJson.Replace("1.1.0", "1.10.0"), new Version(1, 9, 0, 0)) != null, "版本号未按数字比较");
                Check(UpdateChecker.Parse(releaseJson.Replace("\"draft\":false", "\"draft\":true"), currentVersion) == null, "草稿被提示更新");
                Check(UpdateChecker.Parse(releaseJson.Replace("\"prerelease\":false", "\"prerelease\":true"), currentVersion) == null, "预发布版被提示更新");
                Check(UpdateChecker.Parse(releaseJson.Replace("v1.1.0", "v1.1.0-beta"), currentVersion) == null, "非正式标签被提示更新");
                Check(UpdateChecker.Parse(releaseJson.Replace("uploaded", "new"), currentVersion) == null, "未上传完成的版本被提示更新");
                Check(UpdateChecker.Parse(releaseJson.Replace("setup.exe", "source.zip"), currentVersion) == null, "没有 Windows 下载包的版本被提示更新");
                using (var updateForm = new MainForm(Path.Combine(session, "updates.xml"), false))
                {
                    updateForm.ShowInTaskbar = false;
                    updateForm.Opacity = 0;
                    updateForm.Show();
                    Application.DoEvents();
                    var updateButton = (Button)updateForm.Controls.Find("UpdateButton", true)[0];
                    Check(!updateButton.Visible, "未发现更新时按钮未隐藏");
                    Wait(updateForm.CheckForUpdates(() => Task.FromResult(available)));
                    Check(updateButton.Visible && updateButton.Text.Contains("1.1.0"), "发现新版本后按钮未显示");
                    Wait(updateForm.CheckForUpdates(delegate { throw new System.Net.WebException("offline"); }));
                    Check(updateButton.Visible, "检查失败丢失已知更新");
                    updateForm.ClientSize = new Size(720, 500);
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(updateForm.Width, updateForm.Height))
                    {
                        updateForm.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(work, "update-preview.png"));
                    }
                    Wait(updateForm.CheckForUpdates(() => Task.FromResult<ReleaseUpdate>(null)));
                    Check(!updateButton.Visible, "无更新时按钮未隐藏");
                    var pending = new TaskCompletionSource<ReleaseUpdate>();
                    Task checking = updateForm.CheckForUpdates(() => pending.Task);
                    Check(updateForm.AddWebsite("https://example.com"), "更新检查阻塞添加项目");
                    updateForm.Dispose();
                    pending.SetResult(available);
                    Wait(checking);
                }
                string instanceName = "Local\\QuickLauncher.Test." + Guid.NewGuid().ToString("N");
                using (var owner = new SingleInstance(instanceName))
                {
                    Check(owner.IsOwner, "首个实例未获得运行权");
                    Check(Task.Run(() =>
                    {
                        using (var duplicate = new SingleInstance(instanceName)) return !duplicate.IsOwner;
                    }).GetAwaiter().GetResult(), "重复实例未被阻止");
                }
                using (var reopened = new SingleInstance(instanceName))
                    Check(reopened.IsOwner, "退出后无法重新启动");
                string first = Path.Combine(session, "中文 文件 & name.txt");
                string fakeLocalAppData = Path.Combine(session, "local-app-data");
                string installedConfig = Path.Combine(fakeLocalAppData, "QuickLauncher", "items.xml");
                string portableConfig = Path.Combine(session, "data", "items.xml");
                Check(Launcher.ConfigPath(session, fakeLocalAppData, false) == installedConfig, "安装版配置位置错误");
                Check(Launcher.ConfigPath(session, fakeLocalAppData, true) == portableConfig, "便携参数配置位置错误");
                string portableFlag = Path.Combine(session, "portable.flag");
                File.WriteAllText(portableFlag, "");
                Check(Launcher.ConfigPath(session, fakeLocalAppData, false) == portableConfig, "便携标记配置位置错误");
                File.Delete(portableFlag);
                Check(!File.Exists(installedConfig) && !File.Exists(portableConfig), "配置位置检测写入了用户数据");
                string second = Path.Combine(session, "second.txt");
                string config = Path.Combine(session, "items.xml");
                File.WriteAllText(first, "sample"); File.WriteAllText(second, "sample");
                string currentExecutable = Application.ExecutablePath;
                Check(Launcher.IsRunningApplication(currentExecutable), "未识别已运行的软件");
                string otherExecutable = Path.Combine(session, Path.GetFileName(currentExecutable));
                File.Copy(currentExecutable, otherExecutable);
                Check(!Launcher.IsRunningApplication(otherExecutable), "不同路径的同名软件被误判");
                Check(!Launcher.IsRunningApplication(first), "普通文件被误判为已运行软件");
                Launcher.Save(config, new[] { first, second });
                Check(Launcher.Load(config).Count == 2, "配置恢复失败");
                Launcher.Save(config, new[] { second });
                Check(Launcher.Load(config).Count == 1 && Launcher.Load(config + ".bak").Count == 2, "原子保存或备份失败");
                Check(Launcher.StartInfo(first).UseShellExecute && Launcher.StartInfo(first).FileName == first, "Shell 路径处理失败");
                string website = Launcher.NormalizeWebsite(" example.com/Docs?q=One&lang=zh#part ");
                Check(website == "https://example.com/Docs?q=One&lang=zh#part", "网址补全或参数保留失败");
                Check(Launcher.NormalizeWebsite("HTTP://EXAMPLE.COM/Docs") == "http://example.com/Docs", "HTTP 网址解析失败");
                foreach (string invalid in new[] { "", "not a website", "ftp://example.com", "file:///C:/test.txt", "javascript:alert(1)", "https://", "https://example.com:bad" })
                {
                    bool rejected = false;
                    try { Launcher.NormalizeWebsite(invalid); }
                    catch (FormatException) { rejected = true; }
                    Check(rejected, "无效网址未被拒绝：" + invalid);
                }
                var websiteStart = Launcher.StartInfo(website);
                Check(websiteStart.FileName == website && websiteStart.UseShellExecute && websiteStart.WorkingDirectory == "" && websiteStart.Arguments == "", "网站未直接交给 Shell 打开");
                Check(!Launcher.IsRunningApplication(website), "网址被软件运行检测拦截");
                string websiteConfig = Path.Combine(session, "websites.xml");
                Launcher.Save(websiteConfig, new[] { first, website, "https://EXAMPLE.COM/Docs?q=One&lang=zh#part", "https://example.com/docs?q=One&lang=zh#part" });
                var restored = Launcher.Load(websiteConfig);
                Check(restored.Count == 3 && restored[0] == first && restored[1] == website, "混合列表恢复、网址去重或路径大小写区分失败");
                using (var webForm = new MainForm(Path.Combine(session, "web-form.xml"), false))
                {
                    webForm.ShowInTaskbar = false;
                    webForm.Opacity = 0;
                    webForm.Show();
                    Application.DoEvents();
                    webForm.AddPaths(new[] { first });
                    Check(webForm.AddWebsite(website), "添加网站失败");
                    Check(!webForm.AddWebsite(website), "重复网站未去重");
                    Check(webForm.AddWebsite("https://example.com/other"), "添加另一网站失败");
                    Check(Launcher.Load(Path.Combine(session, "web-form.xml")).Count == 3, "网站未自动保存");
                    int calls = 0;
                    Check(!Wait(webForm.OpenItems(true, delegate(string p)
                    {
                        calls++;
                        if (p == website) throw new IOException("模拟网站打开失败");
                    }, false)), "网站失败未正确报告");
                    Check(calls == 3 && !webForm.IsDisposed, "网站失败后未继续或未保留界面");
                    using (var bitmap = new Bitmap(webForm.Width, webForm.Height))
                    {
                        webForm.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(work, "website-list-preview.png"));
                    }
                    webForm.ClientSize = new Size(720, 500);
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(webForm.Width, webForm.Height))
                    {
                        webForm.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(work, "website-list-small-preview.png"));
                    }
                    using (var dialog = new WebsiteDialog(webForm.Font, webForm.Icon))
                    {
                        dialog.ShowInTaskbar = false;
                        dialog.Opacity = 0;
                        dialog.Show(webForm);
                        Application.DoEvents();
                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                        {
                            dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(Path.Combine(work, "website-dialog-preview.png"));
                        }
                    }
                    calls = 0;
                    Check(Wait(webForm.OpenItems(true, delegate(string p) { calls++; }, false)) && calls == 3 && webForm.IsDisposed, "混合列表全部打开并退出失败");
                }
                using (var form = new MainForm(Path.Combine(session, "form.xml"), false))
                {
                    form.Opacity = 0;
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    Check(!Wait(form.OpenItems(true, delegate(string p) { throw new Exception("Empty list launched"); }, false)), "空列表行为错误");
                    form.AddPaths(new[] { first, first.ToUpperInvariant(), second, session, Path.Combine(session, "missing") });
                    Check(Launcher.Load(Path.Combine(session, "form.xml")).Count == 3, "添加、去重或过滤失败");
                    int calls = 0;
                    Check(Wait(form.OpenItems(false, delegate(string p) { calls++; }, false)), "全部打开失败");
                    Check(calls == 3 && !form.IsDisposed, "全部打开数量或保留界面失败");
                    var selectedList = (ListView)form.Controls.Find("ItemsList", true)[0];
                    Check(!Wait(form.OpenSelectedItems(delegate { throw new Exception("空选择不应打开"); }, false)), "空选择仍执行打开");
                    selectedList.Items[0].Selected = true;
                    selectedList.Items[2].Selected = true;
                    Check(selectedList.ContextMenuStrip is RoundedContextMenu, "列表右键未使用圆角菜单");
                    selectedList.ContextMenuStrip.Show(selectedList, new Point(20, 40));
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(selectedList.ContextMenuStrip.Width, selectedList.ContextMenuStrip.Height))
                    {
                        selectedList.ContextMenuStrip.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(work, "list-menu-preview.png"));
                    }
                    selectedList.ContextMenuStrip.Close();
                    var selectedOpened = new System.Collections.Generic.List<string>();
                    Check(Wait(form.OpenSelectedItems(delegate(string p) { selectedOpened.Add(p); }, false)), "选中项目打开失败");
                    Check(selectedOpened.Count == 2 && selectedOpened[0] == first && selectedOpened[1] == session && !form.IsDisposed,
                        "选中项目范围错误或关闭了界面");
                    selectedList.Items[0].Selected = true;
                    selectedList.Items[2].Selected = true;
                    selectedOpened.Clear();
                    Check(!Wait(form.OpenSelectedItems(delegate(string p)
                    {
                        selectedOpened.Add(p);
                        if (p == first) throw new IOException("模拟选中项目失败");
                    }, false)), "选中项目失败未报告");
                    Check(selectedOpened.Count == 2 && !form.IsDisposed && Launcher.Load(Path.Combine(session, "form.xml")).Count == 3,
                        "选中项目失败后未继续、未保留窗口或改变了列表");
                    calls = 0;
                    Check(!Wait(form.OpenItems(true, delegate(string p) { calls++; if (p == first) throw new IOException("模拟失败"); }, false)), "失败结果错误");
                    Check(calls == 3 && !form.IsDisposed, "失败后继续打开或保留界面失败");
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(work, "preview.png"));
                    }
                    Check(Wait(form.OpenItems(true, delegate(string p) { }, false)), "打开并退出失败");
                    Check(form.IsDisposed, "程序未退出");
                }
                string marker = Path.Combine(session, "shell-opened.txt");
                string script = Path.Combine(session, "probe.cmd");
                File.WriteAllText(script, "@echo off\r\necho success>\"" + marker + "\"\r\n");
                string shortcut = Path.Combine(session, "probe.lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcut });
                try
                {
                    link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { script });
                    link.GetType().InvokeMember("WindowStyle", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { 7 });
                    link.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
                }
                finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                Launcher.Open(shortcut);
                var timeout = Stopwatch.StartNew();
                while (!File.Exists(marker) && timeout.ElapsedMilliseconds < 10000) System.Threading.Thread.Sleep(50);
                Check(File.Exists(marker), "真实 .lnk Shell 启动失败");
                shell = Activator.CreateInstance(shellType);
                link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcut });
                try
                {
                    link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { currentExecutable });
                    link.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
                }
                finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                Check(Launcher.IsRunningApplication(shortcut), "快捷方式未识别已运行的软件");
                using (var form = new MainForm(Path.Combine(session, "running.xml"), false))
                {
                    form.AddPaths(new[] { currentExecutable, shortcut, first });
                    int calls = 0;
                    Check(Wait(form.OpenItems(false, delegate(string p)
                    {
                        Check(p == first, "已运行的软件未被跳过");
                        calls++;
                    }, false)), "跳过已运行软件失败");
                    Check(calls == 1, "普通文件未正常打开");
                    Check(Wait(form.OpenItems(false, delegate(string p) { calls++; }, false)), "再次打开失败");
                    Check(calls == 2, "普通文件被错误跳过");
                    Check(Wait(form.OpenItems(true, delegate(string p) { calls++; }, false)) && form.IsDisposed, "跳过已运行软件后未正常退出");
                }
                string selfConfig = Path.Combine(session, "self-startup.xml");
                Launcher.Save(selfConfig, new[] { currentExecutable, shortcut }, true);
                using (var selfForm = new MainForm(selfConfig, false, false))
                {
                    selfForm.Opacity = 0;
                    selfForm.Show();
                    int selfLaunches = 0;
                    Wait(selfForm.RunStartup(delegate { selfLaunches++; }, false));
                    Check(selfLaunches == 0 && !selfForm.Visible, "启动时未跳过启动器自身或其快捷方式");
                    selfForm.ShowMainWindow();
                    Wait(selfForm.RunStartup(delegate { selfLaunches++; }, false));
                    Check(selfLaunches == 0 && selfForm.Visible, "恢复界面重复执行了自身启动项目");
                }
                File.WriteAllText(report, "PASS: 一键更新校验、损坏拒绝、越界包拒绝、便携文件替换与回滚、个人数据保留、更新按钮交接与失败恢复、自动启动跳过自身及自身快捷方式、恢复界面不重复启动、自动打开设置保存、启动只执行一次、成功收起托盘、失败及空列表保留界面、托盘恢复和退出、系统最小化及还原、更新版本比较、正式版及下载包筛选、更新按钮显隐、断网不打断使用、检查期间关闭窗口、网址校验与 HTTPS 补全、URL 参数保留、网站直接 Shell 打开配置、混合列表保存恢复与去重、网站失败后继续及保留界面、混合列表全部打开并退出、已运行软件及快捷方式跳过、同名不同路径区分、普通文件继续打开、跳过后退出、单实例、阻止重复实例、退出后重新启动、配置恢复、原子保存与备份、中文和特殊字符路径、去重、无效路径过滤、全部打开、失败后继续、失败保留界面、全部打开并退出、真实 .lnk 启动。\r\n");
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
        }
    }
}
