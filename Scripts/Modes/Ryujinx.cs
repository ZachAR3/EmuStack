using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;


public class ModeRyubing : Mode
{
	private const string UpdateServerBaseUrl = "https://update.ryujinx.app";
	private const string ForgejoApiBaseUrl = "https://git.ryujinx.app/api/v1/repos/projects/Ryubing";
	private const string ForgejoDownloadBaseUrl = "https://git.ryujinx.app/projects/Ryubing/releases/download";

	// Anubis anti-bot protection on git.ryujinx.app whitelists git clients, so the
	// Forgejo API requires a git/ User-Agent. Direct asset download URLs are not
	// protected and work with any User-Agent.
	private const string ForgejoUserAgent = "git/2.39.0";
	private const int ForgejoApiTimeoutSeconds = 15;

	public override string Id => "ryubing";
	public override string Name => "Ryubing";
	public override string LegacyName => "Ryujinx";
	public override string DefaultExecutableName => "Ryubing";
	public override string DefaultAppDataDirectoryName => "Ryujinx";

	public override IEnumerable<string> LegacyExecutableNames => new[] { "Ryujinx", "Ryujinx.Ava" };

	// Linux ships as a self-contained AppImage — no unpacking needed.
	public override bool IsSingleFileDownload(OsKind os) => os == OsKind.Linux;

	public override string GetArchiveExtension(OsKind os) => os switch
	{
		OsKind.Windows => ".zip",
		OsKind.Linux => ".AppImage",
		_ => ".tar.gz",
	};


	public override async Task<List<ProviderRelease>> GetAvailableReleases(
		HttpClient httpClient,
		OsKind os,
		string releaseChannel)
	{
		// Prefer the update server: it's the official lightweight path and offloads
		// the Forgejo frontend. It returns only the latest release, but with a
		// direct download_url so no Forgejo asset lookup is needed.
		var latest = await TryGetLatestFromUpdateServer(httpClient, os, releaseChannel);
		if (latest != null)
		{
			return new List<ProviderRelease> { latest };
		}

		// Update server unreachable — silently fall back to the Forgejo API.
		return await FetchAllForgejoReleases(httpClient, os);
	}

	public override async Task<ProviderRelease> GetLatestRelease(
		HttpClient httpClient,
		OsKind os,
		string releaseChannel)
	{
		return await TryGetLatestFromUpdateServer(httpClient, os, releaseChannel)
		       ?? (await FetchAllForgejoReleases(httpClient, os)).FirstOrDefault();
	}

	public override ProviderRelease BuildCustomRelease(string version, OsKind os, string releaseChannel)
	{
		var normalizedVersion = NormalizeVersion(version);
		return BuildReleaseFromConvention(normalizedVersion, os);
	}

	public override IEnumerable<string> GetExtractedDirectoriesToFlatten(ProviderRelease release, OsKind os)
		=> new[] { "publish" };


	/// <summary>
	/// Asks the update server for the latest release. If the server responds,
	/// uses its direct download_url without touching Forgejo. Falls back to
	/// Forgejo asset resolution only when the update server omits a download URL
	/// or is unreachable.
	/// </summary>
	private static async Task<ProviderRelease> TryGetLatestFromUpdateServer(
		HttpClient httpClient,
		OsKind os,
		string releaseChannel)
	{
		var channel = NormalizeChannel(releaseChannel);
		var osToken = GetOsToken(os);
		var archToken = GetArchToken();

		var url = $"{UpdateServerBaseUrl}/latest/query?os={osToken}&arch={archToken}&rc={channel}";
		var response = await TryGetUpdateServerResponse(httpClient, url);
		if (response == null || string.IsNullOrEmpty(response.Tag))
		{
			return null;
		}

		var version = NormalizeVersion(response.Tag);

		// Use the direct download URL when provided — no Forgejo asset lookup needed.
		if (!string.IsNullOrEmpty(response.DownloadUrl))
		{
			return new ProviderRelease
			{
				Version = version,
				DisplayName = version,
				DownloadUrl = response.DownloadUrl,
				FileName = GetFileNameFromUrl(response.DownloadUrl),
			};
		}

		// Update server returned a tag but no download URL — resolve via Forgejo as a
		// silent fallback, then convention-based construction as a last resort.
		return await FetchForgejoRelease(httpClient, os, response.Tag)
		       ?? BuildReleaseFromConvention(version, os);
	}


