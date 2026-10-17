using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace QuickLauncher
{
    internal static class StartupRegistration
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static string Command(string executable, bool portable)
        {
            return "\"" + Path.GetFullPath(executable) + "\"" + (portable ? " --portable" : "");
        }
        internal static bool IsEnabled(string command, string keyPath = RunKey)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
                return key != null && string.Equals(key.GetValue("QuickLauncher") as string, command, StringComparison.OrdinalIgnoreCase);
        }
        internal static void SetEnabled(bool enabled, string command, string keyPath = RunKey)
        {
            if (enabled && command.Length > 260) throw new InvalidOperationException(UiText.T("程序路径太长，请将软件放到较短的路径后再启用开机自启。"));
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
            {
                if (enabled) key.SetValue("QuickLauncher", command, Microsoft.Win32.RegistryValueKind.String);
                else key.DeleteValue("QuickLauncher", false);
            }
        }
    }

    internal sealed class SingleInstance : IDisposable
    {
        internal const string Name = "Local\\QuickLauncher.SingleInstance";
        internal static readonly int ActivateMessage = RegisterWindowMessage("QuickLauncher.ActivateExistingWindow");
        readonly Mutex mutex;
        internal readonly bool IsOwner;

        internal SingleInstance(string name)
        {
            mutex = new Mutex(false, name);
            try { IsOwner = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsOwner = true; }
        }

        internal static void ActivateExisting()
        {
            PostMessage(new IntPtr(0xffff), ActivateMessage, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (IsOwner) mutex.ReleaseMutex();
            mutex.Dispose();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }

    public static class Launcher
    {
        sealed class TargetComparer : IEqualityComparer<string>
        {
            public bool Equals(string first, string second)
            {
                return string.Equals(first, second, IsWebsite(first) || IsWebsite(second) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
            public int GetHashCode(string value)
            {
                return (IsWebsite(value) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase).GetHashCode(value);
            }
        }
        static readonly IEqualityComparer<string> Comparer = new TargetComparer();
        public static string ConfigPath(string baseDirectory, string localAppData, bool portable)
        {
            bool usePortable = portable || File.Exists(Path.Combine(baseDirectory, "portable.flag"));
            return Path.Combine(usePortable ? Path.Combine(baseDirectory, "data") : Path.Combine(localAppData, "QuickLauncher"), "items.xml");
        }
        public static bool IsWebsite(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrEmpty(uri.Host);
        }
        public static string NormalizeWebsite(string input)
        {
            string value = (input ?? "").Trim();
            if (value.Length == 0 || value.Any(char.IsWhiteSpace)) throw new FormatException(UiText.T("请输入有效网址，例如 https://www.example.com。"));
            if (value.IndexOf("://", StringComparison.Ordinal) < 0) value = "https://" + value;
            if (!IsWebsite(value)) throw new FormatException(UiText.T("请输入有效的 http 或 https 网站地址。"));
            return new Uri(value).AbsoluteUri;
        }
        public static string Normalize(string path) { return IsWebsite(path) ? new Uri(path).AbsoluteUri : Path.GetFullPath(path); }
        public static bool Exists(string path) { return IsWebsite(path) || File.Exists(path) || Directory.Exists(path); }
        public static string DisplayName(string path)
        {
            return IsWebsite(path) ? new Uri(path).Host : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        }
        public static ProcessStartInfo StartInfo(string path)
        {
            if (IsWebsite(path)) return new ProcessStartInfo { FileName = Normalize(path), UseShellExecute = true };
            if (!Exists(path)) throw new FileNotFoundException(UiText.T("文件已移动或删除"), path);
            return new ProcessStartInfo { FileName = path, UseShellExecute = true,
                WorkingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path) };
        }
        public static void Open(string path)
        {
            // Let Windows resolve .lnk arguments and file associations; never parse as a command.
            using (Process process = Process.Start(StartInfo(path))) { }
        }
        public static bool IsRunningApplication(string path)
        {
            if (IsWebsite(path)) return false;
            if (!File.Exists(path)) return false;
            string target = path;
            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                object shell = null;
                object link = null;
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    shell = Activator.CreateInstance(shellType);
                    link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                    target = (string)link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null);
                    target = Environment.ExpandEnvironmentVariables(target);
                }
                catch { return false; }
                finally
                {
                    if (link != null) Marshal.FinalReleaseComObject(link);
                    if (shell != null) Marshal.FinalReleaseComObject(shell);
                }
            }
            if (!string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target)) return false;
            target = Normalize(target);
            bool running = false;
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target)))
            {
                using (process)
                {
                    try
                    {
                        if (string.Equals(Normalize(process.MainModule.FileName), target, StringComparison.OrdinalIgnoreCase)) running = true;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (NotSupportedException) { }
                }
            }
            return running;
        }
        public static List<string> Load(string file)
        {
            if (!File.Exists(file)) return new List<string>();
            XElement root = XDocument.Load(file).Root;
            if (root == null || root.Name != "launcher") throw new InvalidDataException(UiText.T("列表格式无效"));
            return root.Elements("item").Select(x => Normalize((string)x.Attribute("path")))
                .Distinct(Comparer).ToList();
        }
        public static bool Contains(IEnumerable<string> paths, string path) { return paths.Contains(path, Comparer); }
        public static bool LoadAutoOpen(string file)
        {
            return File.Exists(file) && (bool?)XDocument.Load(file).Root.Attribute("autoOpen") == true;
        }
        public static string LoadLanguage(string file)
        {
            try { return File.Exists(file) && (string)XDocument.Load(file).Root.Attribute("language") == "en" ? "en" : "zh-CN"; }
            catch { return "zh-CN"; }
        }
        public static void Save(string file, IEnumerable<string> paths, bool autoOpen = false, string language = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new XDocument(new XElement("launcher", new XAttribute("autoOpen", autoOpen), new XAttribute("language", language ?? LoadLanguage(file)),
                    paths.Select(p => new XElement("item", new XAttribute("path", p))))).Save(temp);
                if (File.Exists(file)) File.Replace(temp, file, file + ".bak");
                else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    internal sealed class WebsiteDialog : RoundedForm
    {
        internal string Website { get; private set; }

        internal WebsiteDialog(Font font, Icon icon)
        {
            SuspendLayout();
            Text = "添加网站";
            Font = font;
            Icon = icon;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(510, 255);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.Controls.Add(new Label { Text = "网站地址", AutoSize = true }, 0, 0);
            var address = new TextBox { Dock = DockStyle.Fill, Name = "WebsiteAddress", MaxLength = 8192,
                BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
            var inputPanel = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 7, 10, 5), Margin = new Padding(0, 0, 0, 4) };
            inputPanel.Controls.Add(address);
            layout.Controls.Add(inputPanel, 0, 1);
            layout.Controls.Add(new Label { Text = "例如 www.example.com，未填协议时自动使用 https。", AutoSize = true, ForeColor = Theme.Muted }, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
            var cancel = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel, Width = 86, Height = 32, Margin = new Padding(8, 0, 0, 0) };
            var add = new RoundedButton { Text = "添加", Width = 86, Height = 32, Primary = true, BackColor = Theme.Main, Margin = new Padding(8, 0, 0, 0) };
            add.Click += delegate
            {
                try { Website = Launcher.NormalizeWebsite(address.Text); DialogResult = DialogResult.OK; }
                catch (FormatException ex)
                {
                    AppMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    address.Focus();
                }
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(add);
            layout.Controls.Add(buttons, 0, 3);
            Body.Controls.Add(layout);
            AcceptButton = add;
            CancelButton = cancel;
            ActiveControl = address;
            UiText.Apply(this);
            ResumeLayout(true);
        }
    }

    public sealed partial class MainForm : RoundedForm
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == SingleInstance.ActivateMessage)
            {
                ShowMainWindow();
            }
            base.WndProc(ref message);
        }

        readonly string config;
        readonly List<string> paths = new List<string>();
        readonly DarkListView list = new DarkListView();
        readonly Label status = new Label();
        readonly Button openAll;
        readonly Button openExit;
        readonly Button updateButton;
        readonly CheckBox autoOpen = new ClearCheckBox();
        readonly CheckBox startWithWindows = new ClearCheckBox();
        NotifyIcon trayIcon;
        ContextMenuStrip trayMenu;
        readonly RoundedContextMenu listMenu = new RoundedContextMenu();
        CapsuleToast additionToast;
        bool startupHandled;
        ReleaseUpdate availableUpdate;
        bool checkingForUpdates;
        bool busy;
        bool dirty;

        public MainForm(string configPath, bool checkForUpdates = true, bool automaticStartup = true)
        {
            SuspendLayout();
            config = configPath;
            UiText.SetLanguage(Launcher.LoadLanguage(config) == "en");
            using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("QuickLauncher.AppIcon"))
            using (var appIcon = new Icon(iconStream, new Size(32, 32)))
                Icon = (Icon)appIcon.Clone();
            Text = "快捷启动器";
            Font = Theme.UiFont(10.5F);
            ClientSize = new Size(860, 640);
            MinimumSize = new Size(720, 540);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Main;
            ForeColor = Theme.Text;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 16, 26, 18), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 152));
            Body.Controls.Add(layout);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "你的常用项目，一键就绪", AutoSize = true, Location = new Point(0, 0),
                Font = Theme.UiFont(20F, FontStyle.Bold), ForeColor = Theme.Text });
            header.Controls.Add(new Label { Text = "可直接拖入文件（包括快捷方式）或文件夹，也可直接添加网站。", AutoSize = true,
                Location = new Point(2, 47), ForeColor = Theme.Muted });
            updateButton = MakeButton("有新版本", delegate { OpenUpdate(); }, false, 150);
            updateButton.Name = "UpdateButton";
            updateButton.Visible = false;
            updateButton.Height = 32;
            header.Controls.Add(updateButton);
            header.Resize += delegate { updateButton.Location = new Point(Math.Max(0, header.ClientSize.Width - updateButton.Width), 3); };
            layout.Controls.Add(header, 0, 0);
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(3) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var addButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            addButtons.Controls.Add(MakeButton("添加文件", delegate { AddFiles(); }, false, 105));
            addButtons.Controls.Add(MakeButton("添加文件夹", delegate { AddFolder(); }, false, 120));
            addButtons.Controls.Add(MakeButton("添加网站", delegate { AddWebsiteFromDialog(); }, false, 110));
            var removeButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            removeButtons.Controls.Add(MakeButton("移除选中", delegate { RemoveSelected(); }, false, 110));
            var clearButton = MakeButton("清空列表", delegate { ClearItems(); }, false, 110);
            clearButton.Margin = Padding.Empty;
            removeButtons.Controls.Add(clearButton);
            toolbar.Controls.Add(addButtons, 0, 0);
            toolbar.Controls.Add(removeButtons, 2, 0);
            layout.Controls.Add(toolbar, 0, 1);
            list.Dock = DockStyle.Fill;
            list.Name = "ItemsList";
            list.View = View.Details;
            list.HeaderStyle = ColumnHeaderStyle.None;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = true;
            list.ShowItemToolTips = true;
            list.BorderStyle = BorderStyle.None;
            list.Columns.Add("名称", 210);
            list.Columns.Add("类型", 90);
            list.Columns.Add("路径 / 网址", 430);
            list.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            var openSelected = listMenu.Items.Add("仅打开选中的文件", null, async delegate { await OpenSelectedItems(); });
            listMenu.Opening += delegate { openSelected.Enabled = !busy && list.SelectedIndices.Count > 0; };
            list.ContextMenuStrip = listMenu;
            var rowHeight = new ImageList { ImageSize = new Size(1, 38), ColorDepth = ColorDepth.Depth32Bit };
            rowHeight.Images.Add(new Bitmap(1, 38));
            list.SmallImageList = rowHeight;
            Disposed += delegate { rowHeight.Dispose(); };
            var listCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(3, 4, 3, 0) };
            var listViewport = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            var scrollBar = new RoundedScrollBar { Name = "ItemsScrollBar", Dock = DockStyle.Right, Width = 14, Visible = false };
            listViewport.Controls.Add(list);
            listViewport.Controls.Add(scrollBar);
            list.AttachScrollBar(scrollBar);
            listCard.Controls.Add(listViewport);
            var listHeader = new Panel { Name = "ItemsHeader", Dock = DockStyle.Top, Height = 34, BackColor = Theme.Surface, Margin = Padding.Empty };
            listHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                int left = listViewport.Left + list.Left - listHeader.Left;
                foreach (ColumnHeader column in list.Columns)
                {
                    DarkListView.DrawCellText(e.Graphics, column.Text, list.Font,
                        new Rectangle(left, 0, column.Width, listHeader.ClientSize.Height), Theme.Muted);
                    left += column.Width;
                }
            };
            listCard.Controls.Add(listHeader);
            EventHandler fitColumns = delegate
            {
                if (list.Columns.Count < 3) return;
                float scale = list.IsHandleCreated ? list.DeviceDpi / 96f : 1f;
                int first = (int)Math.Round(210 * scale), second = (int)Math.Round(90 * scale);
                int third = Math.Max(1, list.ClientSize.Width - first - second - 1);
                if (list.Columns[0].Width != first) list.Columns[0].Width = first;
                if (list.Columns[1].Width != second) list.Columns[1].Width = second;
                if (list.Columns[2].Width != third) list.Columns[2].Width = third;
                listHeader.Invalidate();
            };
            list.Resize += fitColumns;
            list.DpiChangedAfterParent += fitColumns;
            list.ColumnWidthChanged += delegate { listHeader.Invalidate(); };
            layout.Controls.Add(listCard, 0, 2);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Theme.Muted;
            status.Font = Theme.UiFont(9F);
            layout.Controls.Add(status, 0, 3);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(3) };
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var launchButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 4, 0, 0), Margin = Padding.Empty };
            launchButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            launchButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            launchButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            openExit = MakeButton("全部打开并退出本软件（Space）", async delegate { await OpenItems(true); }, true, 330);
            openExit.Name = "OpenAllAndExitButton";
            openExit.Height = 48;
            openExit.Font = Theme.UiFont(12F);
            openAll = MakeButton("全部打开", async delegate { await OpenItems(false); }, false, 145);
            openExit.Anchor = AnchorStyles.Right;
            openAll.Anchor = AnchorStyles.Right;
            launchButtons.Controls.Add(openAll, 1, 0);
            launchButtons.Controls.Add(openExit, 2, 0);
            bottom.Controls.Add(launchButtons, 0, 0);
            bottom.Controls.Add(new Label {
                Text = "已运行的软件不会重复打开；已打开的文件、文件夹和网站仍会打开。",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
                Font = Theme.UiFont(9F), ForeColor = Theme.Muted,
                Margin = new Padding(0, 0, 10, 0)
            }, 0, 1);
            autoOpen.Name = "AutoOpenCheckBox";
            autoOpen.Text = "启动本软件后自动打开全部项目，不显示本窗口，仅保留右下角托盘图标（建议开启）";
            autoOpen.AutoSize = true;
            autoOpen.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            autoOpen.Margin = new Padding(0, 2, 0, 8);
            autoOpen.ForeColor = Theme.Muted;
            autoOpen.Font = Theme.UiFont(9F);
            autoOpen.Cursor = Theme.SystemHand;
            bottom.Controls.Add(autoOpen, 0, 2);
            startWithWindows.Name = "StartWithWindowsCheckBox";
            startWithWindows.Text = "开机自启（建议开启）";
            startWithWindows.AutoSize = true;
            startWithWindows.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            startWithWindows.Margin = new Padding(0, 0, 0, 4);
            startWithWindows.ForeColor = Theme.Muted;
            startWithWindows.Font = Theme.UiFont(9F);
            startWithWindows.Cursor = Theme.SystemHand;
            bool portable = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.flag"))
                || Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--portable", StringComparison.OrdinalIgnoreCase));
            string startupCommand = StartupRegistration.Command(Application.ExecutablePath, portable);
            try { startWithWindows.Checked = StartupRegistration.IsEnabled(startupCommand); }
            catch { startWithWindows.Enabled = false; }
            bool updatingStartup = false;
            startWithWindows.CheckedChanged += delegate
            {
                if (updatingStartup) return;
                try { StartupRegistration.SetEnabled(startWithWindows.Checked, startupCommand); }
                catch (Exception ex)
                {
                    updatingStartup = true;
                    startWithWindows.Checked = !startWithWindows.Checked;
                    updatingStartup = false;
                    AppMessageBox.Show(this, UiText.T("设置开机自启失败：") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            bottom.Controls.Add(startWithWindows, 0, 3);
            const string authorName = "B站 · 每日DeepSeek消息";
            const string authorUrl = "https://space.bilibili.com/504149815/upload/video";
            string creditText = "作者：" + authorName;
            Color creditColor = Color.FromArgb(145, 150, 154);
            var credit = new SystemHandLinkLabel {
                Name = "AuthorCredit", Text = creditText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight,
                Font = Theme.UiFont(8F), ForeColor = creditColor, LinkColor = creditColor, VisitedLinkColor = creditColor,
                ActiveLinkColor = Theme.Muted, Margin = Padding.Empty
            };
            credit.Links.Clear();
            credit.Links.Add(creditText.IndexOf(authorName, StringComparison.Ordinal), authorName.Length, authorUrl);
            credit.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                try { Launcher.Open((string)e.Link.LinkData); }
                catch (Exception ex) { AppMessageBox.Show(this, UiText.T("无法打开作者主页：") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            var creditFooter = new Panel { Name = "CreditFooter", Dock = DockStyle.Bottom, Height = 34,
                Padding = new Padding(26, 0, 18, 0), BackColor = Theme.Main };
            const string projectUrl = "https://github.com/WalterLyle/quick-launcher";
            var projectLink = new SystemHandLinkLabel {
                Name = "ProjectLink", Text = "免费开源软件，Github链接：Quick Launcher", Dock = DockStyle.Bottom, Height = 17, TextAlign = ContentAlignment.BottomRight,
                Font = Theme.UiFont(8F), ForeColor = creditColor, LinkColor = creditColor, VisitedLinkColor = creditColor,
                ActiveLinkColor = Theme.Muted, Margin = Padding.Empty
            };
            projectLink.Links.Clear();
            projectLink.Links.Add(projectLink.Text.Length - "Quick Launcher".Length, "Quick Launcher".Length, projectUrl);
            projectLink.LinkClicked += delegate
            {
                try { Launcher.Open(projectUrl); }
                catch (Exception ex) { AppMessageBox.Show(this, UiText.T("无法打开项目主页：") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            var footerLinks = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Main };
            footerLinks.Controls.Add(credit);
            footerLinks.Controls.Add(projectLink);
            creditFooter.Controls.Add(footerLinks);
            languageButton = MakeButton("Switch to English", delegate { ToggleLanguage(); }, false, 150);
            languageButton.Name = "LanguageSwitchButton";
            languageButton.Font = Theme.UiFont(9F);
            languageButton.Dock = DockStyle.Left;
            creditFooter.Controls.Add(languageButton);
            Body.Controls.Add(creditFooter);
            layout.Controls.Add(bottom, 0, 4);
            try { paths.AddRange(Launcher.Load(config)); autoOpen.Checked = Launcher.LoadAutoOpen(config); }
            catch (Exception ex)
            {
                AppMessageBox.Show(this, UiText.T("无法读取已保存的列表：") + ex.Message + UiText.T("\n原文件将保留。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                try { File.Copy(config, config + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); }
                catch { Enabled = false; }
            }
            RefreshList();
            autoOpen.CheckedChanged += delegate { dirty = true; Persist(); RefreshList(); };
            Shown += async delegate
            {
                EnsureTrayIcon();
                if (automaticStartup) await RunStartup();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; return; }
                if (dirty && !Persist()) e.Cancel = true;
            };
            if (checkForUpdates) Shown += async delegate
            {
                await CheckForUpdates();
            };
            EnableFileDrop(this);
            ApplyLanguage();
            ResumeLayout(true);
        }

        void EnsureTrayIcon()
        {
            if (trayIcon != null) return;
            trayMenu = new RoundedContextMenu();
            trayMenu.Items.Add("打开编辑窗口", null, delegate { ShowMainWindow(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            var exitItem = trayMenu.Items.Add("退出", null, delegate { Close(); });
            trayMenu.Opening += delegate { exitItem.Enabled = !busy; };
            UiText.ApplyMenu(trayMenu);
            trayIcon = new NotifyIcon { Icon = Icon, Text = UiText.T("快捷启动器"), ContextMenuStrip = trayMenu, Visible = true };
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMainWindow(); };
        }

        internal void ShowMainWindow()
        {
            if (IsDisposed || Disposing) return;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        internal async Task RunStartup(Action<string> launch = null, bool showErrors = true)
        {
            if (startupHandled) return;
            startupHandled = true;
            if (!autoOpen.Checked || paths.Count == 0) return;
            if (await OpenItems(false, launch, showErrors) && !IsDisposed && !Disposing)
            {
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (additionToast != null) { additionToast.Dispose(); additionToast = null; }
                if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
                if (trayMenu != null) { trayMenu.Dispose(); trayMenu = null; }
                listMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V))
            {
                PasteFiles();
                return true;
            }
            if (keyData == Keys.Space && openExit != null)
            {
                if (openExit.Enabled) openExit.PerformClick();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        internal void PasteFiles(IDataObject source = null)
        {
            if (busy) return;
            try
            {
                IDataObject data = source ?? Clipboard.GetDataObject();
                if (data != null && data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null) AddPaths(files, true);
                }
            }
            catch (ExternalException) { ShowAdditionToast(UiText.T("剪贴板暂时无法读取，请重试。")); }
        }

        internal async Task CheckForUpdates(Func<Task<ReleaseUpdate>> fetch = null)
        {
            if (checkingForUpdates || IsDisposed || Disposing) return;
            checkingForUpdates = true;
            try
            {
                ReleaseUpdate update = await (fetch != null ? fetch() : UpdateChecker.Check(typeof(MainForm).Assembly.GetName().Version));
                if (IsDisposed || Disposing) return;
                availableUpdate = update;
                updateButton.Text = update == null ? UiText.T("有新版本") : UiText.F("更新 v{0}", update.Version.ToString(3));
                updateButton.Visible = update != null;
            }
            catch
            {
                // Offline, rate-limited or invalid responses must not interrupt launching.
            }
            finally { checkingForUpdates = false; }
        }
        void OpenUpdate()
        {
            if (availableUpdate == null) return;
            try { Launcher.Open(availableUpdate.DownloadUrl); }
            catch (Exception ex) { AppMessageBox.Show(this, UiText.T("无法打开更新页面：") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        Button MakeButton(string text, EventHandler action, bool primary, int width)
        {
            var button = new RoundedButton { Text = text, Width = width, Height = 36, Margin = new Padding(0, 0, 10, 0),
                Primary = primary, BackColor = primary ? Theme.Main : Theme.Surface, ForeColor = Theme.Text };
            button.Click += action;
            return button;
        }
        void EnableFileDrop(Control control)
        {
            // WinForms drag events do not bubble from child controls to their parent.
            control.AllowDrop = true;
            control.DragEnter += OnDragEnter;
            control.DragDrop += OnDragDrop;
            control.ControlAdded += delegate(object sender, ControlEventArgs e) { EnableFileDrop(e.Control); };
            foreach (Control child in control.Controls) EnableFileDrop(child);
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }
        void ShowAdditionToast(string message)
        {
            if (additionToast != null) additionToast.Dispose();
            additionToast = new CapsuleToast(this, message);
            additionToast.Present();
        }
        void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) AddPaths((string[])e.Data.GetData(DataFormats.FileDrop), true);
        }
        public void AddPaths(IEnumerable<string> incoming, bool showFeedback = false)
        {
            if (busy) return;
            int added = 0;
            var addedNames = new List<string>();
            var duplicateNames = new List<string>();
            foreach (string raw in incoming)
            {
                string path;
                try { path = Launcher.Normalize(raw); } catch { continue; }
                if (!Launcher.Exists(path)) continue;
                if (Launcher.Contains(paths, path))
                {
                    duplicateNames.Add(Launcher.DisplayName(path));
                    continue;
                }
                paths.Add(path); added++;
                addedNames.Add(Launcher.DisplayName(path));
            }
            if (added > 0) { dirty = true; Persist(); }
            RefreshList();
            if (showFeedback && (addedNames.Count > 0 || duplicateNames.Count > 0))
                ShowAdditionToast(AdditionFeedback(addedNames, duplicateNames));
        }
        internal static string AdditionFeedback(IEnumerable<string> added, IEnumerable<string> duplicates)
        {
            var messages = new List<string>();
            string names = string.Join(UiText.English ? ", " : "、", added);
            if (names.Length > 0) messages.Add(UiText.F("添加 {0} 成功！", names));
            names = string.Join(UiText.English ? ", " : "、", duplicates.Distinct());
            if (names.Length > 0) messages.Add(UiText.F("{0} 已添加过啦！", names));
            return string.Join("\n", messages);
        }
        void AddFiles()
        {
            if (busy) return;
            using (var dialog = new OpenFileDialog { Title = UiText.T("选择文件或快捷方式"), Multiselect = true, Filter = UiText.T("所有文件|*.*"), DereferenceLinks = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames, true);
        }
        public bool AddWebsite(string input, bool showFeedback = false)
        {
            if (busy) return false;
            string website = Launcher.NormalizeWebsite(input);
            if (Launcher.Contains(paths, website))
            {
                if (showFeedback) ShowAdditionToast(UiText.F("{0} 已添加过啦！", website));
                return false;
            }
            paths.Add(website);
            dirty = true;
            Persist();
            RefreshList();
            if (showFeedback) ShowAdditionToast(UiText.F("添加 {0} 成功！", website));
            return true;
        }
        void AddWebsiteFromDialog()
        {
            if (busy) return;
            using (var dialog = new WebsiteDialog(Font, Icon))
                if (dialog.ShowDialog(this) == DialogResult.OK) AddWebsite(dialog.Website, true);
        }
        void AddFolder()
        {
            if (busy) return;
            using (var dialog = new FolderBrowserDialog { Description = UiText.T("选择要添加的文件夹"), ShowNewFolderButton = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(new[] { dialog.SelectedPath }, true);
        }
        void RemoveSelected()
        {
            if (busy || list.SelectedIndices.Count == 0) return;
            var removedNames = list.SelectedIndices.Cast<int>().OrderBy(x => x).Select(index => Launcher.DisplayName(paths[index])).ToArray();
            foreach (int index in list.SelectedIndices.Cast<int>().OrderByDescending(x => x)) paths.RemoveAt(index);
            dirty = true; Persist(); RefreshList();
            ShowAdditionToast(UiText.F("移除 {0} 成功！", string.Join(UiText.English ? ", " : "、", removedNames)));
        }
        void ClearItems()
        {
            if (busy || paths.Count == 0) return;
            if (AppMessageBox.Show(this, UiText.T("清空启动列表？原文件不会被删除。"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            paths.Clear(); dirty = true; Persist(); RefreshList();
        }
        bool Persist()
        {
            try { Launcher.Save(config, paths, autoOpen.Checked, UiText.LanguageCode); dirty = false; return true; }
            catch (Exception ex)
            {
                AppMessageBox.Show(this, UiText.T("保存列表失败：") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
        void RefreshList()
        {
            list.BeginUpdate(); list.Items.Clear();
            foreach (string path in paths)
            {
                bool exists = Launcher.Exists(path);
                string type = Launcher.IsWebsite(path) ? "网站" : Directory.Exists(path) ? "文件夹" : Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? "快捷方式" : "文件";
                var item = new ListViewItem(Launcher.DisplayName(path));
                item.SubItems.Add(UiText.T(exists ? type : "已失效")); item.SubItems.Add(path); item.ToolTipText = path;
                item.ForeColor = exists ? Theme.Text : Color.FromArgb(240, 146, 146);
                list.Items.Add(item);
            }
            list.EndUpdate();
            openAll.Enabled = openExit.Enabled = !busy && paths.Count > 0;
            if (paths.Count == 0) SetStatus("拖入文件，或点击“添加文件”“添加网站”开始。");
            else SetStatus("共 {0} 项 · {1}", paths.Count, UiText.T(dirty ? "尚未保存" : "已保存"));
        }
        public async Task<bool> OpenItems(bool exit, Action<string> launch = null, bool showErrors = true)
        {
            return await OpenSnapshot(paths.ToArray(), exit, launch, showErrors);
        }
        internal async Task<bool> OpenSelectedItems(Action<string> launch = null, bool showErrors = true)
        {
            var selected = list.SelectedIndices.Cast<int>().OrderBy(index => index).Select(index => paths[index]).ToArray();
            return await OpenSnapshot(selected, false, launch, showErrors);
        }
        async Task<bool> OpenSnapshot(string[] snapshot, bool exit, Action<string> launch, bool showErrors)
        {
            if (busy || snapshot.Length == 0) return false;
            if (dirty && !Persist()) return false;
            busy = true; openAll.Enabled = openExit.Enabled = false;
            var errors = new List<string>();
            int skipped = 0;
            Action<string> opener = launch ?? Launcher.Open;
            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    SetStatus("正在打开 {0} / {1}…", i + 1, snapshot.Length);
                    string path = snapshot[i];
                    try
                    {
                        if (await Task.Run(() => Launcher.IsRunningApplication(path))) { skipped++; continue; }
                        await Task.Run(() => opener(path));
                    }
                    catch (Exception ex) { errors.Add(path + (UiText.English ? ": " : "：") + UiText.T(ex.Message)); }
                }
            }
            finally { busy = false; RefreshList(); }
            if (errors.Count > 0)
            {
                if (showErrors) AppMessageBox.Show(this, UiText.T("以下项目未能打开，请检查路径、网址或默认应用：\n\n") + string.Join("\n", errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("已发出 {0} 个打开请求，跳过已运行 {1} 项，失败 {2} 项。", snapshot.Length - errors.Count - skipped, skipped, errors.Count);
                return false;
            }
            SetStatus("已发出 {0} 个打开请求，跳过已运行 {1} 项。", snapshot.Length - skipped, skipped);
            if (exit) Close();
            return true;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--self-test") return LauncherTests.Run();
            using (var instance = new SingleInstance(SingleInstance.Name))
            {
                if (!instance.IsOwner)
                {
                    SingleInstance.ActivateExisting();
                    return 0;
                }
                string config = Launcher.ConfigPath(AppDomain.CurrentDomain.BaseDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    args.Any(arg => string.Equals(arg, "--portable", StringComparison.OrdinalIgnoreCase)));
                Application.Run(new MainForm(config));
            }
            return 0;
        }
    }
}
