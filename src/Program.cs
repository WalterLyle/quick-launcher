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
            if (value.Length == 0 || value.Any(char.IsWhiteSpace)) throw new FormatException("请输入有效网址，例如 https://www.example.com。");
            if (value.IndexOf("://", StringComparison.Ordinal) < 0) value = "https://" + value;
            if (!IsWebsite(value)) throw new FormatException("请输入有效的 http 或 https 网站地址。");
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
            if (!Exists(path)) throw new FileNotFoundException("文件已移动或删除", path);
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
            if (root == null || root.Name != "launcher") throw new InvalidDataException("列表格式无效");
            return root.Elements("item").Select(x => Normalize((string)x.Attribute("path")))
                .Distinct(Comparer).ToList();
        }
        public static bool Contains(IEnumerable<string> paths, string path) { return paths.Contains(path, Comparer); }
        public static void Save(string file, IEnumerable<string> paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new XDocument(new XElement("launcher", paths.Select(p => new XElement("item", new XAttribute("path", p))))).Save(temp);
                if (File.Exists(file)) File.Replace(temp, file, file + ".bak");
                else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    internal sealed class WebsiteDialog : Form
    {
        internal string Website { get; private set; }

        internal WebsiteDialog(Font font, Icon icon)
        {
            Text = "添加网站";
            Font = font;
            Icon = icon;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 195);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.Controls.Add(new Label { Text = "网站地址", AutoSize = true }, 0, 0);
            var address = new TextBox { Dock = DockStyle.Top, Name = "WebsiteAddress", MaxLength = 8192 };
            layout.Controls.Add(address, 0, 1);
            layout.Controls.Add(new Label { Text = "例如 www.example.com，未填协议时自动使用 https。", AutoSize = true, ForeColor = Color.DimGray }, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 86, Height = 32 };
            var add = new Button { Text = "添加", Width = 86, Height = 32 };
            add.Click += delegate
            {
                try { Website = Launcher.NormalizeWebsite(address.Text); DialogResult = DialogResult.OK; }
                catch (FormatException ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    address.Focus();
                }
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(add);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            AcceptButton = add;
            CancelButton = cancel;
            ActiveControl = address;
        }
    }

    public sealed class MainForm : Form
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == SingleInstance.ActivateMessage)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show();
                Activate();
            }
            base.WndProc(ref message);
        }

        readonly string config;
        readonly List<string> paths = new List<string>();
        readonly ListView list = new ListView();
        readonly Label status = new Label();
        readonly Button openAll;
        readonly Button openExit;
        bool busy;
        bool dirty;

        public MainForm(string configPath)
        {
            config = configPath;
            using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("QuickLauncher.AppIcon"))
            using (var appIcon = new Icon(iconStream, new Size(32, 32)))
                Icon = (Icon)appIcon.Clone();
            Text = "快捷启动器";
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(820, 570);
            MinimumSize = new Size(720, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleMode = AutoScaleMode.Dpi;
            AllowDrop = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            Controls.Add(layout);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "你的常用项目，一键就绪", AutoSize = true, Location = new Point(0, 0),
                Font = new Font(Font.FontFamily, 21F, FontStyle.Bold), ForeColor = Color.FromArgb(24, 35, 58) });
            header.Controls.Add(new Label { Text = "拖入文件、文件夹或快捷方式，也可直接添加网站。列表自动保存。", AutoSize = true,
                Location = new Point(2, 47), ForeColor = Color.FromArgb(96, 108, 128) });
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
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = true;
            list.ShowItemToolTips = true;
            list.AllowDrop = true;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.Columns.Add("名称", 210);
            list.Columns.Add("类型", 90);
            list.Columns.Add("路径 / 网址", 430);
            list.DragEnter += OnDragEnter;
            list.DragDrop += OnDragDrop;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            list.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            layout.Controls.Add(list, 0, 2);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Color.FromArgb(96, 108, 128);
            layout.Controls.Add(status, 0, 3);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(3) };
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var launchButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
            openExit = MakeButton("全部打开并退出", async delegate { await OpenItems(true); }, true, 195);
            openAll = MakeButton("全部打开", async delegate { await OpenItems(false); }, false, 145);
            launchButtons.Controls.Add(openExit);
            launchButtons.Controls.Add(openAll);
            bottom.Controls.Add(launchButtons, 0, 0);
            bottom.Controls.Add(new Label {
                Text = "已运行的软件会跳过；网站、文件夹和文件仍会打开。",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
                Font = new Font(Font.FontFamily, 9F), ForeColor = Color.FromArgb(96, 108, 128),
                Margin = new Padding(0, 0, 10, 0)
            }, 0, 1);
            layout.Controls.Add(bottom, 0, 4);
            try { paths.AddRange(Launcher.Load(config)); }
            catch (Exception ex)
            {
                MessageBox.Show("无法读取已保存的列表：" + ex.Message + "\n原文件将保留。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                try { File.Copy(config, config + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); }
                catch { Enabled = false; }
            }
            RefreshList();
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; return; }
                if (dirty && !Persist()) e.Cancel = true;
            };
        }

        Button MakeButton(string text, EventHandler action, bool primary, int width)
        {
            var button = new Button { Text = text, Width = width, Height = 36, Margin = new Padding(0, 0, 10, 0),
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(45, 91, 224) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(39, 54, 80), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = primary ? button.BackColor : Color.FromArgb(215, 221, 232);
            button.Click += action;
            return button;
        }
        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }
        void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
        }
        public void AddPaths(IEnumerable<string> incoming)
        {
            if (busy) return;
            int added = 0;
            foreach (string raw in incoming)
            {
                string path;
                try { path = Launcher.Normalize(raw); } catch { continue; }
                if (!Launcher.Exists(path) || Launcher.Contains(paths, path)) continue;
                paths.Add(path); added++;
            }
            if (added > 0) { dirty = true; Persist(); }
            RefreshList();
        }
        void AddFiles()
        {
            if (busy) return;
            using (var dialog = new OpenFileDialog { Title = "选择文件或快捷方式", Multiselect = true, Filter = "所有文件|*.*", DereferenceLinks = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames);
        }
        public bool AddWebsite(string input)
        {
            if (busy) return false;
            string website = Launcher.NormalizeWebsite(input);
            if (Launcher.Contains(paths, website)) return false;
            paths.Add(website);
            dirty = true;
            Persist();
            RefreshList();
            return true;
        }
        void AddWebsiteFromDialog()
        {
            if (busy) return;
            using (var dialog = new WebsiteDialog(Font, Icon))
                if (dialog.ShowDialog(this) == DialogResult.OK && !AddWebsite(dialog.Website))
                    MessageBox.Show(this, "该网站已在列表中。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        void AddFolder()
        {
            if (busy) return;
            using (var dialog = new FolderBrowserDialog { Description = "选择要添加的文件夹", ShowNewFolderButton = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(new[] { dialog.SelectedPath });
        }
        void RemoveSelected()
        {
            if (busy || list.SelectedIndices.Count == 0) return;
            foreach (int index in list.SelectedIndices.Cast<int>().OrderByDescending(x => x)) paths.RemoveAt(index);
            dirty = true; Persist(); RefreshList();
        }
        void ClearItems()
        {
            if (busy || paths.Count == 0) return;
            if (MessageBox.Show(this, "清空启动列表？原文件不会被删除。", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            paths.Clear(); dirty = true; Persist(); RefreshList();
        }
        bool Persist()
        {
            try { Launcher.Save(config, paths); dirty = false; return true; }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存列表失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                item.SubItems.Add(exists ? type : "已失效"); item.SubItems.Add(path); item.ToolTipText = path;
                if (!exists) item.ForeColor = Color.FromArgb(190, 65, 65);
                list.Items.Add(item);
            }
            list.EndUpdate();
            openAll.Enabled = openExit.Enabled = !busy && paths.Count > 0;
            status.Text = paths.Count == 0 ? "拖入文件，或点击“添加文件”“添加网站”开始。" : "共 " + paths.Count + " 项 · " + (dirty ? "尚未保存" : "已保存") + " · 开启后由 Windows 默认应用处理";
        }
        public async Task<bool> OpenItems(bool exit, Action<string> launch = null, bool showErrors = true)
        {
            if (busy || paths.Count == 0) return false;
            if (dirty && !Persist()) return false;
            busy = true; openAll.Enabled = openExit.Enabled = false;
            var errors = new List<string>();
            int skipped = 0;
            var snapshot = paths.ToArray();
            Action<string> opener = launch ?? Launcher.Open;
            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    status.Text = "正在打开 " + (i + 1) + " / " + snapshot.Length + "…";
                    string path = snapshot[i];
                    try
                    {
                        if (await Task.Run(() => Launcher.IsRunningApplication(path))) { skipped++; continue; }
                        await Task.Run(() => opener(path));
                    }
                    catch (Exception ex) { errors.Add(path + "：" + ex.Message); }
                }
            }
            finally { busy = false; RefreshList(); }
            if (errors.Count > 0)
            {
                if (showErrors) MessageBox.Show(this, "以下项目未能打开，请检查路径、网址或默认应用：\n\n" + string.Join("\n", errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                status.Text = "已发出 " + (snapshot.Length - errors.Count - skipped) + " 个打开请求，跳过已运行 " + skipped + " 项，失败 " + errors.Count + " 项。";
                return false;
            }
            status.Text = "已发出 " + (snapshot.Length - skipped) + " 个打开请求，跳过已运行 " + skipped + " 项。";
            if (exit) Close();
            return true;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
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
