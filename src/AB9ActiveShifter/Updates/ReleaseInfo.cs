using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AB9ActiveShifter.Updates
{
    /// <summary>Release metadata only: no network, files, or device access.</summary>
    public sealed class ReleaseInfo
    {
        public const string RepositoryUrl = "https://github.com/Gugic/ab9-shifter-simhub-plugin";
        public const string LatestApiUrl = "https://api.github.com/repos/Gugic/ab9-shifter-simhub-plugin/releases/latest";
        public const string DllName = "AB9ActiveShifter.dll";
        public const int MaximumDllBytes = 16 * 1024 * 1024;

        public string Version { get; private set; }
        public string PageUrl { get; private set; }
        public string Notes { get; private set; }
        public string DownloadUrl { get; private set; }
        public long DownloadBytes { get; private set; }
        public string Sha256 { get; private set; }
        public bool IsNewer { get; private set; }
        public bool CanInstall { get { return IsNewer && DownloadUrl != null && Sha256 != null; } }

        public static ReleaseInfo Parse(string json, string installedVersion)
        {
            JObject release = JObject.Parse(json);
            if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true)
                throw new InvalidDataException("The latest release is not a published stable release.");

            string tag = (string)release["tag_name"];
            System.Version numeric;
            bool prerelease;
            if (!TryVersion(tag, out numeric, out prerelease) || prerelease || tag.Contains("+"))
                throw new InvalidDataException("The release tag is not a stable version.");

            string expectedPage = RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(tag);
            if (!SameUrl((string)release["html_url"], expectedPage))
                throw new InvalidDataException("The release page is outside this plugin's repository.");

            ReleaseInfo info = new ReleaseInfo
            {
                Version = numeric.ToString(3),
                PageUrl = expectedPage,
                Notes = (string)release["body"] ?? "No release notes were supplied.",
                IsNewer = IsNewerStable(tag, installedVersion)
            };

            string expectedDownload = RepositoryUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + DllName;
            JArray assets = release["assets"] as JArray;
            if (assets == null) return info;
            foreach (JToken asset in assets)
            {
                // Source archives and a DLL from another repository are never install candidates.
                if ((string)asset["name"] != DllName || (string)asset["state"] != "uploaded" ||
                    !SameUrl((string)asset["browser_download_url"], expectedDownload)) continue;
                long bytes;
                if (!long.TryParse((string)asset["size"], NumberStyles.None, CultureInfo.InvariantCulture, out bytes) ||
                    bytes < 1024 || bytes > MaximumDllBytes) continue;
                string digest = (string)asset["digest"];
                if (digest == null || !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")) continue;
                info.DownloadUrl = expectedDownload;
                info.DownloadBytes = bytes;
                info.Sha256 = digest.Substring(7).ToLowerInvariant();
                break;
            }
            return info;
        }

        public static bool IsNewerStable(string candidate, string installed)
        {
            System.Version next, current;
            bool nextPrerelease, currentPrerelease;
            if (!TryVersion(candidate, out next, out nextPrerelease) || nextPrerelease ||
                !TryVersion(installed, out current, out currentPrerelease)) return false;
            int comparison = next.CompareTo(current);
            return comparison > 0 || (comparison == 0 && currentPrerelease);
        }

        private static bool TryVersion(string text, out System.Version version, out bool prerelease)
        {
            version = null;
            prerelease = false;
            Match match = Regex.Match(text ?? "", @"^[vV]?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+([0-9A-Za-z.-]+))?$");
            if (!match.Success) return false;
            prerelease = match.Groups[4].Success;
            return System.Version.TryParse(match.Groups[1].Value + "." + match.Groups[2].Value + "." + match.Groups[3].Value, out version);
        }

        private static bool SameUrl(string text, string expected)
        {
            Uri uri;
            return Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
                uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                string.Equals(uri.AbsoluteUri, expected, StringComparison.Ordinal);
        }
    }
}
