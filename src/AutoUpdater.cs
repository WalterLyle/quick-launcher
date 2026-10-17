using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace QuickLauncher
{
    internal sealed class UpdatePlan
    {
        internal string Target, Package, Hash;
        internal Version Version;
        internal bool Portable, Restart = true, English;
        internal int ParentId;
        internal long ParentStarted;

        internal void Save(string path)
        {
            new XDocument(new XElement("update", new XAttribute("target", Target), new XAttribute("package", Package),
                new XAttribute("hash", Hash), new XAttribute("version", Version), new XAttribute("portable", Portable),
                new XAttribute("restart", Restart), new XAttribute("english", English),
                new XAttribute("parent", ParentId), new XAttribute("started", ParentStarted))).Save(path);
        }
        internal static UpdatePlan Load(string path)
        {
            var root = XDocument.Load(path).Root;
            return new UpdatePlan { Target = (string)root.Attribute("target"), Package = (string)root.Attribute("package"),
                Hash = (string)root.Attribute("hash"), Version = Version.Parse((string)root.Attribute("version")),
                Portable = (bool)root.Attribute("portable"), Restart = (bool)root.Attribute("restart"),
                English = (bool)root.Attribute("english"), ParentId = (int)root.Attribute("parent"),
                ParentStarted = (long)root.Attribute("started") };
        }
    }

    internal static class AutoUpdater
    {
        internal const string MutexName = "Local\\QuickLauncher.Update";
        internal static readonly string[] PortableFiles = { "QuickLauncher.exe", "QuickLauncher.exe.config", "LICENSE.txt", "LICENSE-SmileySans.txt", "README.txt", "portable.flag" };
        const long MaxPackageSize = 100 * 1024 * 1024;

        internal static string AssetName(ReleaseUpdate update, bool portable)
        {
            return "QuickLauncher-" + update.Version.ToString(3) + "-windows-" + (portable ? "portable.zip" : "setup.exe");
        }

        // Read only GitHub's exact package digest, never an executable URL from page content.
        internal static string ParseDigest(string html, string assetName)
        {
            foreach (Match match in Regex.Matches(html, "<clipboard-copy\\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            {
                var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match attribute in Regex.Matches(match.Value, "([\\w-]+)=\"([^\"]*)\""))
                    attributes[attribute.Groups[1].Value] = WebUtility.HtmlDecode(attribute.Groups[2].Value);
                string label, value;
                if (attributes.TryGetValue("aria-label", out label) && label == "Copy to clipboard digest for " + assetName
                    && attributes.TryGetValue("value", out value) && Regex.IsMatch(value, "^sha256:[a-fA-F0-9]{64}$"))
                    return value.Substring(7).ToLowerInvariant();
            }
            throw new InvalidDataException(UiText.T("无法获取下载文件的校验值，请稍后重试。"));
        }

        internal static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "QuickLauncher/" + typeof(AutoUpdater).Assembly.GetName().Version;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            return request;
        }

        internal static string ReadAssetPage(ReleaseUpdate update)
        {
            string tag = update.DownloadUrl.Substring(update.DownloadUrl.LastIndexOf('/') + 1);
            using (var response = (HttpWebResponse)Request("https://github.com/WalterLyle/quick-launcher/releases/expanded_assets/" + tag).GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                var buffer = new char[1024 * 1024];
                int length = 0, count;
                while (length < buffer.Length && (count = reader.Read(buffer, length, buffer.Length - length)) > 0) length += count;
                if (length == buffer.Length) throw new InvalidDataException("Release page too large.");
                return new string(buffer, 0, length);
            }
        }

        internal static void VerifyHash(string path, string expected)
        {
            if (expected == null || !Regex.IsMatch(expected, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Invalid SHA-256 digest.");
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(sha.ComputeHash(input)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(UiText.T("下载文件校验失败，请重试。"));
        }

        internal static Task<string> Prepare(ReleaseUpdate update, string target, bool portable, IProgress<int> progress)
        {
            return Task.Run(() =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "QuickLauncher-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    string name = AssetName(update, portable);
                    string hash = ParseDigest(ReadAssetPage(update), name);
                    string tag = update.DownloadUrl.Substring(update.DownloadUrl.LastIndexOf('/') + 1);
                    string package = Path.Combine(directory, name);
                    var request = Request("https://github.com/WalterLyle/quick-launcher/releases/download/" + tag + "/" + name);
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.ResponseUri.Scheme != Uri.UriSchemeHttps || response.ContentLength > MaxPackageSize)
                            throw new InvalidDataException("Unexpected download response.");
                        using (var input = response.GetResponseStream())
                        using (var output = File.Create(package))
                        {
                            byte[] buffer = new byte[65536];
                            long received = 0; int count, lastPercent = -1;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                received += count;
                                if (received > MaxPackageSize) throw new InvalidDataException("Download too large.");
                                output.Write(buffer, 0, count);
                                int percent = response.ContentLength > 0 ? (int)(received * 100 / response.ContentLength) : 0;
                                if (percent != lastPercent) { progress.Report(percent); lastPercent = percent; }
                            }
                            if (response.ContentLength >= 0 && received != response.ContentLength) throw new IOException("Incomplete download.");
                        }
                    }
                    VerifyHash(package, hash);
                    if (portable) ExtractPortable(package, Path.Combine(directory, "preflight"), update.Version);
                    else if (Normalize(new Version(FileVersionInfo.GetVersionInfo(package).ProductVersion)) != Normalize(update.Version))
                        throw new InvalidDataException("Installer version mismatch.");
                    string helper = Path.Combine(directory, "UpdateHelper.exe");
                    File.Copy(Application.ExecutablePath, helper);
                    File.Copy(Application.ExecutablePath + ".config", helper + ".config");
                    var plan = new UpdatePlan { Target = Path.GetFullPath(target), Package = package, Hash = hash,
                        Version = update.Version, Portable = portable, English = UiText.English };
                    using (var process = Process.GetCurrentProcess())
                    {
                        plan.ParentId = process.Id;
                        plan.ParentStarted = process.StartTime.ToUniversalTime().Ticks;
                    }
                    string planPath = Path.Combine(directory, "update.xml");
                    plan.Save(planPath);
                    return planPath;
                }
                catch { try { Directory.Delete(directory, true); } catch { } throw; }
            });
        }

        internal static Version Normalize(Version version)
        {
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        internal static void ExtractPortable(string package, string destination, Version version)
        {
            Directory.CreateDirectory(destination);
            using (var archive = ZipFile.OpenRead(package))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToArray();
                if (names.Length != PortableFiles.Length || !names.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(PortableFiles.OrderBy(x => x, StringComparer.Ordinal)))
                    throw new InvalidDataException("Unexpected files in portable update.");
                foreach (var entry in archive.Entries)
                {
                    if (entry.Length > MaxPackageSize) throw new InvalidDataException("Expanded file too large.");
                    entry.ExtractToFile(Path.Combine(destination, entry.FullName));
                }
            }
            if (Normalize(Version.Parse(FileVersionInfo.GetVersionInfo(Path.Combine(destination, "QuickLauncher.exe")).FileVersion)) != Normalize(version))
                throw new InvalidDataException("Portable version mismatch.");
        }

        // Called only after the old process has exited. Back up every replaced file first.
        internal static void ApplyPortable(string source, string target, string backup)
        {
            Directory.CreateDirectory(backup);
            foreach (string name in PortableFiles)
                if (File.Exists(Path.Combine(target, name))) File.Copy(Path.Combine(target, name), Path.Combine(backup, name));
            var replaced = new List<string>();
            try
            {
                foreach (string name in PortableFiles)
                {
                    string destination = Path.Combine(target, name);
                    if (Directory.Exists(destination)) throw new IOException("A folder blocks an update file: " + name);
                    string staged = Path.Combine(target, ".QuickLauncher-update-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        File.Copy(Path.Combine(source, name), staged);
                        if (File.Exists(destination)) File.Replace(staged, destination, null);
                        else File.Move(staged, destination);
                    }
                    finally { if (File.Exists(staged)) File.Delete(staged); }
                    replaced.Add(name);
                }
            }
            catch
            {
                foreach (string name in replaced.AsEnumerable().Reverse())
                {
                    string old = Path.Combine(backup, name), destination = Path.Combine(target, name);
                    if (File.Exists(old)) File.Copy(old, destination, true);
                    else File.Delete(destination);
                }
                throw;
            }
        }

        internal static string InstallerArguments(string target)
        {
            return "/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /CLOSEAPPLICATIONS /DIR=\"" + target.TrimEnd('\\') + "\"";
        }

        internal static void StartHelper(string planPath)
        {
            using (var helper = Process.Start(new ProcessStartInfo {
                FileName = Path.Combine(Path.GetDirectoryName(planPath), "UpdateHelper.exe"), Arguments = "--apply-update \"" + planPath + "\"",
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(planPath), CreateNoWindow = true })) { }
        }

        internal static int RunHelper(string planPath)
        {
            UpdatePlan plan = null;
            string work = Path.GetDirectoryName(Path.GetFullPath(planPath));
            try
            {
                if (!string.Equals(Path.GetDirectoryName(Application.ExecutablePath), work, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetDirectoryName(work), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || !Regex.IsMatch(Path.GetFileName(work), "^QuickLauncher-update-[a-f0-9]{32}$"))
                    throw new InvalidDataException("Invalid updater workspace.");
                plan = UpdatePlan.Load(planPath);
                UiText.SetLanguage(plan.English);
                using (var updating = new SingleInstance(MutexName))
                {
                    if (!updating.IsOwner) throw new IOException("Another update is already running.");
                    // Do not replace a different process that happens to reuse the same PID.
                    try
                    {
                        using (var parent = Process.GetProcessById(plan.ParentId))
                            if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStarted && !parent.WaitForExit(60000))
                                throw new IOException(UiText.T("启动器尚未退出，请关闭后重试。"));
                    }
                    catch (ArgumentException) { }
                    // Setup checks the application mutex itself, so release this probe before starting it.
                    using (var instance = new SingleInstance(SingleInstance.Name))
                    {
                        if (!instance.IsOwner) throw new IOException(UiText.T("请先退出其他启动器实例，再重试更新。"));
                    }
                    VerifyHash(plan.Package, plan.Hash);
                    string executable = Path.Combine(plan.Target, "QuickLauncher.exe");
                    if (!File.Exists(executable)) throw new FileNotFoundException("Launcher missing.", executable);
                    if (Normalize(plan.Version) <= Normalize(Version.Parse(FileVersionInfo.GetVersionInfo(executable).FileVersion)))
                        throw new InvalidDataException("Update must have a higher version than the installed launcher.");
                    if (plan.Portable)
                    {
                        string source = Path.Combine(work, "extracted");
                        ExtractPortable(plan.Package, source, plan.Version);
                        ApplyPortable(source, plan.Target, Path.Combine(work, "backup"));
                    }
                    else
                    {
                        if (Normalize(Version.Parse(FileVersionInfo.GetVersionInfo(plan.Package).ProductVersion)) != Normalize(plan.Version))
                            throw new InvalidDataException("Installer version mismatch.");
                        using (var setup = Process.Start(new ProcessStartInfo { FileName = plan.Package, Arguments = InstallerArguments(plan.Target), UseShellExecute = false, CreateNoWindow = true }))
                        {
                            setup.WaitForExit();
                            if (setup.ExitCode != 0) throw new IOException("Installer exit code: " + setup.ExitCode);
                        }
                    }
                    if (Normalize(Version.Parse(FileVersionInfo.GetVersionInfo(executable).FileVersion)) != Normalize(plan.Version))
                        throw new InvalidDataException("Installed version mismatch.");
                    File.WriteAllText(Path.Combine(work, "result.txt"), "PASS " + plan.Version);
                }
                if (plan.Restart) Restart(plan);
                // Only known temporary files are removed; never recurse into the install directory.
                TryClean(work);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(work, "result.txt"), "FAIL " + ex);
                if (plan == null || plan.Restart)
                {
                    MessageBox.Show(UiText.T("更新失败，原列表和设置已保留：") + ex.Message + "\n\n" + work,
                        UiText.T("快捷启动器"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (plan != null) try { Restart(plan); } catch { }
                }
                return 1;
            }
        }

        static void Restart(UpdatePlan plan)
        {
            using (var process = Process.Start(new ProcessStartInfo { FileName = Path.Combine(plan.Target, "QuickLauncher.exe"),
                Arguments = "--updated" + (plan.Portable ? " --portable" : ""), WorkingDirectory = plan.Target, UseShellExecute = false })) { }
        }
        static void TryClean(string work)
        {
            try
            {
                foreach (string name in new[] { "preflight", "extracted", "backup" })
                {
                    string path = Path.Combine(work, name);
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                }
                foreach (string path in Directory.GetFiles(work))
                    if (Path.GetFileName(path) != "UpdateHelper.exe" && Path.GetFileName(path) != "UpdateHelper.exe.config" && Path.GetFileName(path) != "result.txt") File.Delete(path);
            }
            catch { }
        }
    }
}
