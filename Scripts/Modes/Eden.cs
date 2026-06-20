using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;


public class ModeEden : Mode
{
	private const string ReleasePageUrl = "https://git.eden-emu.dev/eden-emu/eden/releases";

	// Asset-scoring weights. Higher = stronger preference. Tuned so that platform
	// match dominates and architecture/variant tweaks act as tie-breakers.
	private const int PlatformAndExtensionMatch = 40;
	private const int ArchitectureMatch = 30;
	private const int ArchitectureMismatchPenalty = -20;
	private const int PreferredVariantBonus = 20;
	private const int SecondaryVariantBonus = 10;
	private const int TertiaryVariantBonus = 8;

	// Forgejo paginates releases at 10 per page. Eden currently has 14 releases
	// across 2 pages; cap at 5 pages to future-proof without unbounded fetching.
	private const int MaxReleasePages = 5;

	public override string Id => "eden";
	public override string Name => "Eden";
	public override string DefaultExecutableName => "Eden";
	public override string DefaultAppDataDirectoryName => "eden";
	public override bool SupportsCustomVersions => false;

	public override IEnumerable<string> LegacyExecutableNames => new[] { "eden" };
	protected override IEnumerable<string> LinuxExecutableSuffixes => new[] { "", ".AppImage" };


	public override async Task<List<ProviderRelease>> GetAvailableReleases(
		HttpClient httpClient,
		OsKind os,
		string releaseChannel)
	{
		var assetLinks = new List<EdenAssetLink>();

		// Fetch all release pages — Forgejo paginates at 10 releases per page.
		for (var page = 1; page <= MaxReleasePages; page++)
		{
			var pageUrl = page == 1 ? ReleasePageUrl : $"{ReleasePageUrl}?page={page}";
			var response = await httpClient.GetAsync(pageUrl);
			if (!response.IsSuccessStatusCode)
			{
				break;
			}

			var pageHtml = await response.Content.ReadAsStringAsync();
			var linksOnPage = ParseAssetLinks(pageHtml);
			if (linksOnPage.Count == 0)
			{
				break;
			}

			assetLinks.AddRange(linksOnPage);
		}

		if (assetLinks.Count == 0)
		{
			throw new InvalidOperationException("Eden official release page returned no downloadable assets.");
		}

		var releases = assetLinks
			.Select(link => new { Link = link, Version = ExtractVersion(link.FileName) })
			.Where(link => !string.IsNullOrEmpty(link.Version))
			.GroupBy(link => link.Version)
			.Select(group => SelectBestAsset(group.Select(link => link.Link), group.Key, os))
			.Where(release => release != null)
			.ToList();

		if (releases.Count == 0)
		{
			throw new InvalidOperationException($"No Eden official release assets matched this platform ({os}).");
		}

		return releases
			.OrderByDescending(release => GetSortableVersion(release.Version))
			.ToList();
	}

	public override bool IsSingleFileDownload(OsKind os) => os == OsKind.Linux;

	public override bool SupportsAutoUnpack(OsKind os) => os != OsKind.MacOS && base.SupportsAutoUnpack(os);

	public override string GetArchiveExtension(OsKind os) => os switch
	{
		OsKind.Windows => ".zip",
		OsKind.Linux => ".AppImage",
		_ => ".dmg",
	};


	private static List<EdenAssetLink> ParseAssetLinks(string html)
	{
		var htmlDocument = new HtmlDocument();
		htmlDocument.LoadHtml(html);

		return htmlDocument.DocumentNode
			.SelectNodes("//a[@href]")
			?.Select(link => new EdenAssetLink
			{
				Url = link.GetAttributeValue("href", ""),
				Text = HtmlEntity.DeEntitize(link.InnerText).Trim()
			})
			.Where(link => link.Url.Contains("stable.eden-emu.dev") && IsInstallableAsset(link.FileName))
			.ToList() ?? new List<EdenAssetLink>();
	}


	private static bool IsInstallableAsset(string fileName)
	{
		var lowerName = fileName.ToLowerInvariant();
		return lowerName.StartsWith("eden-") &&
		       !lowerName.EndsWith(".zsync") &&
		       !lowerName.EndsWith(".torrent") &&
		       !lowerName.EndsWith(".apk") &&
		       !lowerName.Contains("room") &&
		       (lowerName.EndsWith(".appimage") ||
		        lowerName.EndsWith(".zip") ||
		        lowerName.EndsWith(".dmg") ||
		        lowerName.EndsWith(".tar.gz") ||
		        lowerName.EndsWith(".tar.xz"));
	}

