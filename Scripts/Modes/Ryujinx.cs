using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Octokit;

namespace EmuStack.Scripts.Modes;

public class ModeRyubing : Mode
{
    private const string UpdateServerBaseUrl = "https://ryujinx.app";

    public override string Id => "ryubing";
    public override string Name => "Ryubing";
    public override string LegacyName => "Ryujinx";
    public override string DefaultExecutableName => "Ryubing";
    public override string DefaultAppDataDirectoryName => "Ryujinx";

    public override async Task<List<ProviderRelease>> GetAvailableReleases(
        GitHubClient gitHubClient,
        HttpClient httpClient,
        string os,
        string releaseChannel)
    {
        var latestRelease = await GetLatestRelease(gitHubClient, httpClient, os, releaseChannel);
        return latestRelease == null ? new List<ProviderRelease>() : new List<ProviderRelease> { latestRelease };
    }

    public override async Task<ProviderRelease> GetLatestRelease(
        GitHubClient gitHubClient,
        HttpClient httpClient,
        string os,
        string releaseChannel)
    {
        var response = await httpClient.GetAsync(GetLatestQueryUrl(os, releaseChannel));
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var latestResponse = JsonSerializer.Deserialize<RyubingVersionResponse>(
            responseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (latestResponse == null || string.IsNullOrEmpty(latestResponse.Version))
        {
            return null;
        }

        var downloadUrl = string.IsNullOrEmpty(latestResponse.ArtifactUrl)
            ? GetDownloadQueryUrl(os, releaseChannel, latestResponse.Version)
            : latestResponse.ArtifactUrl;

        return new ProviderRelease
        {
            Version = NormalizeVersion(latestResponse.Version),
            DisplayName = NormalizeVersion(latestResponse.Version),
            DownloadUrl = downloadUrl,
            FileName = $"ryubing-{NormalizeVersion(latestResponse.Version)}-{GetRyubingOsToken(os)}-{GetArchitectureToken()}{GetArchiveExtension(os)}"
        };
    }

    public override ProviderRelease BuildCustomRelease(string version, string os, string releaseChannel)
    {
        var normalizedVersion = NormalizeVersion(version);
        return new ProviderRelease
        {
            Version = normalizedVersion,
            DisplayName = normalizedVersion,
            DownloadUrl = GetDownloadQueryUrl(os, releaseChannel, normalizedVersion),
            FileName = $"ryubing-{normalizedVersion}-{GetRyubingOsToken(os)}-{GetArchitectureToken()}{GetArchiveExtension(os)}"
        };
    }

    public override IEnumerable<string> GetExtractedDirectoriesToFlatten(ProviderRelease release, string os)
    {
        return new[] { "publish" };
    }

    public override IEnumerable<string> GetExecutableCandidates(string os, string executableName)
    {
        if (os == "Windows")
        {
            return new[]
            {
                $"{executableName}.exe",
                "Ryubing.exe",
                "Ryujinx.exe"
            };
        }

        if (os == "Linux")
        {
            return new[]
            {
                executableName,
                "Ryubing",
                "Ryujinx",
                "Ryujinx.Ava"
            };
        }

        return new[]
        {
            $"{executableName}.app",
            "Ryubing.app",
            "Ryujinx.app"
        };
    }

    private static string GetLatestQueryUrl(string os, string releaseChannel)
    {
        return $"{UpdateServerBaseUrl}/latest/query?os={GetRyubingOsToken(os)}&arch={GetArchitectureToken()}&rc={GetReleaseChannel(releaseChannel)}";
    }

    private static string GetDownloadQueryUrl(string os, string releaseChannel, string version)
    {
        return $"{UpdateServerBaseUrl}/download/query?os={GetRyubingOsToken(os)}&arch={GetArchitectureToken()}&rc={GetReleaseChannel(releaseChannel)}&version={Uri.EscapeDataString(version)}";
    }

    private static string GetReleaseChannel(string releaseChannel)
    {
        return string.IsNullOrEmpty(releaseChannel) ? "stable" : releaseChannel.ToLowerInvariant();
    }

    private class RyubingVersionResponse
    {
        public string Version { get; set; }
        public string ArtifactUrl { get; set; }
    }
}

public class ModeRyujinx : ModeRyubing
{
}
