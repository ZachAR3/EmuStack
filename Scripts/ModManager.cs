using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Button = Godot.Button;
using ProgressBar = Godot.ProgressBar;


public partial class ModManager : Control
{
	private const int LabelLeftPadding = 4;
	private const float SearchDebounceSeconds = 0.35f;

	[ExportGroup("ModManager")]
	[Export] private string _installedModsPath;
	[Export] private ItemList _modList;
	[Export] private ProgressBar _downloadBar;
	[Export] private Timer _downloadUpdateTimer;
	[Export] private HttpRequest _downloadRequester;
	[Export] private HttpRequest _titleRequester;
	[Export] private Texture2D _installedIcon;
	[Export] private Panel _loadingPanel;
	[Export] private OptionButton _gamePickerButton;
	[Export] private OptionButton _sourcePickerButton;
	[Export] private Button _modLocationButton;
	[Export] private Button _refreshButton;
	[Export] private Button _updateAllButton;
	[Export] private Button _updateSelectedButton;
	[Export] private Button _loadMoreButton;
	[Export] private LineEdit _manualGameIdLineEdit;
	[Export] private Button _addGameButton;
	[Export] private LineEdit _searchBar;


	private string _currentGameId;


	public enum Sources
	{
		Banana = 1,
	}

	private Dictionary<string, List<Mod>> _browseMods = new();
	private Dictionary<string, List<Mod>> _searchMods = new();
	private Sources _selectedSource = Sources.Banana;

	private readonly Dictionary<string, string> _titles = new();
	private Dictionary<string, string> _installedGames = new();
	private Dictionary<string, List<Mod>> _installedMods = new();

	private readonly StandardModManagement _standardModManager = new();
	// Initialized in Initiate — Globals.Instance is not set yet during construction
	// (autoload _Ready runs after all scene-node constructors).
	private BananaManager _bananaManager;

	private int _modsPage = 1;
	private ProviderSettingsResource ProviderSettings => Globals.Instance.CurrentProviderSettings;

	// Search state. _searchToken is incremented on every keystroke so that stale
	// debounced searches can be discarded when a newer one is already in flight.
	private bool _isSearchMode;
	private int _searchToken;

	private Dictionary<string, List<Mod>> ActiveSourceMods => _isSearchMode ? _searchMods : _browseMods;


	private async void Initiate()
	{
		if (Globals.Instance.Settings.LauncherMode)
		{
			return;
		}

		// NOTIFICATION_WM_CLOSE_REQUEST is delivered to the root Window, not nested
		// Controls, so subscribe to the root window's close signal to flush the
		// installed-mods index when the app is closed.
		GetTree().Root.CloseRequested += () => SaveInstalledMods();

		_bananaManager = new BananaManager(Globals.Instance.HttpClient);

		_installedModsPath = ProjectSettings.GlobalizePath(_installedModsPath);

		_standardModManager.DownloadRequester = _downloadRequester;
		_standardModManager.DownloadUpdateTimer = _downloadUpdateTimer;

		_loadMoreButton.Disabled = true;

		_titleRequester.Connect("request_completed", new Callable(this, nameof(GetTitles)));

		_modLocationButton.Text = PadLabel(ProviderSettings.ModsLocation);

		AddSources();
		await GetGamesAndMods();
	}


	public void ResetInstalled()
	{
		_installedMods = new Dictionary<string, List<Mod>>();
		SaveInstalledMods();
	}


