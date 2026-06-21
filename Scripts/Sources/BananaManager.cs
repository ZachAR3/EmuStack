using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using HttpClient = System.Net.Http.HttpClient;

public class BananaManager
{
	private const string ApiBase = "https://gamebanana.com/apiv11";

	// Subfeed search returns ~30 records per page. Iterate multiple pages so
	// titles with many entries actually surface matches beyond the first page
	// (issue #61). Five pages is a sane cap — beyond that the user should be
	// narrowing their query rather than scanning further.
	private const int MaxSearchPages = 5;

	private readonly HttpClient _httpClient;
	private readonly Dictionary<string, int> _gameIdCache = new();


	public BananaManager(HttpClient httpClient)
	{
		_httpClient = httpClient;
	}


	public async Task<List<Mod>> GetAvailableMods(string gameId, Dictionary<string, string> installedGames, int sourceId, int page)
	{
		if (!installedGames.TryGetValue(gameId, out var gameName))
		{
			return new List<Mod>();
		}

		var bananaGameId = await ResolveGameBananaGameId(gameName);
		if (bananaGameId == -1)
		{
			Tools.Instance.AddError($"Could not find '{gameName}' on GameBanana. Mods for this title will be unavailable.");
			return new List<Mod>();
		}

		return await FetchMods(bananaGameId, sourceId, page, searchQuery: null);
	}


	public async Task<List<Mod>> SearchMods(string gameId, Dictionary<string, string> installedGames, int sourceId, string query)
	{
		if (!installedGames.TryGetValue(gameId, out var gameName))
		{
			return new List<Mod>();
		}

		var bananaGameId = await ResolveGameBananaGameId(gameName);
		if (bananaGameId == -1)
		{
			return new List<Mod>();
		}

		// Subfeed returns mixed-type records; paginate through results so mods
		// beyond the first page are included (issue #61). Stop when a page
		// returns nothing new.
		var results = new List<Mod>();
		var seenModIds = new HashSet<string>();

		for (var page = 1; page <= MaxSearchPages; page++)
		{
			var pageMods = await FetchMods(bananaGameId, sourceId, page, searchQuery: query);
			if (pageMods.Count == 0)
			{
				break;
			}

			var addedFromPage = false;
			foreach (var mod in pageMods)
			{
				if (!string.IsNullOrEmpty(mod.ModId) && !seenModIds.Add(mod.ModId))
				{
					continue;
				}

				results.Add(mod);
				addedFromPage = true;
			}

			if (!addedFromPage)
			{
				break;
			}
		}

		return results;
	}


	/// <summary>
	/// Looks up an online source for a locally-installed (unmanaged) mod by name,
	/// so it can be treated as a managed mod going forward (issue #40). Returns the
	/// match only when unambiguous: an exact name match, or a single search result.
	/// </summary>
	public async Task<Mod> FindOnlineSource(string gameId, Dictionary<string, string> installedGames, string modName, int sourceId)
	{
		if (string.IsNullOrWhiteSpace(modName))
		{
			return null;
		}

		var results = await SearchMods(gameId, installedGames, sourceId, modName);
		if (results.Count == 0)
		{
			return null;
		}

		var exact = results.FirstOrDefault(r =>
			string.Equals(r.ModName?.Trim(), modName.Trim(), StringComparison.OrdinalIgnoreCase));

		// Only auto-link when unambiguous to avoid attaching the wrong source.
		return exact ?? (results.Count == 1 ? results[0] : null);
	}


	private async Task<List<Mod>> FetchMods(int bananaGameId, int sourceId, int page, string searchQuery)
	{
		var url = $"{ApiBase}/Game/{bananaGameId}/Subfeed?_nPage={page}";
		if (!string.IsNullOrEmpty(searchQuery))
		{
			url += $"&_sName={Uri.EscapeDataString(searchQuery)}";
		}

		var response = await _httpClient.GetAsync(url);
		if (!response.IsSuccessStatusCode)
		{
			return new List<Mod>();
		}

		var json = JObject.Parse(await response.Content.ReadAsStringAsync());
		var records = json["_aRecords"] as JArray;
		if (records == null)
		{
			return new List<Mod>();
		}

		var mods = new List<Mod>();
		foreach (var record in records)
		{
			// The Subfeed endpoint returns mixed types (Mods, Wips, Questions, Tools,
			// Requests, Tutorials). Only Mods with downloadable files are installable.
			if (record["_sModelName"]?.ToString() != "Mod")
			{
				continue;
			}

			if (record["_bHasFiles"]?.Value<bool>() != true)
			{
				continue;
			}

			var mod = await ParseModRecord(record, sourceId);
			if (mod != null)
			{
				mods.Add(mod);
			}
		}

		return mods;
	}


	private async Task<Mod> ParseModRecord(JToken record, int sourceId)
	{
		var modId = record["_idRow"]?.ToString() ?? "";
		var modName = record["_sName"]?.ToString() ?? "";

		// Fetch the download URL from the files endpoint. A mod may have multiple
		// files (variants); take the first one — EmuStack installs the mod as a
		// single archive.
		var filesResponse = await _httpClient.GetAsync($"{ApiBase}/Mod/{modId}/Files");
		if (!filesResponse.IsSuccessStatusCode)
		{
			return null;
		}

		var filesJson = JToken.Parse(await filesResponse.Content.ReadAsStringAsync());
		var firstFile = filesJson?.FirstOrDefault();
		var downloadUrl = firstFile?["_sDownloadUrl"]?.ToString();
		if (string.IsNullOrEmpty(downloadUrl))
		{
			return null;
		}

		// Compatible game versions are in _aTags as "Game Version: Version X.Y.Z".
		// The _sVersion field is the mod's own version, NOT game compatibility.
		var compatibleVersions = ParseCompatibleVersions(record["_aTags"]);

		return new Mod
		{
			ModId = modId,
			ModName = modName,
			ModUrl = downloadUrl,
			CompatibleVersions = compatibleVersions,
			Source = sourceId,
		};
	}


	private static List<string> ParseCompatibleVersions(JToken tags)
	{
		if (tags == null)
		{
			return new List<string> { "NA" };
		}

		var versions = new List<string>();
		foreach (var tag in tags)
		{
			var tagStr = tag.ToString();
			const string prefix = "Game Version: ";
			if (tagStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				var version = tagStr[prefix.Length..].Trim();
				// Strip a leading "Version " if the tag uses the long form.
				const string versionPrefix = "Version ";
				if (version.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase))
				{
					version = version[versionPrefix.Length..];
				}

				versions.Add(version);
			}
		}

		return versions.Count > 0 ? versions : new List<string> { "NA" };
	}


	private async Task<int> ResolveGameBananaGameId(string gameName)
	{
		if (_gameIdCache.TryGetValue(gameName, out var cachedId))
		{
			return cachedId;
		}

		var response = await _httpClient.GetAsync($"{ApiBase}/Util/Game/NameMatch?_sName={Uri.EscapeDataString(gameName)}");
		if (!response.IsSuccessStatusCode)
		{
			return -1;
		}

		var json = JObject.Parse(await response.Content.ReadAsStringAsync());
		var records = json["_aRecords"] as JArray;
		if (records == null || records.Count == 0)
		{
			return -1;
		}

		var modId = records[0]?["_idRow"]?.ToString();
		var id = int.TryParse(modId, out var parsedId) ? parsedId : -1;

		if (id != -1)
		{
			_gameIdCache[gameName] = id;
		}

		return id;
	}
}