	// Captures the full version string including pre-release suffixes (e.g. v0.2.0-rc2),
	// so release candidates are listed as distinct versions instead of being silently
	// merged with their stable counterpart.
	private static string ExtractVersion(string fileName)
	{
		var match = Regex.Match(fileName, @"v\d+\.\d+\.\d+(?:-rc\d+)?");
		return match.Success ? match.Value : "";
	}

	private static Version GetSortableVersion(string version)
	{
		// Strip pre-release suffix for sortable Version parsing — release candidates
		// sort below their stable counterpart by treating the suffix as a negative offset.
		var baseVersion = NormalizeVersion(version);
		var preReleaseOffset = 0;
		var rcMatch = Regex.Match(version, @"-rc(\d+)$", RegexOptions.IgnoreCase);
		if (rcMatch.Success)
		{
			baseVersion = NormalizeVersion(version[..rcMatch.Index]);
			// RC N sorts before stable by subtracting a fraction; -rc1 → -0.9, -rc2 → -0.8, etc.
			preReleaseOffset = -100 + int.Parse(rcMatch.Groups[1].Value);
		}

		var versionParts = baseVersion.Split('.').ToList();
		while (versionParts.Count < 3)
		{
			versionParts.Add("0");
		}

		if (!int.TryParse(versionParts[0], out var major) ||
		    !int.TryParse(versionParts[1], out var minor) ||
		    !int.TryParse(versionParts[2], out var build))
		{
			return new Version(0, 0, 0);
		}

		// Use a 4-part Version where the 4th component encodes the pre-release offset
		// so that v0.2.0-rc2 < v0.2.0-rc1... wait, rc2 > rc1. RC N is newer (closer to stable).
		// So -rc1 should sort lower than -rc2 which sorts lower than stable.
		// Stable has no suffix → revision = 0. RC N → revision = -100 + N (e.g. rc1 = -99, rc2 = -98).
		// But Version doesn't support negative components. Instead, use:
		// Stable → revision = 100. RC N → revision = N (so rc1=1, rc2=2, ..., stable=100).
		var revision = rcMatch.Success ? int.Parse(rcMatch.Groups[1].Value) : 100;
		return new Version(major, minor, build, revision);
	}

	private static ProviderRelease SelectBestAsset(IEnumerable<EdenAssetLink> links, string version, OsKind os)
	{
		var selectedLink = links
			.Select(link => new { Link = link, Score = ScoreAsset(link.FileName, os) })
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
			FileName = selectedLink.Link.FileName,
		};
	}

	private static int ScoreAsset(string fileName, OsKind os)
	{
		var name = fileName.ToLowerInvariant();
		var isArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

		switch (os)
		{
			case OsKind.Windows when name.Contains("windows") && name.EndsWith(".zip"):
			{
				var score = PlatformAndExtensionMatch;
				score += isArm
					? (name.Contains("arm64") ? ArchitectureMatch : ArchitectureMismatchPenalty)
					: (name.Contains("amd64") ? ArchitectureMatch : ArchitectureMismatchPenalty);
				if (!isArm)
				{
					score += name.Contains("msvc-standard") ? PreferredVariantBonus : 0;
					score += name.Contains("clang-pgo") ? TertiaryVariantBonus : 0;
				}
				return score;
			}

			case OsKind.Linux when name.Contains("linux") && name.EndsWith(".appimage"):
			{
				var score = PlatformAndExtensionMatch;
				score += isArm
					? (name.Contains("aarch64") ? ArchitectureMatch : ArchitectureMismatchPenalty)
					: (name.Contains("amd64") ? ArchitectureMatch : ArchitectureMismatchPenalty);
				if (!isArm)
				{
					score += name.Contains("clang-pgo") ? PreferredVariantBonus : 0;
					score += name.Contains("gcc-standard") ? SecondaryVariantBonus : 0;
				}
				return score;
			}

			case OsKind.MacOS when name.Contains("macos"):
			{
				// Newer Eden releases ship as .dmg; older ones used .tar.gz or .tar.xz.
				// Prefer .dmg when available (simpler to handle), fall back to tarballs.
				if (name.EndsWith(".dmg"))
				{
					return PlatformAndExtensionMatch;
				}

				if (name.EndsWith(".tar.gz") || name.EndsWith(".tar.xz"))
				{
					return PlatformAndExtensionMatch - 5;
				}

				return 0;
			}

			default:
				return 0;
		}
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