	private async Task GetGamesAndMods(Sources source = Sources.Banana, int selectedGame = 0)
	{
		_loadingPanel.Visible = true;
		if (!EnsureModsLocation())
		{
			_loadingPanel.Visible = false;
			return;
		}

		var requestError = _titleRequester.Request(
			"https://switchbrew.org/w/index.php?title=Title_list/Games&mobileaction=toggle_view_desktop");
		if (requestError != Error.Ok)
		{
			Tools.Instance.AddError($@"failed to request titles list: {requestError}");
			_loadingPanel.Visible = false;
			return;
		}

		// GetTitles fires on completion and populates _titles, including built-in fallbacks.
		await ToSignal(_titleRequester, "request_completed");

		if (_titles.Count <= 0)
		{
			Tools.Instance.AddError("Failed to retrieve titles list, check connection and try again later.");
			_loadingPanel.Visible = false;
			return;
		}

		foreach (var gameModFolder in Directory.GetDirectories(ProviderSettings.ModsLocation))
		{
			var gameId = gameModFolder.GetFile();
			if (_titles.TryGetValue(gameId, out var gameName))
			{
				_installedGames[gameId] = gameName;
				GetInstalledMods(gameId);
				_gamePickerButton.AddItem(PadLabel(gameName));
				await GetAvailableMods(gameId, source);
			}
			else
			{
				Tools.Instance.AddError($@"could not find associated game title for: {gameId}");
			}
		}

		if (_installedGames.Count > 0)
		{
			selectedGame = Math.Clamp(selectedGame, 0, _installedGames.Count - 1);
			_gamePickerButton.Selected = selectedGame;
			SelectGame(selectedGame);
		}
		else
		{
			_loadingPanel.Visible = false;
		}
	}


	private bool EnsureModsLocation()
	{
		if (string.IsNullOrWhiteSpace(ProviderSettings.ModsLocation))
		{
			Tools.Instance.AddError("Mods location is empty. Please choose a mods folder.");
			return false;
		}

		try
		{
			Directory.CreateDirectory(ProviderSettings.ModsLocation);
			return true;
		}
		catch (Exception createDirectoryError)
		{
			Tools.Instance.AddError($@"Unable to create mods location: {createDirectoryError.Message}");
			return false;
		}
	}


	private async Task AddGameFromInput(string gameId)
	{
		gameId = gameId.Trim().ToUpperInvariant();
		if (!Regex.IsMatch(gameId, @"^[A-F0-9]{16}$"))
		{
			Tools.Instance.AddError("Game ID must be a 16-character hexadecimal Switch title ID.");
			return;
		}

		if (!EnsureModsLocation())
		{
			return;
		}

		Directory.CreateDirectory(Path.Join(ProviderSettings.ModsLocation, gameId));
		if (!_titles.ContainsKey(gameId))
		{
			Tools.Instance.AddError($"Title ID {gameId} was added, but its name was not found. GameBanana matching may not work for this title.");
			_titles[gameId] = gameId;
		}

		if (!_installedGames.ContainsKey(gameId))
		{
			_installedGames[gameId] = _titles[gameId];
			GetInstalledMods(gameId);
			_gamePickerButton.AddItem(PadLabel(_titles[gameId]));
		}

		var gameIndex = GetGameButtonIndex(gameId);
		if (gameIndex >= 0)
		{
			_gamePickerButton.Selected = gameIndex;
			_currentGameId = gameId;
			_modList.Clear();
			await GetAvailableMods(gameId, _selectedSource);
			SelectGame(gameIndex);
		}
	}


	private async Task GetAvailableMods(string gameId, Sources source)
	{
		if (gameId == null || !_installedGames.ContainsKey(gameId))
		{
			Tools.Instance.AddError("game ID invalid. Cancelling...");
			_loadingPanel.Visible = false;
			return;
		}

		try
		{
			var mods = await _bananaManager.GetAvailableMods(gameId, _installedGames, (int)Sources.Banana, _modsPage);
			AppendBrowseMods(gameId, mods);
		}
		catch (ArgumentException argumentException)
		{
			Tools.Instance.AddError(
				$@"Failed to retrieve mod list for ID:{gameId} | Title:{_titles[gameId]}. Exception:{argumentException.Message}");
			return;
		}

		RemoveInstalledFromBrowse(gameId);
	}


	private void AppendBrowseMods(string gameId, List<Mod> mods)
	{
		if (!_browseMods.ContainsKey(gameId))
		{
			_browseMods[gameId] = new List<Mod>();
		}

		_browseMods[gameId].AddRange(mods);
	}


	private void RemoveInstalledFromBrowse(string gameId)
	{
		if (!_browseMods.TryGetValue(gameId, out var browseList))
		{
			return;
		}

		if (!_installedMods.TryGetValue(gameId, out var installedList))
		{
			return;
		}

		var installedKeys = new HashSet<string>(installedList.Select(ModKey));
		browseList.RemoveAll(mod => installedKeys.Contains(ModKey(mod)));
	}


