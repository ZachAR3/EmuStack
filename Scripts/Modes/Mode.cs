using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;


public abstract class Mode
{
	public abstract string Id { get; }
	public abstract string Name { get; }
	public abstract string DefaultExecutableName { get; }

	public virtual string LegacyName => Name;
	public virtual string DefaultAppDataDirectoryName => Id;
	public virtual string DefaultReleaseChannel => "stable";
	public virtual bool SupportsCustomVersions => true;

	public virtual IEnumerable<string> LegacyExecutableNames => Array.Empty<string>();
	protected virtual IEnumerable<string> LinuxExecutableSuffixes => new[] { "" };

	public abstract Task<List<ProviderRelease>> GetAvailableReleases(
		HttpClient httpClient,
		OsKind os,
		string releaseChannel);

	public virtual async Task<ProviderRelease> GetLatestRelease(
		HttpClient httpClient,
		OsKind os,
		string releaseChannel)
	{
		return (await GetAvailableReleases(httpClient, os, releaseChannel)).FirstOrDefault();
	}

	public virtual async Task<ProviderRelease> GetRelease(
		string version,
		HttpClient httpClient,
		OsKind os,
		string releaseChannel)
	{
		var releases = await GetAvailableReleases(httpClient, os, releaseChannel);
		var release = releases.FirstOrDefault(candidate =>
			string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(candidate.DisplayName, version, StringComparison.OrdinalIgnoreCase));

		if (release != null)
		{
			return release;
		}

		return SupportsCustomVersions ? BuildCustomRelease(version, os, releaseChannel) : null;
	}

	public virtual ProviderRelease BuildCustomRelease(string version, OsKind os, string releaseChannel)
	{
		return null;
	}

	public virtual bool IsSingleFileDownload(OsKind os) => false;

	public virtual bool SupportsAutoUnpack(OsKind os) => !IsSingleFileDownload(os);

	public virtual string GetDownloadFileName(ProviderRelease release, OsKind os, string executableName)
	{
		if (!string.IsNullOrEmpty(release.FileName))
		{
			return release.FileName;
		}

		return $"{GetSafeFileName(executableName)}-{GetSafeFileName(release.Version)}{GetArchiveExtension(os)}";
	}

	public virtual string GetArchiveExtension(OsKind os) => os == OsKind.Windows ? ".zip" : ".tar.gz";

	public virtual string GetDefaultInstallDirectory(OsKind os)
	{
		return Path.Join(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"EmuStack",
			Id);
	}

	public virtual string GetDefaultAppDataPath(OsKind os)
	{
		var basePath = os == OsKind.Linux
			? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
			: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

		return Path.Join(basePath, DefaultAppDataDirectoryName);
	}

	public virtual string DesktopEntryComment => "Nintendo Switch video game console emulator";
	public virtual string DesktopEntryCategories => "Game;Emulator;Qt;";

	public virtual string GetDefaultModsLocation(string appDataPath) => Path.Join(appDataPath, "load");

	public virtual string GetDefaultSavesLocation(string appDataPath) => Path.Join(appDataPath, "nand", "user", "save");

	public virtual IEnumerable<string> GetExtractedDirectoriesToFlatten(ProviderRelease release, OsKind os)
		=> Array.Empty<string>();


	public virtual IEnumerable<string> GetExecutableCandidates(OsKind os, string executableName)
	{
		var names = new[] { executableName, DefaultExecutableName, Name }
			.Concat(LegacyExecutableNames)
			.Where(name => !string.IsNullOrEmpty(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		return os switch
		{
			OsKind.Windows => names.Select(name => $"{name}.exe"),
			OsKind.MacOS => names.Select(name => $"{name}.app"),
			_ => names.SelectMany(name => LinuxExecutableSuffixes.Select(suffix => name + suffix)),
		};
	}


	public virtual string FindExecutable(string installDirectory, OsKind os, string executableName)
	{
		if (!Directory.Exists(installDirectory))
		{
			return "";
		}

		var candidates = GetExecutableCandidates(os, executableName)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var candidate in candidates)
		{
			var directPath = Path.Join(installDirectory, candidate);
			if (File.Exists(directPath) || Directory.Exists(directPath))
			{
				return directPath;
			}
		}

		// Cap recursion: scanning an entire user-pointed directory tree would be brutal
		// if the user picks the wrong folder. Three levels covers normal release archives.
		return SearchExecutable(installDirectory, candidates, depthRemaining: 3);
	}


	private static string SearchExecutable(string directory, IReadOnlyCollection<string> candidates, int depthRemaining)
	{
		if (depthRemaining < 0)
		{
			return "";
		}

		foreach (var file in Directory.GetFiles(directory))
		{
			if (candidates.Any(candidate => string.Equals(Path.GetFileName(file), candidate, StringComparison.OrdinalIgnoreCase)))
			{
				return file;
			}
		}

		foreach (var subDirectory in Directory.GetDirectories(directory))
		{
			if (candidates.Any(candidate => string.Equals(Path.GetFileName(subDirectory), candidate, StringComparison.OrdinalIgnoreCase)))
			{
				return subDirectory;
			}

			var match = SearchExecutable(subDirectory, candidates, depthRemaining - 1);
			if (!string.IsNullOrEmpty(match))
			{
				return match;
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

	protected static string NormalizeVersion(string version) => version?.Trim().TrimStart('v') ?? "";
}
