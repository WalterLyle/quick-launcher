using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace QuickLauncher
{
    internal sealed class ReleaseUpdate
    {
        internal readonly Version Version;
        internal readonly string DownloadUrl;

        internal ReleaseUpdate(Version version, string tag)
        {
            Version = version;
            DownloadUrl = "https://github.com/WalterLyle/quick-launcher/releases/tag/" + Uri.EscapeDataString(tag);
        }
    }

    internal static class UpdateChecker
    {
        const string Endpoint = "https://api.github.com/repos/WalterLyle/quick-launcher/releases/latest";

        static Version NewerVersion(string tag, Version currentVersion)
        {
            if (tag == null || !Regex.IsMatch(tag, @"^[vV]?\d+\.\d+\.\d+(?:\.\d+)?$")) return null;
            Version version;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out version)) return null;
            version = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
            currentVersion = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build), Math.Max(0, currentVersion.Revision));
            return version > currentVersion ? version : null;
        }

        internal static ReleaseUpdate ParseLatestLink(string location, Version currentVersion)
        {
            const string prefix = "https://github.com/WalterLyle/quick-launcher/releases/tag/";
            if (location == null || !location.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string tag = location.Substring(prefix.Length);
            Version version = NewerVersion(tag, currentVersion);
            return version == null ? null : new ReleaseUpdate(version, tag);
        }

        internal static ReleaseUpdate Parse(string json, Version currentVersion)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 262144 };
            var release = serializer.DeserializeObject(json) as Dictionary<string, object>;
            object value;
            if (release == null || !release.TryGetValue("draft", out value) || !(value is bool) || (bool)value) return null;
            if (!release.TryGetValue("prerelease", out value) || !(value is bool) || (bool)value) return null;
            if (!release.TryGetValue("tag_name", out value)) return null;
            string tag = value as string;
            Version version = NewerVersion(tag, currentVersion);
            if (version == null) return null;
            if (!release.TryGetValue("assets", out value) || !(value is IEnumerable)) return null;
            string prefix = "QuickLauncher-" + tag.TrimStart('v', 'V') + "-windows-";
            bool hasDownload = ((IEnumerable)value).OfType<Dictionary<string, object>>().Any(asset =>
            {
                object name;
                object state;
                return asset.TryGetValue("name", out name) && asset.TryGetValue("state", out state)
                    && string.Equals(state as string, "uploaded", StringComparison.Ordinal)
                    && (string.Equals(name as string, prefix + "setup.exe", StringComparison.Ordinal)
                        || string.Equals(name as string, prefix + "portable.zip", StringComparison.Ordinal));
            });
            return hasDownload ? new ReleaseUpdate(version, tag) : null;
        }

        internal static Task<ReleaseUpdate> Check(Version currentVersion)
        {
            return Task.Run(() =>
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(Endpoint);
                request.Method = "GET";
                request.UserAgent = "QuickLauncher/" + currentVersion;
                request.Accept = "application/vnd.github+json";
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        // Bound memory even if an unexpected server response is received.
                        var buffer = new char[262145];
                        int length = 0;
                        int count;
                        while (length < buffer.Length && (count = reader.Read(buffer, length, buffer.Length - length)) > 0) length += count;
                        if (length == buffer.Length) return null;
                        return Parse(new string(buffer, 0, length), currentVersion);
                    }
                }
                catch (WebException ex)
                {
                    using (var response = ex.Response as HttpWebResponse)
                    {
                        if (response == null || (response.StatusCode != HttpStatusCode.Forbidden && (int)response.StatusCode != 429)) throw;
                    }
                    return CheckLatestLink(currentVersion);
                }
            });
        }

        static ReleaseUpdate CheckLatestLink(Version currentVersion)
        {
            // Public release links do not consume the REST API request allowance.
            var request = HeadRequest("https://github.com/WalterLyle/quick-launcher/releases/latest", currentVersion);
            ReleaseUpdate update;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.Found) return null;
                string location = response.Headers["Location"];
                if (location != null && location.StartsWith("/", StringComparison.Ordinal)) location = "https://github.com" + location;
                update = ParseLatestLink(location, currentVersion);
            }
            if (update == null) return null;
            string tag = update.DownloadUrl.Substring(update.DownloadUrl.LastIndexOf('/') + 1);
            string asset = "https://github.com/WalterLyle/quick-launcher/releases/download/" + tag
                + "/QuickLauncher-" + tag.TrimStart('v', 'V') + "-windows-setup.exe";
            using (var response = (HttpWebResponse)HeadRequest(asset, currentVersion).GetResponse())
                return response.StatusCode == HttpStatusCode.Found || response.StatusCode == HttpStatusCode.OK ? update : null;
        }
        static HttpWebRequest HeadRequest(string address, Version version)
        {
            var request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = "HEAD";
            request.UserAgent = "QuickLauncher/" + version;
            request.AllowAutoRedirect = false;
            request.Timeout = request.ReadWriteTimeout = 5000;
            return request;
        }
    }
}
