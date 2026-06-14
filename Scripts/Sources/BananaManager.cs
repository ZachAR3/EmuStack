using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using HttpClient = System.Net.Http.HttpClient;

public class BananaManager
{
	private readonly HttpClient _httpClient = new();
	
	
	public async Task<Dictionary<string, List<Mod>>> GetAvailableMods(Dictionary<string, List<Mod>> modList, Dictionary<string, Game> installedGames, string gameId, int sourceId, int page = 1)
	{
		if (!modList.ContainsKey(gameId))
		{
			modList[gameId] = new List<Mod>();
		}
		
		int bananaGameId = await GetGameBananaGameId(installedGames[gameId].GameName);
		if (bananaGameId == -1)
		{
			return modList;
		}
		
		var gameModsSource = await _httpClient
			.GetAsync($@"https://gamebanana.com/apiv11/Game/{bananaGameId}/Subfeed?_nPage={page}");
		if (!gameModsSource.IsSuccessStatusCode)
		{
			return modList;
		}

		var jsonMods = JObject.Parse(await gameModsSource.Content.ReadAsStringAsync());
		var records = jsonMods["_aRecords"];
		if (records == null)
		{
			return modList;
		}


		foreach (var mod in records)
		{
			var filesResponse = await _httpClient.GetAsync($@"https://gamebanana.com/apiv11/Mod/{mod["_idRow"]}/Files");
			if (!filesResponse.IsSuccessStatusCode)
			{
				continue;
			}

			string modPage = await filesResponse.Content.ReadAsStringAsync();
			var modPageContent = JToken.Parse(modPage);
			string downloadUrl = modPageContent[0]?["_sDownloadUrl"]?.ToString();
			if (string.IsNullOrEmpty(downloadUrl))
			{
				continue;
			}
			
			// If there is an available compatible version sets it as that, otherwises sets it as NA
			List<string> compatibleVersions = mod["_sVersion"] == null
				? new List<string>() { "NA" }
				: new List<string>() { mod["_sVersion"].ToString() };
			
			modList[gameId].Add(new Mod
			{
				ModName = mod["_sName"].ToString(), 
				ModUrl = downloadUrl, 
				CompatibleVersions = compatibleVersions, 
				Source = sourceId, 
				InstalledPath = null
			});
		}

		return modList;
	}
	
	 
	private async Task<int> GetGameBananaGameId(string gameName)
	{
		// Searches for the game ID using the name from banana mods
		var searchResponse = await _httpClient.GetAsync("https://gamebanana.com/apiv11/Util/Game/NameMatch?_sName=" + gameName);
		if (!searchResponse.IsSuccessStatusCode)
		{
			return -1;
		}

		string searchContent = await searchResponse.Content.ReadAsStringAsync();
		
		var jsonContent = JObject.Parse($@"{searchContent}");
		var modId = jsonContent["_aRecords"]?[0]?["_idRow"]!.ToString();

		// If the return is null replace with our version (-1)
		int returnValue = modId == null ? -1 : int.Parse(modId);
		return returnValue;
	}
}