	/// <summary>
	/// A stable identity key for deduplication: uses ModId when available, falls
	/// back to lowercased ModName for local mods that have no remote ID.
	/// </summary>
	private static string ModKey(Mod mod)
	{
		return !string.IsNullOrEmpty(mod.ModId)
			? mod.ModId
			: (mod.ModName ?? "").ToLower().Trim();
	}


	private void GetInstalledMods(string gameId)
	{
		try
		{
			_installedMods[gameId] = new List<Mod>();

			if (File.Exists(_installedModsPath))
			{
				var installedModsJson =
					JsonSerializer.Deserialize<Dictionary<string, List<Mod>>>(File.ReadAllText(_installedModsPath));
				if (installedModsJson != null && installedModsJson.TryGetValue(gameId, out var gameMods))
				{
					_installedMods[gameId] = gameMods;
				}
			}

			foreach (var modDirectory in Directory.GetDirectories(Path.Join(ProviderSettings.ModsLocation, gameId)))
			{
				if (ModPaths.IsManaged(modDirectory.GetFile()))
				{
					continue;
				}

				var modToAdd = new Mod
				{
					ModName = modDirectory.GetFile(),
					ModUrl = null,
					CompatibleVersions = new List<string> { "NA" },
					Source = -1,
					InstalledPath = modDirectory,
				};

				if (_installedMods[gameId].Any(mod => mod.InstalledPath == modToAdd.InstalledPath))
				{
					continue;
				}

				_installedMods[gameId].Add(modToAdd);
			}
		}
		catch (Exception installedError)
		{
			Tools.Instance.AddError($@"cannot find installed mods error: {installedError}");
			_loadingPanel.Visible = false;
		}

		SaveInstalledMods();
	}


	private async void LoadNextPage()
	{
		if (_isSearchMode || string.IsNullOrEmpty(_currentGameId))
		{
			return;
		}

		_modsPage++;
		_loadingPanel.Visible = true;
		DisableInteraction();

		var oldModCount = _browseMods.TryGetValue(_currentGameId, out var oldMods) ? oldMods.Count : 0;

		var mods = await _bananaManager.GetAvailableMods(_currentGameId, _installedGames, (int)Sources.Banana, _modsPage);
		AppendBrowseMods(_currentGameId, mods);

		var noNewMods = !_browseMods.TryGetValue(_currentGameId, out var browseList) || browseList.Count <= oldModCount;
		_loadMoreButton.Disabled = noNewMods;

		RemoveInstalledFromBrowse(_currentGameId);

		DisableInteraction(false);
		_loadingPanel.Visible = false;

		SelectGame(_gamePickerButton.Selected);
	}


	private void AddMods(string gameId)
	{
		if (!ActiveSourceMods.ContainsKey(gameId) && !_installedMods.ContainsKey(gameId))
		{
			_loadingPanel.Visible = false;
			return;
		}

		if (_installedMods.TryGetValue(gameId, out var installedMods))
		{
			foreach (var mod in installedMods)
			{
				var modIndex = _modList.AddItem($@"  {mod.ModName} || Supports:{string.Join(", ", mod.CompatibleVersions)}  ",
					icon: _installedIcon);
				_modList.SetItemMetadata(modIndex, mod.ModName);
			}
		}

		if (ActiveSourceMods.TryGetValue(gameId, out var sourceMods))
		{
			foreach (var mod in sourceMods)
			{
				var modIndex = _modList.AddItem($@"  {mod.ModName} || Supports:{string.Join(", ", mod.CompatibleVersions)}  ");
				_modList.SetItemMetadata(modIndex, mod.ModName);
			}
		}

		_loadingPanel.Visible = false;
	}


