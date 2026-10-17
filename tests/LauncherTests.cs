using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickLauncher
{
    static class LauncherTests
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        static T Wait<T>(Task<T> task)
        {
            while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            return task.GetAwaiter().GetResult();
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
                using (var webForm = new MainForm(Path.Combine(session, "web-form.xml")))
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
                using (var form = new MainForm(Path.Combine(session, "form.xml")))
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
                using (var form = new MainForm(Path.Combine(session, "running.xml")))
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
                File.WriteAllText(report, "PASS: 网址校验与 HTTPS 补全、URL 参数保留、网站直接 Shell 打开配置、混合列表保存恢复与去重、网站失败后继续及保留界面、混合列表全部打开并退出、已运行软件及快捷方式跳过、同名不同路径区分、普通文件继续打开、跳过后退出、单实例、阻止重复实例、退出后重新启动、配置恢复、原子保存与备份、中文和特殊字符路径、去重、无效路径过滤、全部打开、失败后继续、失败保留界面、全部打开并退出、真实 .lnk 启动。\r\n");
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
        }
    }
}
