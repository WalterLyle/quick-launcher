using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickLauncher
{
    internal static class UpdaterTests
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new Exception(message);
        }
        static void Wait(Task task) { while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } task.GetAwaiter().GetResult(); }

        internal static void Run(string session)
        {
            string work = Path.Combine(session, "auto-update");
            Directory.CreateDirectory(work);
            string name = "QuickLauncher-1.0.0-windows-portable.zip", hash = new string('a', 64);
            string html = "<clipboard-copy value=\"sha256:" + hash + "\" aria-label=\"Copy to clipboard digest for " + name + "\">";
            Check(AutoUpdater.ParseDigest(html, name) == hash, "附件 SHA-256 解析错误");
            Reject(() => AutoUpdater.ParseDigest(html, "other.zip"), "接受了其他附件校验值");
            Reject(() => AutoUpdater.ParseDigest(html.Replace(hash, "invalid"), name), "接受了无效校验值");
            string sample = Path.Combine(work, "download.zip");
            File.WriteAllText(sample, "download bytes");
            string realHash;
            using (var sha = SHA256.Create()) realHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sample))).Replace("-", "");
            AutoUpdater.VerifyHash(sample, realHash);
            File.AppendAllText(sample, "corruption");
            Reject(() => AutoUpdater.VerifyHash(sample, realHash), "下载损坏后仍通过校验");
            Reject(() => AutoUpdater.VerifyHash(sample, ""), "缺少校验值仍允许更新");
            string source = Path.Combine(work, "source"), target = Path.Combine(work, "target 中文 空格");
            Directory.CreateDirectory(source); Directory.CreateDirectory(target);
            foreach (string file in AutoUpdater.PortableFiles)
            {
                File.WriteAllText(Path.Combine(source, file), "new " + file);
                File.WriteAllText(Path.Combine(target, file), "old " + file);
            }
            Directory.CreateDirectory(Path.Combine(target, "data"));
            string config = Path.Combine(target, "data", "items.xml");
            Launcher.Save(config, new[] { "https://example.com/keep" }, true, "en");
            byte[] originalConfig = File.ReadAllBytes(config);
            File.WriteAllText(Path.Combine(target, "personal.txt"), "keep user file");
            AutoUpdater.ApplyPortable(source, target, Path.Combine(work, "backup"));
            Check(AutoUpdater.PortableFiles.All(file => File.ReadAllText(Path.Combine(target, file)) == "new " + file), "便携更新文件替换失败");
            Check(originalConfig.SequenceEqual(File.ReadAllBytes(config)) && File.ReadAllText(Path.Combine(target, "personal.txt")) == "keep user file", "更新破坏用户列表或其他文件");
            File.Delete(Path.Combine(target, "README.txt")); Directory.CreateDirectory(Path.Combine(target, "README.txt"));
            foreach (string file in AutoUpdater.PortableFiles) File.WriteAllText(Path.Combine(source, file), "next " + file);
            bool failed = false;
            try { AutoUpdater.ApplyPortable(source, target, Path.Combine(work, "rollback")); }
            catch (IOException) { failed = true; }
            Check(failed && File.ReadAllText(Path.Combine(target, "QuickLauncher.exe")) == "new QuickLauncher.exe", "失败时未回滚程序");
            Check(originalConfig.SequenceEqual(File.ReadAllBytes(config)), "回滚破坏个人配置");
            string goodZip = Path.Combine(work, "good.zip");
            using (var zip = ZipFile.Open(goodZip, ZipArchiveMode.Create))
                foreach (string file in AutoUpdater.PortableFiles)
                    if (file == "QuickLauncher.exe") zip.CreateEntryFromFile(Application.ExecutablePath, file);
                    else { using (var writer = new StreamWriter(zip.CreateEntry(file).Open())) writer.Write("fixture"); }
            Version current = typeof(UpdaterTests).Assembly.GetName().Version;
            AutoUpdater.ExtractPortable(goodZip, Path.Combine(work, "extracted"), current);
            Reject(() => AutoUpdater.ExtractPortable(goodZip, Path.Combine(work, "wrong-version"), new Version(9, 0, 0)), "错误程序集版本仍允许替换");
            string badZip = Path.Combine(work, "bad.zip"); File.Copy(goodZip, badZip);
            using (var zip = ZipFile.Open(badZip, ZipArchiveMode.Update)) zip.CreateEntry("../items.xml");
            Reject(() => AutoUpdater.ExtractPortable(badZip, Path.Combine(work, "bad-extract"), current), "接受了越界 ZIP 项目");
            Check(!File.Exists(Path.Combine(work, "items.xml")), "ZIP 越界写入");
            Check(AutoUpdater.InstallerArguments(target).Contains("/DIR=\"" + target + "\""), "安装目录参数未正确引用");
            var update = new ReleaseUpdate(new Version(9, 0, 0), "v9.0.0");
            using (var form = new MainForm(Path.Combine(work, "ui.xml"), false, false))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                Wait(form.CheckForUpdates(() => Task.FromResult(update)));
                bool handedOff = false;
                Task<bool> failedDownload = form.InstallUpdate((release, directory, portable, progress) => { throw new IOException("offline"); }, path => handedOff = true, false);
                Wait(failedDownload);
                Check(!failedDownload.Result && !handedOff && !form.IsDisposed, "下载失败却退出或启动安装");
                Check(form.AddWebsite("https://example.com/after-failure"), "更新失败后界面不能继续使用");
                var downloading = new TaskCompletionSource<string>();
                Task<bool> installing = form.InstallUpdate((release, directory, portable, progress) => downloading.Task, path => handedOff = path == "verified-plan", false);
                Check(!form.InstallUpdate().Result, "重复点击导致重复更新");
                Check(!form.AddWebsite("https://example.com/during-update"), "更新期间允许修改配置");
                form.Close(); Check(!form.IsDisposed, "下载中可提前关闭窗口");
                downloading.SetResult("verified-plan"); Wait(installing);
                Check(installing.Result && handedOff && form.IsDisposed, "下载成功后没有交接并退出");
                Check(Launcher.Load(Path.Combine(work, "ui.xml")).Contains("https://example.com/after-failure"), "更新退出前丢失列表");
            }
        }
    }
}