	private void GetTitles(long result, long responseCode, string[] headers, byte[] body)
	{
		// Fall back to the built-in list whether or not the network request succeeded.
		AddBuiltInTitles();

		if (result != (int)HttpRequest.Result.Success || responseCode is < 200 or >= 400)
		{
			Tools.Instance.AddError($@"cannot retrieve titles, result: {result}, HTTP status: {responseCode}");
			_loadingPanel.Visible = false;
			return;
		}

		var gamesList = Encoding.UTF8.GetString(body).Split("<tr>");
		var gameList = gamesList.ToList();

		if (gameList.Count < 2)
		{
			Tools.Instance.AddError("cannot retrieve titles");
			return;
		}

		gameList.RemoveRange(0, 2);

		foreach (var game in gameList)
		{
			// Strip all HTML tags (the wiki wraps titles in <a> tags inside <td> cells)
			// and decode entities so GameBanana name-matching gets plain text.
			var gameCleaned = StripWikiHtml(game);
			var gameSplit = gameCleaned.Split("\n");

			if (gameSplit.Length < 2)
			{
				Tools.Instance.AddError("unable to parse titles list, check connection and try again later.");
				_loadingPanel.Visible = false;
				return;
			}

			var gameId = gameSplit[1].Trim();
			var gameName = gameSplit[2].Trim();
			if (gameId.Length > 0 && gameName.Length > 0)
			{
				_titles[gameId] = gameName;
			}
		}
	}


	private static string StripWikiHtml(string raw)
	{
		// Remove all HTML tags, decode entities, strip the trademark glyph.
		var stripped = Regex.Replace(raw, @"<[^>]+>", "");
		stripped = WebUtility.HtmlDecode(stripped);
		stripped = stripped.Replace("™", "");
		return stripped;
	}


	private void AddBuiltInTitles()
	{
		_titles["0100F2C0115B6000"] = "The Legend of Zelda: Tears of the Kingdom";
		_titles["010010A00DA48000"] = "Baldur's Gate and Baldur's Gate II: Enhanced Editions";
		_titles["010012101468C000"] = "Metroid Prime Remastered";
		_titles["010028600EBDA000"] = "Super Mario™ 3D World + Bowser’s Fury";
		_titles["01002B00111A2000"] = "Hyrule Warriors: Age of Calamity";
		_titles["01002DA013484000"] = "The Legend of Zelda: Skyward Sword HD";
		_titles["010030B00C316000"] = "Planescape: Torment and Icewind Dale: Enhanced Editions";
		_titles["010031200E044000"] = "Two Point Hospital";
	}


	private async void UpdateAll()
	{
		if (!await Tools.Instance.ConfirmationPopup("Update all mods?"))
		{
			return;
		}

		foreach (var installedGame in _installedGames)
		{
			foreach (var mod in new List<Mod>(_installedMods[installedGame.Key]))
			{
				if (mod.ModUrl != null)
				{
					var modUpdated = await UpdateMod(installedGame.Key, mod, true);
					if (modUpdated != true)
					{
						Tools.Instance.AddError($@"failed to update:{mod.ModName}");
						_loadingPanel.Visible = false;
						return;
					}
				}
			}
		}

		SelectGame(_gamePickerButton.Selected);
	}


	private async Task<bool> UpdateMod(string gameId, Mod mod, bool noConfirmation = false)
	{
		if (!noConfirmation && !await Tools.Instance.ConfirmationPopup($@"Update {mod.ModName}?"))
		{
			return false;
		}

		try
		{
			var removedMod = await DeleteMod(gameId, mod, true);
			if (!removedMod)
			{
				Tools.Instance.AddError($@"failed to update mod, unable to delete old... Returning.");
				return false;
			}

			await InstallMod(gameId, mod);
		}
		catch (Exception updateError)
		{
			Tools.Instance.AddError($@"failed to update mod:{updateError}");
			return false;
		}

		SelectGame(_gamePickerButton.Selected);
		return true;
	}


	private async void SelectGame(int gameIndex)
	{
		if (gameIndex < 0 || gameIndex >= _gamePickerButton.ItemCount)
		{
			return;
		}

		_currentGameId = GetGameIdFromValue(_gamePickerButton.GetItemText(gameIndex).Trim(), _installedGames);
		_modList.Clear();

		_sourcePickerButton.Clear();
		AddSources();

		AddMods(_currentGameId);
	}


	private static string GetGameIdFromValue(string value, Dictionary<string, string> installedGames)
	{
		foreach (var (gameId, gameName) in installedGames)
		{
			if (gameName == value)
			{
				return gameId;
			}
		}

		return null;
	}


