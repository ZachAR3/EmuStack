using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Octokit;

namespace EmuStack.Scripts.Modes;

public abstract class Mode
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string DefaultExecutableName { get; }

    public virtual string LegacyName => Name;
    public virtual string DefaultAppDataDirectoryName => Id;
    public virtual string DefaultReleaseChannel => "stable";
    public virtual bool SupportsCustomVersions => true;

    public abstract Task<List<ProviderRelease>> GetAvailableReleases(
        GitHubClient gitHubClient,
        HttpClient httpClient,
        string os,
        string releaseChannel);

    public virtual async Task<ProviderRelease> GetLatestRelease(
        GitHubClient gitHubClient,
        HttpClient httpClient,
        string os,
        string releaseChannel)
    {
        return (await GetAvailableReleases(gitHubClient, httpClient, os, releaseChannel)).FirstOrDefault();
    }

    public virtual async Task<ProviderRelease> GetRelease(
        string version,
        GitHubClient gitHubClient,
        HttpClient httpClient,
        string os,
        string releaseChannel)
    {
        var releases = await GetAvailableReleases(gitHubClient, httpClient, os, releaseChannel);
        var release = releases.FirstOrDefault(candidate =>
            string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.DisplayName, version, StringComparison.OrdinalIgnoreCase));

        if (release != null)
        {
            return release;
        }

        return SupportsCustomVersions ? BuildCustomRelease(version, os, releaseChannel) : null;
    }

    public virtual ProviderRelease BuildCustomRelease(string version, string os, string releaseChannel)
    {
        return null;
    }

    public virtual bool IsSingleFileDownload(string os)
    {
        return false;
    }

    public virtual bool SupportsAutoUnpack(string os)
    {
        return !IsSingleFileDownload(os);
    }

    public virtual string GetDownloadFileName(ProviderRelease release, string os, string executableName)
    {
        if (!string.IsNullOrEmpty(release.FileName))
        {
            return release.FileName;
        }

        return $"{GetSafeFileName(executableName)}-{GetSafeFileName(release.Version)}{GetArchiveExtension(os)}";
    }

    public virtual string GetArchiveExtension(string os)
    {
        return os == "Windows" ? ".zip" : ".tar.gz";
    }

    public virtual string GetDefaultInstallDirectory(string os)
    {
        return Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "EmuStack",
            Id);
    }

    public virtual string GetDefaultAppDataPath(string os)
    {
        var basePath = os == "Linux"
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Path.Join(basePath, DefaultAppDataDirectoryName);
    }

    public virtual string GetDefaultModsLocation(string appDataPath)
    {
        return Path.Join(appDataPath, "load");
    }

    public virtual string GetDefaultSavesLocation(string appDataPath)
    {
        return Path.Join(appDataPath, "nand", "user", "save");
    }

    public virtual IEnumerable<string> GetExtractedDirectoriesToFlatten(ProviderRelease release, string os)
    {
        return Array.Empty<string>();
    }

    public virtual IEnumerable<string> GetExecutableCandidates(string os, string executableName)
    {
        if (os == "Windows")
        {
            return new[]
            {
                $"{executableName}.exe",
                $"{DefaultExecutableName}.exe",
                $"{Name}.exe"
            };
        }

        if (os == "Linux")
        {
            return new[]
            {
                executableName,
                DefaultExecutableName,
                Name,
                $"{executableName}.AppImage",
                $"{DefaultExecutableName}.AppImage",
                $"{Name}.AppImage"
            };
        }

        return new[]
        {
            $"{executableName}.app",
            $"{DefaultExecutableName}.app",
            $"{Name}.app"
        };
    }

    public virtual string FindExecutable(string installDirectory, string os, string executableName)
    {
        if (!Directory.Exists(installDirectory))
        {
            return "";
        }

        var candidates = GetExecutableCandidates(os, executableName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var candidate in candidates)
        {
            var directPath = Path.Join(installDirectory, candidate);
            if (File.Exists(directPath) || Directory.Exists(directPath))
            {
                return directPath;
            }
        }

        foreach (var file in Directory.GetFiles(installDirectory, "*", SearchOption.AllDirectories))
        {
            if (candidates.Any(candidate => string.Equals(Path.GetFileName(file), candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return file;
            }
        }

        return "";
    }

    protected static string GetSafeFileName(string value)
    {
        var safeName = value;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '-');
        }

        return safeName;
    }

    protected static string NormalizeVersion(string version)
    {
        return version?.Trim().TrimStart('v') ?? "";
    }

    protected static string GetRyubingOsToken(string os)
    {
        return os switch
        {
            "Windows" => "windows",
            "Linux" => "linux",
            _ => "macos"
        };
    }

    protected static string GetArchitectureToken()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };
    }

    protected static List<ProviderRelease> BuildGitHubReleaseList(
        IReadOnlyList<Release> releases,
        Func<Release, ReleaseAsset> selectAsset)
    {
        var providerReleases = new List<ProviderRelease>();

        foreach (var release in releases)
        {
            var asset = selectAsset(release);
            if (asset == null)
            {
                continue;
            }

            providerReleases.Add(new ProviderRelease
            {
                Version = NormalizeVersion(release.TagName),
                DisplayName = string.IsNullOrEmpty(release.Name) ? release.TagName : release.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                FileName = asset.Name,
                IsPrerelease = release.Prerelease
            });
        }

        return providerReleases;
    }

    protected static ReleaseAsset SelectBestGitHubAsset(Release release, string os, params string[] preferredTerms)
    {
        var assets = release.Assets
            .Where(asset => IsDownloadableAsset(asset.Name))
            .Select(asset => new
            {
                Asset = asset,
                Score = ScoreAsset(asset.Name, os, preferredTerms)
            })
            .Where(asset => asset.Score > 0)
            .OrderByDescending(asset => asset.Score)
            .FirstOrDefault();

        return assets?.Asset;
    }

    private static bool IsDownloadableAsset(string assetName)
    {
        var name = assetName.ToLowerInvariant();
        return !name.EndsWith(".sha256") &&
               !name.EndsWith(".sha256sum") &&
               !name.EndsWith(".sig") &&
               !name.EndsWith(".json") &&
               !name.Contains("source") &&
               (name.EndsWith(".zip") ||
                name.EndsWith(".7z") ||
                name.EndsWith(".tar.gz") ||
                name.EndsWith(".appimage") ||
                name.EndsWith(".dmg"));
    }

    private static int ScoreAsset(string assetName, string os, IEnumerable<string> preferredTerms)
    {
        var name = assetName.ToLowerInvariant();
        var score = 0;

        foreach (var term in preferredTerms)
        {
            if (!string.IsNullOrEmpty(term) && name.Contains(term.ToLowerInvariant()))
            {
                score += 10;
            }
        }

        if (os == "Windows" && (name.Contains("windows") || Regex.IsMatch(name, @"(^|[-_.])win")))
        {
            score += 30;
        }
        else if (os == "Linux" && (name.Contains("linux") || name.EndsWith(".appimage")))
        {
            score += 30;
        }
        else if (os == "macOS" && (name.Contains("macos") || name.Contains("darwin") || name.Contains("osx")))
        {
            score += 30;
        }

        if (name.EndsWith(".appimage") && os == "Linux")
        {
            score += 8;
        }
        else if (name.EndsWith(".zip") && os == "Windows")
        {
            score += 5;
        }
        else if (name.EndsWith(".tar.gz") && os != "Windows")
        {
            score += 4;
        }

        return score;
    }
}
