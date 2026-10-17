using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace QuickLauncher
{
    internal static class UiText
    {
        internal static volatile bool English;
        internal static string LanguageCode { get { return English ? "en" : "zh-CN"; } }
        static readonly Dictionary<string, string> EnglishText = new Dictionary<string, string> {
            { "快捷启动器", "Quick Launcher" },
            { "你的常用项目，一键就绪", "Your essentials, one click away" },
            { "可直接拖入文件（包括快捷方式）或文件夹，也可直接添加网站。", "Drop files, shortcuts or folders here, or add a website." },
            { "添加文件", "Add files" }, { "添加文件夹", "Add folder" }, { "添加网站", "Add website" },
            { "移除选中", "Remove selected" }, { "清空列表", "Clear list" },
            { "名称", "Name" }, { "类型", "Type" }, { "路径 / 网址", "Path / URL" },
            { "网站", "Website" }, { "文件夹", "Folder" }, { "快捷方式", "Shortcut" }, { "文件", "File" }, { "已失效", "Missing" },
            { "仅打开选中的文件", "Open selected items only" }, { "全部打开", "Open all" },
            { "全部打开并退出本软件（Space）", "Open all and exit (Space)" },
            { "已运行的软件不会重复打开；已打开的文件、文件夹和网站仍会打开。", "Running apps are skipped; files, folders and websites still open." },
            { "启动本软件后自动打开全部项目，不显示本窗口，仅保留右下角托盘图标（建议开启）", "Open all items at startup and stay in the system tray (recommended)" },
            { "开机自启（建议开启）", "Start with Windows (recommended)" },
            { "作者：", "Author: " },
            { "B站 · 每日DeepSeek消息", "Bilibili · 每日DeepSeek消息" },
            { "免费开源软件，Github链接：Quick Launcher", "Free and open-source. Github link: Quick Launcher" },
            { "无法打开项目主页：", "Could not open the project page: " },
            { "打开编辑窗口", "Open editor" }, { "退出", "Exit" },
            { "关闭", "Close" }, { "最小化", "Minimize" }, { "最大化或还原", "Maximize or restore" },
            { "列表垂直滚动条", "Vertical item scrollbar" },
            { "有新版本", "Update available" }, { "更新 v{0}", "Update v{0}" },
            { "网站地址", "Website address" },
            { "例如 www.example.com，未填协议时自动使用 https。", "For example, www.example.com. HTTPS is added automatically." },
            { "取消", "Cancel" }, { "添加", "Add" }, { "确定", "OK" }, { "是", "Yes" }, { "否", "No" },
            { "选择文件或快捷方式", "Choose files or shortcuts" }, { "所有文件|*.*", "All files|*.*" },
            { "选择要添加的文件夹", "Choose a folder to add" },
            { "程序路径太长，请将软件放到较短的路径后再启用开机自启。", "The program path is too long. Move the app to a shorter path before enabling startup." },
            { "请输入有效网址，例如 https://www.example.com。", "Enter a valid website address, such as https://www.example.com." },
            { "请输入有效的 http 或 https 网站地址。", "Enter a valid HTTP or HTTPS website address." },
            { "文件已移动或删除", "The file has been moved or deleted" }, { "列表格式无效", "Invalid item list format" },
            { "设置开机自启失败：", "Could not change Windows startup settings: " },
            { "无法打开作者主页：", "Could not open the author's page: " },
            { "无法读取已保存的列表：", "Could not read the saved item list: " },
            { "\n原文件将保留。", "\nThe original file will be kept." },
            { "剪贴板暂时无法读取，请重试。", "The clipboard is temporarily unavailable. Please try again." },
            { "无法打开更新页面：", "Could not open the update page: " },
            { "添加 {0} 成功！", "Added {0}!" }, { "{0} 已添加过啦！", "{0} has already been added!" },
            { "移除 {0} 成功！", "Removed {0}!" },
            { "清空启动列表？原文件不会被删除。", "Clear the launch list? Your original files will not be deleted." },
            { "保存列表失败：", "Could not save the item list: " },
            { "保存语言设置失败：", "Could not save the language preference: " },
            { "拖入文件，或点击“添加文件”“添加网站”开始。", "Drop files here, or choose Add files or Add website to begin." },
            { "共 {0} 项 · {1}", "{0} items · {1}" }, { "尚未保存", "Unsaved" }, { "已保存", "Saved" },
            { "正在打开 {0} / {1}…", "Opening {0} / {1}…" },
            { "以下项目未能打开，请检查路径、网址或默认应用：\n\n", "These items could not be opened. Check their paths, URLs or default apps:\n\n" },
            { "已发出 {0} 个打开请求，跳过已运行 {1} 项，失败 {2} 项。", "Sent {0} open requests; skipped {1} running apps; {2} failed." },
            { "已发出 {0} 个打开请求，跳过已运行 {1} 项。", "Sent {0} open requests; skipped {1} running apps." }
        };
        sealed class Binding { internal string TextKey, AccessibleKey; }
        static readonly ConditionalWeakTable<Control, Binding> Controls = new ConditionalWeakTable<Control, Binding>();
        static readonly ConditionalWeakTable<ToolStripItem, Binding> MenuItems = new ConditionalWeakTable<ToolStripItem, Binding>();
        internal static string T(string chinese)
        {
            string english;
            return English && EnglishText.TryGetValue(chinese, out english) ? english : chinese;
        }
        internal static string F(string chinese, params object[] args) { return string.Format(CultureInfo.CurrentCulture, T(chinese), args); }
        internal static void SetLanguage(bool english)
        {
            English = english;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(english ? "en-US" : "zh-CN");
        }
        static string Key(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (EnglishText.ContainsKey(value)) return value;
            foreach (var pair in EnglishText) if (pair.Value == value) return pair.Key;
            return null;
        }
        internal static string TranslateKnown(string value)
        {
            string key = Key(value);
            return key == null ? value : T(key);
        }
        internal static void Apply(Control root)
        {
            Binding binding = Controls.GetValue(root, delegate(Control control) {
                return new Binding { TextKey = control is TextBoxBase ? null : Key(control.Text), AccessibleKey = Key(control.AccessibleName) };
            });
            if (binding.TextKey != null) root.Text = T(binding.TextKey);
            if (binding.AccessibleKey != null) root.AccessibleName = T(binding.AccessibleKey);
            foreach (Control child in root.Controls) Apply(child);
        }
        internal static void ApplyMenu(ToolStrip menu)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                Binding binding = MenuItems.GetValue(item, delegate(ToolStripItem entry) { return new Binding { TextKey = Key(entry.Text) }; });
                if (binding.TextKey != null) item.Text = T(binding.TextKey);
            }
        }
    }

    public sealed partial class MainForm
    {
        Button languageButton;
        string statusKey;
        object[] statusArguments;
        void SetStatus(string key, params object[] arguments)
        {
            statusKey = key; statusArguments = arguments;
            status.Text = UiText.F(key, arguments);
        }
        void ApplyLanguage()
        {
            SuspendLayout();
            UiText.Apply(this);
            languageButton.Text = UiText.English ? "切换到中文" : "Switch to English";
            UiText.ApplyMenu(listMenu);
            if (trayMenu != null) UiText.ApplyMenu(trayMenu);
            if (trayIcon != null) trayIcon.Text = UiText.T("快捷启动器");
            list.Columns[0].Text = UiText.T("名称");
            list.Columns[1].Text = UiText.T("类型");
            list.Columns[2].Text = UiText.T("路径 / 网址");
            foreach (ListViewItem item in list.Items)
                item.SubItems[1].Text = UiText.TranslateKnown(item.SubItems[1].Text);
            updateButton.Text = availableUpdate == null ? UiText.T("有新版本") : UiText.F("更新 v{0}", availableUpdate.Version.ToString(3));
            var credit = (LinkLabel)Controls.Find("AuthorCredit", true)[0];
            string author = UiText.T("B站 · 每日DeepSeek消息");
            credit.Text = UiText.T("作者：") + author;
            credit.Links.Clear();
            credit.Links.Add(credit.Text.Length - author.Length, author.Length, "https://space.bilibili.com/504149815/upload/video");
            var projectLink = (LinkLabel)Controls.Find("ProjectLink", true)[0];
            projectLink.Links.Clear();
            projectLink.Links.Add(projectLink.Text.Length - "Quick Launcher".Length, "Quick Launcher".Length, "https://github.com/WalterLyle/quick-launcher");
            if (statusKey != null)
            {
                if (statusKey == "共 {0} 项 · {1}") statusArguments[1] = UiText.T(dirty ? "尚未保存" : "已保存");
                status.Text = UiText.F(statusKey, statusArguments);
            }
            list.Invalidate();
            var headers = Controls.Find("ItemsHeader", true);
            if (headers.Length > 0) headers[0].Invalidate();
            ResumeLayout(true);
        }
        void ToggleLanguage()
        {
            UiText.SetLanguage(!UiText.English);
            if (additionToast != null) { additionToast.Dispose(); additionToast = null; }
            ApplyLanguage();
            dirty = true;
            if (!Persist()) return;
            if (statusKey == "共 {0} 项 · {1}") { statusArguments[1] = UiText.T("已保存"); status.Text = UiText.F(statusKey, statusArguments); }
        }
    }

    internal sealed class AppMessageDialog : RoundedForm
    {
        internal AppMessageDialog(string message, string title, bool question)
        {
            Text = title; Font = Theme.UiFont(10.5F);
            MinimizeBox = MaximizeBox = false; ShowInTaskbar = false;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            Size measured = TextRenderer.MeasureText(message, Font, new Size(500, 600), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            ClientSize = new Size(550, Math.Min(640, Math.Max(210, measured.Height + 140)));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 4, 12, 12), WrapContents = false };
            var accept = new RoundedButton { Text = UiText.T(question ? "是" : "确定"), Width = 100, Height = 34,
                DialogResult = question ? DialogResult.Yes : DialogResult.OK, Margin = new Padding(8, 0, 0, 0) };
            if (question)
            {
                var no = new RoundedButton { Text = UiText.T("否"), Width = 100, Height = 34, DialogResult = DialogResult.No };
                actions.Controls.Add(no); CancelButton = no;
            }
            else CancelButton = accept;
            actions.Controls.Add(accept); AcceptButton = accept;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 14, 20, 8) };
            var text = new Label { Text = message, AutoSize = true, MaximumSize = new Size(500, 0), UseMnemonic = false, Location = new Point(20, 14) };
            scroll.Controls.Add(text);
            Body.Controls.Add(scroll); Body.Controls.Add(actions);
            UiText.Apply(this);
        }
    }
    internal static class AppMessageBox
    {
        internal static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (var dialog = new AppMessageDialog(text, caption, buttons == MessageBoxButtons.YesNo)) return dialog.ShowDialog(owner);
        }
    }
}