	private async Task Refresh(Sources source = Sources.Banana)
	{
		ExitSearchMode();

		_modList.Clear();
		_browseMods.Clear();
		_installedGames.Clear();
		_titles.Clear();

		var selectedGame = _gamePickerButton.Selected;
		_gamePickerButton.Clear();
		_selectedSource = source;

		await GetGamesAndMods(source, selectedGame);
	}


	private void AddSources()
	{
		foreach (Sources source in Enum.GetValues(typeof(Sources)))
		{
			_sourcePickerButton.AddItem(PadLabel(source.ToString()), (int)source);
		}

		_sourcePickerButton.Select(GetSourceButtonIndex(_selectedSource));
	}


	private void DisableInteraction(bool interactionDisabled = true)
	{
		for (var itemIndex = 0; itemIndex < _modList.ItemCount; itemIndex++)
		{
			_modList.SetItemDisabled(itemIndex, interactionDisabled);
		}

		_gamePickerButton.Disabled = interactionDisabled;
		_sourcePickerButton.Disabled = interactionDisabled;
		_modLocationButton.Disabled = interactionDisabled;
		_refreshButton.Disabled = interactionDisabled;
		_updateAllButton.Disabled = interactionDisabled;
		_updateSelectedButton.Disabled = interactionDisabled;
		_loadMoreButton.Disabled = interactionDisabled;
	}


	private async Task SelectSource(int sourceIndex)
	{
		var sourceId = _sourcePickerButton.GetItemId(sourceIndex);
		if (!Enum.IsDefined(typeof(Sources), sourceId))
		{
			Tools.Instance.AddError("source not found, please file a bug report. Defaulting back to GameBanana");
			_selectedSource = Sources.Banana;
			_sourcePickerButton.Select(GetSourceButtonIndex(_selectedSource));
			return;
		}

		_selectedSource = (Sources)sourceId;
		_loadMoreButton.Disabled = false;

		await Refresh(_selectedSource);
	}


	private int GetSourceButtonIndex(Sources sourceId)
	{
		for (var itemIndex = 0; itemIndex < _sourcePickerButton.ItemCount; itemIndex++)
		{
			if (_sourcePickerButton.GetItemId(itemIndex) == (int)sourceId)
			{
				return itemIndex;
			}
		}

		return 0;
	}


	private int GetGameButtonIndex(string gameId)
	{
		for (var itemIndex = 0; itemIndex < _gamePickerButton.ItemCount; itemIndex++)
		{
			if (GetGameIdFromValue(_gamePickerButton.GetItemText(itemIndex).Trim(), _installedGames) == gameId)
			{
				return itemIndex;
			}
		}

		return -1;
	}


	private async void ClearMods(string gameId = "")
	{
		gameId = string.IsNullOrEmpty(gameId) ? _currentGameId : gameId;
		if (!_installedMods.ContainsKey(gameId))
		{
			return;
		}

		try
		{
			// DeleteMod already handles directory removal; the old code also iterated
			// the game folder and deleted directories a second time, which threw when
			// the directory was already gone.
			foreach (var mod in new List<Mod>(_installedMods[gameId]))
			{
				await DeleteMod(gameId, mod, true);
				_installedMods[gameId].Remove(mod);
			}
		}
		catch (Exception deleteError)
		{
			Tools.Instance.AddError($@"failed to delete mod:{deleteError}");
		}

		SelectGame(_gamePickerButton.Selected);
	}


	private void SaveInstalledMods()
	{
		var saveDirectory = Path.GetDirectoryName(_installedModsPath);
		if (!string.IsNullOrEmpty(saveDirectory))
		{
			Directory.CreateDirectory(saveDirectory);
		}

		var serializedMods = JsonSerializer.Serialize(_installedMods);
		File.WriteAllText(_installedModsPath, serializedMods);
	}


	private async Task InstallMod(string gameId, Mod mod)
	{
		if (await _standardModManager.InstallMod(gameId, mod, _installedMods, ActiveSourceMods))
		{
			_downloadBar.Value = 100;
		}

		// Keep the inactive source dictionary consistent so switching between
		// browse and search views doesn't show stale entries.
		RemoveModFromInactiveSource(gameId, mod);

		SaveInstalledMods();
	}


