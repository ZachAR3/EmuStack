using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Octokit;

namespace EmuStack.Scripts.Modes;

public class ModeYuzu : Mode
{
    private const string RepoOwner = "pineappleEA";
    private const string RepoName = "pineapple-src";

    public override string Id => "yuzu";
    public override string Name => "Yuzu";
    public override string DefaultExecutableName => "yuzu";
    public override string DefaultAppDataDirectoryName => "yuzu";

    public override async Task<List<ProviderRelease>> GetAvailableReleases(
        GitHubClient gitHubClient,
        HttpClient httpClient,
        string os,
        string releaseChannel)
    {
        var releases = await gitHubClient.Repository.Release.GetAll(RepoOwner, RepoName);
        return releases
            .Select(release => BuildCustomRelease(NormalizeVersion(release.TagName).Split("-").Last(), os, releaseChannel))
            .Where(release => release != null)
            .ToList();
    }

    public override ProviderRelease BuildCustomRelease(string version, string os, string releaseChannel)
    {
        var normalizedVersion = NormalizeVersion(version).Split("-").Last();
        var extension = GetArchiveExtension(os);
        var fileName = $"{os}-Yuzu-EA-{normalizedVersion}{extension}";

        return new ProviderRelease
        {
            Version = normalizedVersion,
            DisplayName = normalizedVersion,
            DownloadUrl = $"https://github.com/pineappleEA/pineapple-src/releases/download/EA-{normalizedVersion}/{fileName}",
            FileName = fileName
        };
    }

    public override bool IsSingleFileDownload(string os)
    {
        return os == "Linux";
    }

    public override string GetArchiveExtension(string os)
    {
        return os switch
        {
            "Windows" => ".zip",
            "Linux" => ".AppImage",
            _ => ".tar.gz"
        };
    }

    public override IEnumerable<string> GetExtractedDirectoriesToFlatten(ProviderRelease release, string os)
    {
        if (os == "Windows")
        {
            return new[] { "yuzu-windows-msvc-early-access" };
        }

        return base.GetExtractedDirectoriesToFlatten(release, os);
    }

    public override IEnumerable<string> GetExecutableCandidates(string os, string executableName)
    {
        if (os == "Windows")
        {
            return new[] { $"{executableName}.exe", "yuzu.exe" };
        }

        return base.GetExecutableCandidates(os, executableName);
    }
}
