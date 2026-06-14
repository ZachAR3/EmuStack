using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Octokit;

namespace EmuStack.Scripts.Modes;

public class ModeEden : Mode
{
    private const string ReleasePageUrl = "https://git.eden-emu.dev/eden-emu/eden/releases";

    public override string Id => "eden";
    public override string Name => "Eden";
    public override string DefaultExecutableName => "Eden";
    public override string DefaultAppDataDirectoryName => "eden";
    public override bool SupportsCustomVersions => false;

    public override async Task<List<ProviderRelease>> GetAvailableReleases(
        GitHubClient gitHubClient,
        HttpClient httpClient,
        string os,
        string releaseChannel)
    {
        var releasePage = await httpClient.GetStringAsync(ReleasePageUrl);
        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(releasePage);

        var assetLinks = htmlDocument.DocumentNode
            .SelectNodes("//a[@href]")
            ?.Select(link => new EdenAssetLink
            {
                Url = link.GetAttributeValue("href", ""),
                Text = HtmlEntity.DeEntitize(link.InnerText).Trim()
            })
            .Where(link => link.Url.Contains("stable.eden-emu.dev") && IsInstallableAsset(link.FileName))
            .ToList() ?? new List<EdenAssetLink>();

        return assetLinks
            .Select(link => new { Link = link, Version = ExtractVersion(link.FileName) })
            .Where(link => !string.IsNullOrEmpty(link.Version))
            .GroupBy(link => link.Version)
            .Select(group => SelectBestAsset(group.Select(link => link.Link), group.Key, os))
            .Where(release => release != null)
            .ToList();
    }

    public override ProviderRelease BuildCustomRelease(string version, string os, string releaseChannel)
    {
        return null;
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

    public override IEnumerable<string> GetExecutableCandidates(string os, string executableName)
    {
        if (os == "Windows")
        {
            return new[]
            {
                $"{executableName}.exe",
                "Eden.exe",
                "eden.exe"
            };
        }

        if (os == "Linux")
        {
            return new[]
            {
                executableName,
                "Eden",
                "eden",
                $"{executableName}.AppImage",
                "Eden.AppImage",
                "eden.AppImage"
            };
        }

        return new[]
        {
            $"{executableName}.app",
            "Eden.app",
            "eden.app"
        };
    }

    private static bool IsInstallableAsset(string fileName)
    {
        var lowerName = fileName.ToLowerInvariant();
        return lowerName.StartsWith("eden-") &&
               !lowerName.EndsWith(".zsync") &&
               !lowerName.EndsWith(".torrent") &&
               !lowerName.EndsWith(".apk") &&
               !lowerName.Contains("room") &&
               (lowerName.EndsWith(".appimage") || lowerName.EndsWith(".zip") || lowerName.EndsWith(".dmg"));
    }

    private static string ExtractVersion(string fileName)
    {
        var match = Regex.Match(fileName, @"v\d+(?:\.\d+)+(?:[-._a-zA-Z0-9]*)?");
        return match.Success ? match.Value.TrimEnd('.') : "";
    }

    private static ProviderRelease SelectBestAsset(IEnumerable<EdenAssetLink> links, string version, string os)
    {
        var selectedLink = links
            .Select(link => new
            {
                Link = link,
                Score = ScoreAsset(link.FileName, os)
            })
            .Where(link => link.Score > 0)
            .OrderByDescending(link => link.Score)
            .FirstOrDefault();

        if (selectedLink == null)
        {
            return null;
        }

        return new ProviderRelease
        {
            Version = NormalizeVersion(version),
            DisplayName = NormalizeVersion(version),
            DownloadUrl = selectedLink.Link.Url,
            FileName = selectedLink.Link.FileName
        };
    }

    private static int ScoreAsset(string fileName, string os)
    {
        var name = fileName.ToLowerInvariant();
        var score = 0;

        if (os == "Windows" && name.Contains("windows") && name.EndsWith(".zip"))
        {
            score += 40;
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                score += name.Contains("arm64") ? 30 : -20;
            }
            else
            {
                score += name.Contains("amd64") ? 30 : -20;
                score += name.Contains("msvc-standard") ? 20 : 0;
                score += name.Contains("clang-pgo") ? 8 : 0;
            }
        }
        else if (os == "Linux" && name.Contains("linux") && name.EndsWith(".appimage"))
        {
            score += 40;
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                score += name.Contains("aarch64") ? 30 : -20;
            }
            else
            {
                score += name.Contains("amd64") ? 30 : -20;
                score += name.Contains("clang-pgo") ? 20 : 0;
                score += name.Contains("gcc-standard") ? 10 : 0;
            }
        }
        else if (os == "macOS" && name.Contains("macos") && name.EndsWith(".dmg"))
        {
            score += 40;
        }

        return score;
    }

    private class EdenAssetLink
    {
        public string Url { get; init; } = "";
        public string Text { get; init; } = "";

        public string FileName
        {
            get
            {
                if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
                {
                    return Path.GetFileName(uri.LocalPath);
                }

                return Path.GetFileName(Url);
            }
        }
    }
}