	private async Task<bool> DeleteMod(string gameId, Mod mod, bool noConfirmation = false)
	{
		var successful = await _standardModManager.DeleteMod(gameId, mod, _installedMods, ActiveSourceMods, noConfirmation);

		// Re-add the mod to the browse list so it reappears when not searching.
		if (successful && mod.ModUrl != null)
		{
			if (!_browseMods.TryGetValue(gameId, out var browseList))
			{
				browseList = new List<Mod>();
				_browseMods[gameId] = browseList;
			}

			if (!browseList.Any(m => ModKey(m) == ModKey(mod)))
			{
				browseList.Add(mod);
			}
		}

		SaveInstalledMods();
		return successful;
	}


	private void RemoveModFromInactiveSource(string gameId, Mod mod)
	{
		var inactive = _isSearchMode ? _browseMods : _searchMods;
		if (inactive.TryGetValue(gameId, out var inactiveList))
		{
			inactiveList.RemoveAll(m => ModKey(m) == ModKey(mod));
		}
	}


	private async void SearchUpdated(string newSearch)
	{
		var query = newSearch.Trim();
		var token = ++_searchToken;

		// Debounce: wait before issuing the search so we don't hit the API on
		// every keystroke.
		await ToSignal(GetTree().CreateTimer(SearchDebounceSeconds), "timeout");

		// A newer keystroke arrived while we were waiting — discard this search.
		if (token != _searchToken)
		{
			return;
		}

		await PerformSearch(query);
	}


	private async Task PerformSearch(string query)
	{
		var gameId = _currentGameId;
		if (string.IsNullOrEmpty(gameId))
		{
			return;
		}

		// Empty query: exit search mode and show browse results.
		if (string.IsNullOrEmpty(query))
		{
			ExitSearchMode();
			_modList.Clear();
			AddMods(gameId);
			return;
		}

		_isSearchMode = true;
		_searchMods.Clear();
		_loadMoreButton.Disabled = true;
		_loadingPanel.Visible = true;

		try
		{
			var results = await _bananaManager.SearchMods(gameId, _installedGames, (int)Sources.Banana, query);
			_searchMods[gameId] = FilterInstalled(gameId, results);
		}
		catch (ArgumentException ex)
		{
			Tools.Instance.AddError($"Search failed: {ex.Message}");
			_searchMods[gameId] = new List<Mod>();
		}

		_loadingPanel.Visible = false;
		_modList.Clear();
		AddMods(gameId);
	}


	private List<Mod> FilterInstalled(string gameId, List<Mod> mods)
	{
		if (!_installedMods.TryGetValue(gameId, out var installedList))
		{
			return mods;
		}

		var installedKeys = new HashSet<string>(installedList.Select(ModKey));
		return mods.Where(m => !installedKeys.Contains(ModKey(m))).ToList();
	}


	private void ExitSearchMode()
	{
		_isSearchMode = false;
		_searchMods.Clear();
		_searchToken++;
		if (IsInstanceValid(_searchBar))
		{
			_searchBar.Text = "";
		}
		_loadMoreButton.Disabled = false;
	}


	private async void ModClicked(int modIndex)
	{
		if (_installedMods.TryGetValue(_currentGameId, out var installedMods))
		{
			foreach (var mod in installedMods)
			{
				if ((string)_modList.GetItemMetadata(modIndex) == mod.ModName)
				{
					DisableInteraction();
					await DeleteMod(_currentGameId, mod);
					DisableInteraction(false);
					SelectGame(_gamePickerButton.Selected);
					return;
				}
			}
		}

		if (ActiveSourceMods.TryGetValue(_currentGameId, out var selectedSourceMods))
		{
			foreach (var mod in selectedSourceMods)
			{
				if ((string)_modList.GetItemMetadata(modIndex) == mod.ModName)
				{
					DisableInteraction();
					await InstallMod(_currentGameId, mod);
					DisableInteraction(false);
					SelectGame(_gamePickerButton.Selected);
					return;
				}
			}
		}
	}