	private static async Task<UpdateServerResponse> TryGetUpdateServerResponse(HttpClient httpClient, string url)
	{
		try
		{
			var response = await httpClient.GetAsync(url);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			var json = await response.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<UpdateServerResponse>(json, JsonOptions.Default);
		}
		catch (HttpRequestException)
		{
			return null;
		}
	}


	private static string GetFileNameFromUrl(string url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out var uri)
			? Path.GetFileName(uri.LocalPath)
			: Path.GetFileName(url);
	}


	private static async Task<List<ProviderRelease>> FetchAllForgejoReleases(HttpClient httpClient, OsKind os)
	{
		var url = $"{ForgejoApiBaseUrl}/releases?limit=30&pre_release=false";
		var forgejoReleases = await TryGetForgejoJson<List<ForgejoRelease>>(httpClient, url);
		if (forgejoReleases == null || forgejoReleases.Count == 0)
		{
			return new List<ProviderRelease>();
		}

		var releases = new List<ProviderRelease>();
		foreach (var forgejoRelease in forgejoReleases.Where(r => !r.Prerelease && !string.IsNullOrEmpty(r.TagName)))
		{
			var release = SelectAsset(forgejoRelease, os);
			if (release != null)
			{
				releases.Add(release);
			}
		}

		// Sort by version descending (latest first)
		return releases
			.OrderByDescending(r => GetSortableVersion(r.Version))
			.ToList();
	}

	private static async Task<ProviderRelease> FetchForgejoRelease(HttpClient httpClient, OsKind os, string tag)
	{
		var url = $"{ForgejoApiBaseUrl}/releases/tags/{Uri.EscapeDataString(tag)}";
		var forgejoRelease = await TryGetForgejoJson<ForgejoRelease>(httpClient, url);
		if (forgejoRelease == null)
		{
			return null;
		}

		return SelectAsset(forgejoRelease, os);
	}

	private static async Task<T> TryGetForgejoJson<T>(HttpClient httpClient, string url)
	{
		try
		{
			using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(ForgejoApiTimeoutSeconds));
			var request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.UserAgent.ParseAdd(ForgejoUserAgent);
			var response = await httpClient.SendAsync(request, cts.Token);
			if (!response.IsSuccessStatusCode)
			{
				return default;
			}

			var json = await response.Content.ReadAsStringAsync(cts.Token);
			return JsonSerializer.Deserialize<T>(json, JsonOptions.Default);
		}
		catch (Exception)
		{
			return default;
		}
	}

	private static ProviderRelease SelectAsset(ForgejoRelease forgejoRelease, OsKind os)
	{
		if (forgejoRelease.Assets == null || forgejoRelease.Assets.Count == 0)
		{
			return null;
		}

		var isArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
		var version = NormalizeVersion(forgejoRelease.TagName);

		ForgejoAsset bestAsset = null;
		var bestScore = 0;

		foreach (var asset in forgejoRelease.Assets)
		{
			var score = ScoreAsset(asset.Name, os, isArm);
			if (score > bestScore)
			{
				bestScore = score;
				bestAsset = asset;
			}
		}

		if (bestAsset == null)
		{
			return null;
		}

		return new ProviderRelease
		{
			Version = version,
			DisplayName = version,
			DownloadUrl = bestAsset.BrowserDownloadUrl,
			FileName = bestAsset.Name,
		};
	}

	private static int ScoreAsset(string assetName, OsKind os, bool isArm)
	{
		var name = assetName.ToLowerInvariant();

		// Skip auxiliary files
		if (name.EndsWith(".zsync") || name.EndsWith(".torrent"))
		{
			return 0;
		}

		switch (os)
		{
			case OsKind.Windows:
				if (name.Contains("win") && name.EndsWith(".zip"))
				{
					var score = 40;
					score += name.Contains("x64") ? 30 : 0;
					return score;
				}
				return 0;

			case OsKind.Linux:
				if (name.EndsWith(".appimage"))
				{
					var score = 50; // Prefer AppImage — single file, no unpacking needed
					score += isArm
						? (name.Contains("arm64") ? 30 : -20)
						: (name.Contains("x64") && !name.Contains("arm64") ? 30 : -20);
					return score;
				}

				if (name.Contains("linux") && name.EndsWith(".tar.gz"))
				{
					var score = 40;
					score += isArm
						? (name.Contains("arm64") ? 30 : -20)
						: (name.Contains("x64") ? 30 : -20);
					return score;
				}
				return 0;

			case OsKind.MacOS:
				if (name.Contains("macos") && name.EndsWith(".tar.gz"))
				{
					return 40; // Universal binary — works on both ARM64 and x64
				}
				return 0;

			default:
				return 0;
		}
	}

	private static ProviderRelease BuildReleaseFromConvention(string version, OsKind os)
	{
		var assetName = GetConventionAssetName(version, os);
		var downloadUrl = $"{ForgejoDownloadBaseUrl}/{version}/{assetName}";

		return new ProviderRelease
		{
			Version = version,
			DisplayName = version,
			DownloadUrl = downloadUrl,
			FileName = assetName,
		};
	}

	private static string GetConventionAssetName(string version, OsKind os)
	{
		var isArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
		return os switch
		{
			OsKind.Windows => $"ryujinx-{version}-win_x64.zip",
			OsKind.Linux => isArm
				? $"ryujinx-{version}-arm64.AppImage"
				: $"ryujinx-{version}-x64.AppImage",
			_ => $"ryujinx-{version}-macos_universal.app.tar.gz",
		};
	}

	private static Version GetSortableVersion(string version)
	{
		var versionParts = NormalizeVersion(version).Split('.').ToList();
		while (versionParts.Count < 3)
		{
			versionParts.Add("0");
		}

		return Version.TryParse(string.Join(".", versionParts.Take(3)), out var parsedVersion)
			? parsedVersion
			: new Version(0, 0, 0);
	}

	private static string NormalizeChannel(string releaseChannel)
		=> string.IsNullOrEmpty(releaseChannel) ? "stable" : releaseChannel.ToLowerInvariant();

	private static string GetOsToken(OsKind os) => os switch
	{
		OsKind.Windows => "windows",
		OsKind.Linux => "linux",
		_ => "macos",
	};

	private static string GetArchToken() => RuntimeInformation.ProcessArchitecture switch
	{
		Architecture.Arm64 => "arm64",
		Architecture.X86 => "x86",
		_ => "x64",
	};


	private static class JsonOptions
	{
		public static readonly JsonSerializerOptions Default = new()
		{
			PropertyNameCaseInsensitive = true,
		};
	}


	private class UpdateServerResponse
	{
		[JsonPropertyName("tag")]
		public string Tag { get; set; }

		[JsonPropertyName("download_url")]
		public string DownloadUrl { get; set; }
	}

	private class ForgejoRelease
	{
		[JsonPropertyName("tag_name")]
		public string TagName { get; set; }

		[JsonPropertyName("prerelease")]
		public bool Prerelease { get; set; }

		[JsonPropertyName("assets")]
		public List<ForgejoAsset> Assets { get; set; }
	}

	private class ForgejoAsset
	{
		[JsonPropertyName("name")]
		public string Name { get; set; }

		[JsonPropertyName("browser_download_url")]
		public string BrowserDownloadUrl { get; set; }
	}
}
