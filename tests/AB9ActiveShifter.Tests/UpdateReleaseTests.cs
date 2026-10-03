using System.IO;
using AB9ActiveShifter.Updates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>Release decisions are pure; these tests never contact GitHub or replace files.</summary>
    public class UpdateReleaseTests
    {
        [Theory]
        [InlineData("v0.14.0", "0.13.0", true)]
        [InlineData("v0.13.0", "0.13.0+2992c6c", false)]
        [InlineData("v0.12.0", "0.13.0", false)]
        [InlineData("v0.13.0", "0.13.0-rc1+2992c6c", true)]
        [InlineData("v0.12.0", "0.13.0-ci.42+2992c6c", false)]
        [InlineData("v0.10.0", "0.9.0", true)]
        [InlineData("v1.0.0", "0.99.99", true)]
        [InlineData("v1.2.3-rc1", "1.2.2", false)]
        [InlineData("v1.2", "1.1.0", false)]
        [InlineData("main", "1.1.0", false)]
        [InlineData("v1.2.3", "unknown", false)]
        [InlineData("v999999999999.0.0", "1.0.0", false)]
        public void AStableReleaseNeverDowngradesOrConfusesMetadataWithVersion(string next, string current, bool expected)
        {
            Assert.Equal(expected, ReleaseInfo.IsNewerStable(next, current));
        }

        [Fact]
        public void AReleaseUsesOnlyItsOwnExactDllAssetAndGitHubChecksum()
        {
            JObject json = Release();
            ((JArray)json["assets"]).Insert(0, new JObject
            {
                ["name"] = "source.zip",
                ["browser_download_url"] = "https://example.com/source.zip"
            });
            ReleaseInfo result = Parse(json);
            Assert.True(result.IsNewer);
            Assert.True(result.CanInstall);
            Assert.Equal("0.14.0", result.Version);
            Assert.Equal("Release notes", result.Notes);
            Assert.Equal(ReleaseInfo.RepositoryUrl + "/releases/download/v0.14.0/AB9ActiveShifter.dll", result.DownloadUrl);
            Assert.Equal(300000, result.DownloadBytes);
            Assert.Equal(new string('a', 64), result.Sha256);
        }

        [Theory]
        [InlineData("https://github.com/stranger/ab9-shifter-simhub-plugin/releases/download/v0.14.0/AB9ActiveShifter.dll")]
        [InlineData("http://github.com/Gugic/ab9-shifter-simhub-plugin/releases/download/v0.14.0/AB9ActiveShifter.dll")]
        [InlineData("https://github.com.evil.test/Gugic/ab9-shifter-simhub-plugin/releases/download/v0.14.0/AB9ActiveShifter.dll")]
        [InlineData("https://github.com/Gugic/ab9-shifter-simhub-plugin/releases/download/v0.13.0/AB9ActiveShifter.dll")]
        [InlineData("https://user@github.com/Gugic/ab9-shifter-simhub-plugin/releases/download/v0.14.0/AB9ActiveShifter.dll")]
        [InlineData("https://github.com/Gugic/ab9-shifter-simhub-plugin/releases/download/v0.14.0/AB9ActiveShifter.dll?redirect=evil")]
        [InlineData("https://github.com/Gugic/ab9-shifter-simhub-plugin/releases/download/v0.14.0/AB9ActiveShifter.dll#fragment")]
        [InlineData("https://github.com:444/Gugic/ab9-shifter-simhub-plugin/releases/download/v0.14.0/AB9ActiveShifter.dll")]
        public void AnUntrustedAssetCanBeNotifiedButNeverInstalled(string url)
        {
            JObject json = Release();
            json["assets"][0]["browser_download_url"] = url;
            ReleaseInfo result = Parse(json);
            Assert.True(result.IsNewer);
            Assert.False(result.CanInstall);
            Assert.Null(result.DownloadUrl);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1023)]
        [InlineData(16777217)]
        public void AnEmptyOrOversizedAssetIsNeverDownloaded(long bytes)
        {
            JObject json = Release();
            json["assets"][0]["size"] = bytes;
            Assert.False(Parse(json).CanInstall);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("sha256:abc")]
        [InlineData("md5:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void InstallationRequiresAVerifiableSha256Digest(string digest)
        {
            JObject json = Release();
            json["assets"][0]["digest"] = digest;
            Assert.False(Parse(json).CanInstall);
        }

        [Fact]
        public void AHandPublishedReleaseWithoutAnAssetStillHasReleaseNotes()
        {
            JObject json = Release();
            json.Remove("assets");
            ReleaseInfo result = Parse(json);
            Assert.True(result.IsNewer);
            Assert.False(result.CanInstall);
            Assert.Equal("Release notes", result.Notes);
        }

        [Fact]
        public void AnUpToDateReleaseCannotBeReinstalled()
        {
            Assert.False(ReleaseInfo.Parse(Release().ToString(), "0.14.0").CanInstall);
        }

        [Theory]
        [InlineData("draft")]
        [InlineData("prerelease")]
        public void UnpublishedAndPreviewReleasesAreRefused(string field)
        {
            JObject json = Release();
            json[field] = true;
            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Theory]
        [InlineData("main")]
        [InlineData("v0.14.0-rc1")]
        [InlineData("v0.14.0+local")]
        public void AStableChannelRejectsNonStableTags(string tag)
        {
            JObject json = Release();
            json["tag_name"] = tag;
            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Fact]
        public void ReleaseNotesCannotSendTheBrowserToAnotherRepository()
        {
            JObject json = Release();
            json["html_url"] = "https://github.com/stranger/plugin/releases/tag/v0.14.0";
            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Fact]
        public void InvalidJsonFailsTheCheckInsteadOfInventingAnUpdate()
        {
            Assert.Throws<JsonReaderException>(() => ReleaseInfo.Parse("not JSON", "0.13.0"));
        }

        [Fact]
        public void UpdatePreferencesPersistOnTheStoreAndAreAbsentFromASharedProfile()
        {
            ProfileStore original = new ProfileStore { CheckUpdatesAutomatically = false, DismissedUpdateVersion = "0.14.0" };
            ProfileStore restored = JsonConvert.DeserializeObject<ProfileStore>(JsonConvert.SerializeObject(original));
            Assert.False(restored.CheckUpdatesAutomatically);
            Assert.Equal("0.14.0", restored.DismissedUpdateVersion);
            Assert.True(JsonConvert.DeserializeObject<ProfileStore>("{}").CheckUpdatesAutomatically);
            Assert.Null(typeof(ShifterSettings).GetProperty(nameof(ProfileStore.CheckUpdatesAutomatically)));
            Assert.Null(typeof(ShifterSettings).GetProperty(nameof(ProfileStore.DismissedUpdateVersion)));
        }

        private static ReleaseInfo Parse(JObject json) { return ReleaseInfo.Parse(json.ToString(), "0.13.0"); }

        private static JObject Release()
        {
            return new JObject
            {
                ["tag_name"] = "v0.14.0",
                ["draft"] = false,
                ["prerelease"] = false,
                ["html_url"] = ReleaseInfo.RepositoryUrl + "/releases/tag/v0.14.0",
                ["body"] = "Release notes",
                ["assets"] = new JArray(new JObject
                {
                    ["name"] = "AB9ActiveShifter.dll",
                    ["state"] = "uploaded",
                    ["size"] = 300000,
                    ["digest"] = "sha256:" + new string('A', 64),
                    ["browser_download_url"] = ReleaseInfo.RepositoryUrl + "/releases/download/v0.14.0/AB9ActiveShifter.dll"
                })
            };
        }
    }
}