	private async void UpdateSelectedPressed()
	{
		if (!_installedMods.TryGetValue(_currentGameId, out var installedMods))
		{
			return;
		}

		var selectedMods = _modList.GetSelectedItems();
		if (selectedMods.Length <= 0)
		{
			return;
		}

		var selectedModName = (string)_modList.GetItemMetadata(selectedMods.First());

		foreach (var mod in installedMods)
		{
			if (selectedModName == mod.ModName)
			{
				DisableInteraction();
				if (mod.ModUrl != null)
				{
					await UpdateMod(_currentGameId, mod);
				}
				else
				{
					// Local unmanaged mod: look up an online source by name so the
					// mod becomes managed and can be updated in future (issue #40).
					// Falls back to the dedicated Link Online flow.
					await LinkLocalModOnline(_currentGameId, mod);
				}

				SelectGame(_gamePickerButton.Selected);
				DisableInteraction(false);
				return;
			}
		}
	}


	/// <summary>
	/// Searches GameBanana for an online source matching a locally-installed
	/// (unmanaged) mod by name. If an unambiguous match is found, attaches the
	/// ModUrl/ModId/Source to the installed mod and persists it (issue #40).
	/// </summary>
	private async Task LinkLocalModOnline(string gameId, Mod mod)
	{
		if (!await Tools.Instance.ConfirmationPopup($"Search GameBanana for an online source for '{mod.ModName}'?"))
		{
			return;
		}

		var match = await _bananaManager.FindOnlineSource(gameId, _installedGames, mod.ModName, (int)Sources.Banana);
		if (match == null)
		{
			Tools.Instance.AddError($"Could not find an unambiguous online source for '{mod.ModName}'.");
			return;
		}

		mod.ModId = match.ModId;
		mod.ModUrl = match.ModUrl;
		mod.Source = match.Source;
		if (match.CompatibleVersions != null && match.CompatibleVersions.Count > 0)
		{
			mod.CompatibleVersions = match.CompatibleVersions;
		}

		SaveInstalledMods();
	}


	private async void LinkOnlinePressed()
	{
		if (_currentGameId == null || !_installedMods.TryGetValue(_currentGameId, out var installedMods))
		{
			return;
		}

		var selectedMods = _modList.GetSelectedItems();
		if (selectedMods.Length <= 0)
		{
			Tools.Instance.AddError("Select an installed local mod first.");
			return;
		}

		var selectedModName = (string)_modList.GetItemMetadata(selectedMods.First());
		foreach (var mod in installedMods)
		{
			if (selectedModName != mod.ModName)
			{
				continue;
			}

			if (mod.ModUrl != null)
			{
				Tools.Instance.AddError($"'{mod.ModName}' is already linked to an online source.");
				return;
			}

			DisableInteraction();
			await LinkLocalModOnline(_currentGameId, mod);
			SelectGame(_gamePickerButton.Selected);
			DisableInteraction(false);
			return;
		}
	}


	private async void ModLocationPressed()
	{
		var picked = await Tools.Instance.PickFolder(ProviderSettings.ModsLocation);
		if (picked != null)
		{
			ProviderSettings.ModsLocation = picked;
			Globals.Instance.SyncCurrentProviderSettings();
			EnsureModsLocation();
		}

		_modLocationButton.Text = PadLabel(ProviderSettings.ModsLocation ?? "");
		await Refresh(_selectedSource);
	}


	private async void RefreshPressed()
	{
		await Refresh(_selectedSource);
	}


	private void UpdateDownloadProgress()
	{
		if (_downloadRequester.GetBodySize() <= 0)
		{
			return;
		}

		_downloadBar.Value = (float)_downloadRequester.GetDownloadedBytes() / _downloadRequester.GetBodySize() * 100;
	}


	private async void SourceSelected(int selectedSource)
	{
		await SelectSource(selectedSource);
	}


	private async void AddGamePressed()
	{
		await AddGameFromInput(_manualGameIdLineEdit.Text);
	}


	private async void AddGameTextSubmitted(string gameId)
	{
		await AddGameFromInput(gameId);
	}


	private async void ClearModsPressed()
	{
		if (!await Tools.Instance.ConfirmationPopup("Remove all mods for selected title?"))
		{
			return;
		}
		ClearMods();
	}


	private static string PadLabel(string text) => text.PadLeft(text.Length + LabelLeftPadding, ' ');
}
