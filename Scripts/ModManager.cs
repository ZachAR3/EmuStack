using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Button = Godot.Button;
using ProgressBar = Godot.ProgressBar;
using CancellationTokenSource = System.Threading.CancellationTokenSource;


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

	private readonly Dictionary<string, int> _modsPageByGame = new();
	private readonly HashSet<string> _exhaustedPages = new();
	private bool _hasMorePages = true;
	private int _operationGeneration;
	private int _titleRequestGeneration = -1;
	private bool _operationInProgress;
	private Window _rootWindow;
	private string _legacyInstalledModsPath;
	private bool _initialized;
	private readonly CancellationTokenSource _lifetimeCancellation = new();
	private ProviderSettingsResource ProviderSettings => Globals.Instance.CurrentProviderSettings;

	// Search state. _searchToken is incremented on every keystroke so that stale
	// debounced searches can be discarded when a newer one is already in flight.
	private bool _isSearchMode;
	private int _searchToken;

	private Dictionary<string, List<Mod>> ActiveSourceMods => _isSearchMode ? _searchMods : _browseMods;


	private async void Initiate()
	{
		try
		{
			if (Globals.Instance.Settings.LauncherMode)
			{
				return;
			}

			// NOTIFICATION_WM_CLOSE_REQUEST is delivered to the root Window, not nested
			// Controls, so subscribe to the root window's close signal to flush the
			// installed-mods index when the app is closed.
			_rootWindow = GetTree().Root;
			_rootWindow.CloseRequested += OnCloseRequested;

			_bananaManager = new BananaManager(Globals.Instance.HttpClient);

			_legacyInstalledModsPath = ProjectSettings.GlobalizePath(_installedModsPath);
			var indexDirectory = Path.GetDirectoryName(_legacyInstalledModsPath) ?? ProjectSettings.GlobalizePath("user://");
			var indexName = Path.GetFileNameWithoutExtension(_legacyInstalledModsPath);
			_installedModsPath = Path.Join(indexDirectory, $"{indexName}-{Globals.Instance.AppMode.Id}.json");
			_initialized = true;
			LoadInstalledIndex();

			_standardModManager.DownloadRequester = _downloadRequester;
			_standardModManager.DownloadUpdateTimer = _downloadUpdateTimer;
			AddChild(_standardModManager);
			_downloadRequester.Timeout = 60;
			_titleRequester.Timeout = 30;

			_loadMoreButton.Disabled = true;

			_titleRequester.Connect("request_completed", new Callable(this, nameof(GetTitles)));

			_modLocationButton.Text = PadLabel(ProviderSettings.ModsLocation);

			AddSources();
			if (BeginOperation())
			{
				try
				{
					await GetGamesAndMods();
				}
				finally
				{
					EndOperation();
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Scene-owned work is canceled during provider switches and shutdown.
		}
		catch (Exception startupError)
		{
			_loadingPanel.Visible = false;
			Tools.Instance?.AddError($"Failed to initialize mod manager: {startupError.Message}");
		}
	}


	public override void _ExitTree()
	{
		_lifetimeCancellation.Cancel();
		_operationGeneration++;
		_searchToken++;
		_downloadRequester?.CancelRequest();
		_titleRequester?.CancelRequest();
		if (_rootWindow != null)
		{
			_rootWindow.CloseRequested -= OnCloseRequested;
		}
		if (_initialized && !_operationInProgress)
		{
			SaveInstalledMods();
		}
	}


	private void OnCloseRequested()
	{
		if (!_operationInProgress)
		{
			SaveInstalledMods();
		}
	}


	public bool ResetInstalled()
	{
		if (_operationInProgress || _lifetimeCancellation.IsCancellationRequested)
		{
			Tools.Instance?.AddError("Cannot reset the installed-mod index while another mod operation is running.");
			return false;
		}
		var previousInstalledMods = _installedMods;
		_installedMods = new Dictionary<string, List<Mod>>();
		var scanSucceeded = true;
		foreach (var gameId in _installedGames.Keys)
		{
			if (!GetInstalledMods(gameId, persist: false))
			{
				scanSucceeded = false;
			}
		}
		if (!scanSucceeded || !SaveInstalledMods())
		{
			_installedMods = previousInstalledMods;
			return false;
		}
		foreach (var gameId in _browseMods.Keys.ToList())
		{
			RemoveInstalledFromBrowse(gameId);
		}
		if (!string.IsNullOrEmpty(_currentGameId) && IsInstanceValid(_gamePickerButton))
		{
			SelectGame(_gamePickerButton.Selected);
		}

		return true;
	}


	private async Task GetGamesAndMods(int selectedGame = 0)
	{
		var generation = _operationGeneration;
		if (_lifetimeCancellation.IsCancellationRequested)
		{
			return;
		}
		_loadingPanel.Visible = true;
		if (!EnsureModsLocation())
		{
			_loadingPanel.Visible = false;
			return;
		}

		_titleRequester.CancelRequest();
		_titleRequestGeneration = generation;
		var requestError = _titleRequester.Request(
			"https://switchbrew.org/w/index.php?title=Title_list/Games&mobileaction=toggle_view_desktop");
		if (requestError != Error.Ok)
		{
			Tools.Instance?.AddError($@"failed to request titles list: {requestError}");
			_loadingPanel.Visible = false;
			return;
		}

		// GetTitles fires on completion and populates _titles, including built-in fallbacks.
		await ToSignal(_titleRequester, "request_completed");
		if (generation != _operationGeneration || _lifetimeCancellation.IsCancellationRequested)
		{
			return;
		}

		if (_titles.Count <= 0)
		{
			Tools.Instance?.AddError("Failed to retrieve titles list, check connection and try again later.");
			_loadingPanel.Visible = false;
			return;
		}

		foreach (var gameModFolder in Directory.GetDirectories(ProviderSettings.ModsLocation))
		{
			if (generation != _operationGeneration || _lifetimeCancellation.IsCancellationRequested)
			{
				return;
			}

			try
			{
				FsHelpers.EnsureNoReparsePoints(gameModFolder);
			}
			catch (Exception linkError) when (linkError is IOException or UnauthorizedAccessException)
			{
				continue;
			}
			var gameId = gameModFolder.GetFile();
			if (!IsValidGameId(gameId))
			{
				continue;
			}

			if (_titles.TryGetValue(gameId, out var gameName))
			{
				_installedGames[gameId] = gameName;
				GetInstalledMods(gameId);
				_gamePickerButton.AddItem(PadLabel(gameName));
				_gamePickerButton.SetItemMetadata(_gamePickerButton.ItemCount - 1, gameId);
				await GetAvailableMods(gameId, generation);
			}
			else
			{
				Tools.Instance?.AddError($@"could not find associated game title for: {gameId}");
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
			Tools.Instance?.AddError("Mods location is empty. Please choose a mods folder.");
			return false;
		}

		try
		{
			FsHelpers.RejectDangerousRoot(ProviderSettings.ModsLocation);
			FsHelpers.EnsureNoReparsePoints(ProviderSettings.ModsLocation);
			Directory.CreateDirectory(ProviderSettings.ModsLocation);
			FsHelpers.EnsureNoReparsePoints(ProviderSettings.ModsLocation);
			return true;
		}
		catch (Exception createDirectoryError)
		{
			Tools.Instance?.AddError($@"Unable to create mods location: {createDirectoryError.Message}");
			return false;
		}
	}


	private async Task AddGameFromInput(string gameId)
	{
		if (_lifetimeCancellation.IsCancellationRequested)
		{
			return;
		}
		gameId = gameId.Trim().ToUpperInvariant();
		if (!Regex.IsMatch(gameId, @"^[A-F0-9]{16}$"))
		{
			Tools.Instance?.AddError("Game ID must be a 16-character hexadecimal Switch title ID.");
			return;
		}

		if (!EnsureModsLocation())
		{
			return;
		}

		var gameRoot = ModPaths.GameRoot(ProviderSettings.ModsLocation, gameId);
		FsHelpers.EnsureNoReparsePoints(gameRoot);
		Directory.CreateDirectory(gameRoot);
		FsHelpers.EnsureNoReparsePoints(gameRoot);
		if (!_titles.ContainsKey(gameId))
		{
			Tools.Instance?.AddError($"Title ID {gameId} was added, but its name was not found. GameBanana matching may not work for this title.");
			_titles[gameId] = gameId;
		}

		if (!_installedGames.ContainsKey(gameId))
		{
			_installedGames[gameId] = _titles[gameId];
			GetInstalledMods(gameId);
			_gamePickerButton.AddItem(PadLabel(_titles[gameId]));
			_gamePickerButton.SetItemMetadata(_gamePickerButton.ItemCount - 1, gameId);
		}

		var gameIndex = GetGameButtonIndex(gameId);
		if (gameIndex >= 0)
		{
			if (_lifetimeCancellation.IsCancellationRequested)
			{
				return;
			}
			_gamePickerButton.Selected = gameIndex;
			_currentGameId = gameId;
			_modList.Clear();
			await GetAvailableMods(gameId, _operationGeneration);
			SelectGame(gameIndex);
		}
	}


	private async Task GetAvailableMods(string gameId, int expectedGeneration = -1)
	{
		if (gameId == null || !_installedGames.ContainsKey(gameId))
		{
			Tools.Instance?.AddError("game ID invalid. Cancelling...");
			_loadingPanel.Visible = false;
			return;
		}

		try
		{
			var page = _modsPageByGame.TryGetValue(gameId, out var loadedPage) ? loadedPage : 1;
			var mods = await _bananaManager.GetAvailableMods(
					gameId,
					_installedGames,
					(int)Sources.Banana,
					page,
					_lifetimeCancellation.Token);
			if ((expectedGeneration >= 0 && expectedGeneration != _operationGeneration) ||
				_lifetimeCancellation.IsCancellationRequested)
			{
				return;
			}
			AppendBrowseMods(gameId, mods);
			_modsPageByGame[gameId] = page;
			if (gameId == _currentGameId)
			{
				_hasMorePages = !_exhaustedPages.Contains(gameId);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception argumentException)
		{
			Tools.Instance?.AddError(
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

		var existingKeys = new HashSet<string>(_browseMods[gameId].Select(ModKey));
		_browseMods[gameId].AddRange(mods.Where(mod => existingKeys.Add(ModKey(mod))));
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
		return ModPaths.StableId(mod);
	}


	private bool GetInstalledMods(string gameId, bool persist = true)
	{
		var scanSucceeded = false;
		try
		{
			var gameRoot = ModPaths.GameRoot(ProviderSettings.ModsLocation, gameId);
				if (!IsValidGameId(gameId))
				{
					return false;
			}
			Directory.CreateDirectory(gameRoot);
			FsHelpers.EnsureNoReparsePoints(gameRoot);
			RecoverInterruptedModOperations(gameId, gameRoot);
			RecoverInterruptedModDeletes(gameId, gameRoot);
			if (!_installedMods.TryGetValue(gameId, out var gameMods))
			{
				gameMods = new List<Mod>();
				_installedMods[gameId] = gameMods;
			}

			gameMods.RemoveAll(mod => mod == null || !TryValidateOrMigrateManagedMod(gameId, mod));
			foreach (var modDirectory in Directory.GetDirectories(gameRoot))
			{
				if (Path.GetFileName(modDirectory).StartsWith(".emustack-", StringComparison.Ordinal))
				{
					continue;
				}
				try
				{
					FsHelpers.EnsureNoReparseTree(modDirectory);
				}
				catch (Exception linkError) when (linkError is IOException or UnauthorizedAccessException)
				{
					Tools.Instance?.AddError($"Skipping linked mod directory: {modDirectory}");
					continue;
				}

				if (ModPaths.IsManaged(modDirectory))
				{
					if (!gameMods.Any(mod => PathsEqual(mod.InstalledPath, modDirectory)))
					{
						var marker = File.ReadAllText(Path.Join(modDirectory, FsHelpers.OwnerMarkerFileName)).Trim();
						var markerPrefix = $"{ModPaths.OwnerPrefix}{Globals.Instance.AppMode.Id}:{gameId}:";
						if (marker.StartsWith(markerPrefix, StringComparison.Ordinal))
						{
							gameMods.Add(new Mod
							{
								StableId = marker[markerPrefix.Length..],
								ModName = modDirectory.GetFile(),
								CompatibleVersions = new List<string> { "NA" },
								InstalledPath = modDirectory,
							});
						}
					}
					continue;
				}

				var modToAdd = new Mod
				{
					ModName = modDirectory.GetFile(),
					ModUrl = null,
					CompatibleVersions = new List<string> { "NA" },
					Source = -1,
					StableId = $"local-{Guid.NewGuid():N}",
					InstalledPath = modDirectory,
				};

				if (gameMods.Any(mod => PathsEqual(mod.InstalledPath, modToAdd.InstalledPath)))
				{
					continue;
				}

				gameMods.Add(modToAdd);
			}
			scanSucceeded = true;
		}
		catch (Exception installedError)
		{
			Tools.Instance?.AddError($@"cannot find installed mods error: {installedError}");
			if (IsInstanceValid(_loadingPanel))
			{
				_loadingPanel.Visible = false;
			}
		}

		if (scanSucceeded && persist)
		{
			SaveInstalledMods();
		}

		return scanSucceeded;
	}


	private void RecoverInterruptedModOperations(string gameId, string gameRoot)
	{
		foreach (var operationRoot in Directory.GetDirectories(gameRoot, ".emustack-operation-*", SearchOption.TopDirectoryOnly))
		{
			try
			{
				FsHelpers.EnsureNoReparseTree(operationRoot);
				var operationMarker = File.ReadAllText(Path.Join(operationRoot, FsHelpers.OwnerMarkerFileName)).Trim();
				if (!operationMarker.StartsWith("operation:", StringComparison.Ordinal))
				{
					continue;
				}

				var installPath = File.ReadAllText(Path.Join(operationRoot, "install.path")).Trim();
				var previousPath = File.ReadAllText(Path.Join(operationRoot, "previous.path")).Trim();
				var previousPresent = !string.IsNullOrEmpty(previousPath);
				var ownerId = File.ReadAllText(Path.Join(operationRoot, "owner")).Trim();
				var phase = File.ReadAllText(Path.Join(operationRoot, "phase")).Trim();
				if (phase is not ("staging" or "swapping" or "filesystem-committed" or "state-committed" or "rollback"))
				{
					throw new InvalidDataException($"Unknown mod recovery phase: {phase}");
				}
				if (!FsHelpers.IsPathContainedBy(gameRoot, installPath) ||
				    (!string.IsNullOrEmpty(previousPath) && !FsHelpers.IsPathContainedBy(gameRoot, previousPath)))
				{
					throw new InvalidDataException("Operation journal contains an out-of-root path.");
				}
				var expectedOwnerPrefix = $"{ModPaths.OwnerPrefix}{Globals.Instance.AppMode.Id}:{gameId}:";
				if (!ownerId.StartsWith(expectedOwnerPrefix, StringComparison.Ordinal))
				{
					throw new InvalidDataException("Operation journal owner does not belong to the current provider and title.");
				}
				var modJournalPath = Path.Join(operationRoot, "mod.json");
				FsHelpers.EnsureNoReparsePoints(modJournalPath);
				var journalMod = JsonSerializer.Deserialize<Mod>(File.ReadAllText(modJournalPath));
				if (journalMod == null || !string.Equals(ModPaths.OwnerId(Globals.Instance.AppMode.Id, gameId, journalMod), ownerId, StringComparison.Ordinal))
				{
					throw new InvalidDataException("Operation journal mod identity does not match its owner.");
				}

				var previous = Path.Join(operationRoot, "previous");
				var stateCommitted = phase == "state-committed";
				var commitConfirmed = stateCommitted && Directory.Exists(installPath);
				if (commitConfirmed && !FsHelpers.IsOwnedDirectory(installPath, ownerId))
				{
					throw new IOException("Interrupted mod recovery found an unowned current directory.");
				}
				if (Directory.Exists(previous))
				{
					var previousOwner = File.ReadAllText(Path.Join(previous, FsHelpers.OwnerMarkerFileName)).Trim();
					if (!string.Equals(previousOwner, ownerId, StringComparison.Ordinal))
					{
						throw new InvalidDataException("Operation journal previous ownership does not match the target mod.");
					}
					if (stateCommitted && Directory.Exists(installPath))
					{
						FsHelpers.DeleteOwnedDirectory(previous, operationRoot, previousOwner);
					}
					else
					{
						if (Directory.Exists(installPath) && !FsHelpers.IsOwnedDirectory(installPath, ownerId))
						{
							throw new IOException("Interrupted mod recovery found an unowned current directory.");
						}
						DeleteMarkerOwnedPathIfPresent(installPath, gameRoot, ownerId);
						FsHelpers.EnsureNoReparsePoints(previousPath);
						if (Directory.Exists(previousPath) || File.Exists(previousPath))
						{
							throw new IOException($"Cannot restore interrupted mod because the original path is occupied: {previousPath}");
						}
						Directory.Move(previous, previousPath);
					}
				}
				else if (!commitConfirmed && Directory.Exists(installPath))
				{
					if (FsHelpers.ShouldRemoveUncommittedTarget(
						phase, previousPresent, previousSnapshotExists: false))
					{
						DeleteMarkerOwnedPathIfPresent(installPath, gameRoot, ownerId);
					}
					else if (previousPresent && phase == "filesystem-committed")
					{
						throw new IOException("Interrupted mod recovery is missing the previous snapshot needed for rollback.");
					}
				}

				if (!commitConfirmed)
				{
					RestoreInterruptedModMetadata(
						gameId,
						previousPath,
						operationRoot,
						journalMod);
				}

				if (Directory.Exists(operationRoot))
				{
					FsHelpers.DeleteOwnedDirectory(operationRoot, gameRoot, operationMarker);
				}
			}
			catch (Exception recoveryError)
			{
				Tools.Instance?.AddError($"Could not recover an interrupted mod operation for '{gameId}': {recoveryError.Message}");
			}
		}
	}


	private void RecoverInterruptedModDeletes(string gameId, string gameRoot)
	{
		foreach (var operationRoot in Directory.GetDirectories(gameRoot, ".emustack-delete-*", SearchOption.TopDirectoryOnly))
		{
			try
			{
				FsHelpers.EnsureNoReparseTree(operationRoot);
				var operationMarkerPath = Path.Join(operationRoot, FsHelpers.OwnerMarkerFileName);
				if (!File.Exists(operationMarkerPath))
				{
					continue;
				}

				var operationMarker = File.ReadAllText(operationMarkerPath).Trim();
				if (!operationMarker.StartsWith("operation:", StringComparison.Ordinal) ||
					!FsHelpers.IsOwnedDirectory(operationRoot, operationMarker))
				{
					continue;
				}

				var installPath = File.ReadAllText(Path.Join(operationRoot, "install.path")).Trim();
				var ownerId = File.ReadAllText(Path.Join(operationRoot, "owner")).Trim();
				var modJournalPath = Path.Join(operationRoot, "mod.json");
				var phase = File.ReadAllText(Path.Join(operationRoot, "phase")).Trim();
				if (phase is not ("staging" or "filesystem-committed" or "state-committed" or "rollback"))
				{
					throw new InvalidDataException($"Unknown mod deletion recovery phase: {phase}");
				}
				if (!FsHelpers.IsPathContainedBy(gameRoot, installPath))
				{
					throw new InvalidDataException("Delete journal contains an out-of-root path.");
				}
				var expectedOwnerPrefix = $"{ModPaths.OwnerPrefix}{Globals.Instance.AppMode.Id}:{gameId}:";
				if (!ownerId.StartsWith(expectedOwnerPrefix, StringComparison.Ordinal))
				{
					throw new InvalidDataException("Delete journal owner does not belong to the current provider and title.");
				}

				var quarantine = Path.Join(operationRoot, "quarantine");
				if (phase == "state-committed")
				{
					if (Directory.Exists(quarantine))
					{
						try
						{
							FsHelpers.DeleteOwnedDirectory(quarantine, operationRoot, ownerId);
						}
						catch
						{
							RestoreInterruptedDelete(gameId, installPath, quarantine, ownerId, modJournalPath);
						}
					}
				}
				else if (Directory.Exists(quarantine))
				{
					if (!FsHelpers.IsOwnedDirectory(quarantine, ownerId))
					{
						throw new IOException("Delete journal quarantine ownership does not match the target mod.");
					}
					if (Directory.Exists(installPath))
					{
						throw new IOException($"Cannot restore deleted mod because the original path is occupied: {installPath}");
					}
					Directory.Move(quarantine, installPath);
					RestoreInterruptedDeleteMetadata(gameId, installPath, ownerId, modJournalPath);
				}

				FsHelpers.DeleteOwnedDirectory(operationRoot, gameRoot, operationMarker);
			}
			catch (Exception recoveryError)
			{
				Tools.Instance?.AddError($"Could not recover an interrupted mod deletion for '{gameId}': {recoveryError.Message}");
			}
		}
	}


	private void RestoreInterruptedModMetadata(string gameId, string previousPath, string operationRoot, Mod journalMod)
	{
		var stableId = ModPaths.StableId(journalMod);
		if (!string.IsNullOrEmpty(previousPath))
		{
			Mod restoredMod;
			var previousMetadataPath = Path.Join(operationRoot, "previous.mod.json");
			if (File.Exists(previousMetadataPath))
			{
				FsHelpers.EnsureNoReparsePoints(previousMetadataPath);
				restoredMod = JsonSerializer.Deserialize<Mod>(File.ReadAllText(previousMetadataPath));
				if (restoredMod == null || ModPaths.StableId(restoredMod) != stableId)
				{
					throw new InvalidDataException("Previous mod metadata does not match the operation owner.");
				}
			}
			else
			{
				restoredMod = _installedMods.TryGetValue(gameId, out var currentMods)
					? currentMods.FirstOrDefault(mod => ModPaths.StableId(mod) == stableId)
					: null;
				restoredMod ??= journalMod;
			}

			restoredMod.InstalledPath = previousPath;
			StandardModManagement.RecordInstalledMod(gameId, restoredMod, _installedMods, _browseMods);
		}
		else if (_installedMods.TryGetValue(gameId, out var installedMods))
		{
			installedMods.RemoveAll(mod => mod != null && ModPaths.StableId(mod) == stableId);
		}

		if (!SaveInstalledMods())
		{
			throw new IOException("Failed to persist recovered mod metadata.");
		}
	}


	private void RestoreInterruptedDelete(string gameId, string installPath, string quarantine, string ownerId, string modJournalPath)
	{
		if (!FsHelpers.IsOwnedDirectory(quarantine, ownerId))
		{
			throw new IOException("Delete journal quarantine ownership does not match the target mod.");
		}
		if (Directory.Exists(installPath))
		{
			throw new IOException($"Cannot restore the deleted mod because the original path is occupied: {installPath}");
		}
		Directory.Move(quarantine, installPath);
		if (!FsHelpers.IsOwnedDirectory(installPath, ownerId))
		{
			throw new IOException("Restored mod ownership marker did not match the delete journal.");
		}
		RestoreInterruptedDeleteMetadata(gameId, installPath, ownerId, modJournalPath);
	}


	private void RestoreInterruptedDeleteMetadata(string gameId, string installPath, string ownerId, string modJournalPath)
	{
		if (!File.Exists(modJournalPath))
		{
			throw new InvalidDataException("Delete journal is missing the mod metadata needed for rollback.");
		}

		var restoredMod = JsonSerializer.Deserialize<Mod>(File.ReadAllText(modJournalPath));
		if (restoredMod == null)
		{
			throw new InvalidDataException("Delete journal contains invalid mod metadata.");
		}
		if (!string.Equals(
			ModPaths.OwnerId(Globals.Instance.AppMode.Id, gameId, restoredMod),
			ownerId,
			StringComparison.Ordinal))
		{
			throw new InvalidDataException("Delete journal mod identity does not match its owner.");
		}
		restoredMod.InstalledPath = installPath;
		if (!FsHelpers.IsOwnedDirectory(installPath, ownerId))
		{
			throw new IOException("Restored mod ownership marker did not match the delete journal.");
		}
		StandardModManagement.RecordInstalledMod(gameId, restoredMod, _installedMods, _browseMods);
		RemoveModFromInactiveSource(gameId, restoredMod);
		if (!SaveInstalledMods())
		{
			throw new IOException("Failed to persist the restored mod metadata.");
		}
	}


	private static void DeleteMarkerOwnedPathIfPresent(string path, string parentRoot, string expectedOwner)
	{
		if (!Directory.Exists(path))
		{
			return;
		}
		var marker = Path.Join(path, FsHelpers.OwnerMarkerFileName);
		if (!File.Exists(marker))
		{
			throw new InvalidOperationException($"Recovery refused to delete an unowned directory: {path}");
		}
		var actualOwner = File.ReadAllText(marker).Trim();
		if (!string.Equals(actualOwner, expectedOwner, StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"Recovery refused to delete a directory with unexpected ownership: {path}");
		}
		FsHelpers.DeleteOwnedDirectory(path, parentRoot, actualOwner);
	}


	private bool TryValidateOrMigrateManagedMod(string gameId, Mod mod)
	{
		var gameRoot = ModPaths.GameRoot(ProviderSettings.ModsLocation, gameId);
		if (string.IsNullOrWhiteSpace(mod.InstalledPath) || !Directory.Exists(mod.InstalledPath) ||
		    !FsHelpers.IsPathContainedBy(gameRoot, mod.InstalledPath))
		{
			return false;
		}
		try
		{
			FsHelpers.EnsureNoReparseTree(mod.InstalledPath);
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}

		ModPaths.StableId(mod);
		var ownerId = ModPaths.OwnerId(Globals.Instance.AppMode.Id, gameId, mod);
		if (FsHelpers.IsOwnedDirectory(mod.InstalledPath, ownerId))
		{
			return true;
		}

		// Adopt only legacy online-managed folders that came from a contained index.
		// Local/unmanaged folders remain read-only and never receive an ownership marker.
		var legacyPath = Path.Join(gameRoot, $"Managed{ModPaths.Sanitize(mod.ModName)}");
		var currentPath = ModPaths.ManagedFolder(ProviderSettings.ModsLocation, gameId, mod);
		if (mod.ModUrl != null &&
			(PathsEqual(mod.InstalledPath, legacyPath) || PathsEqual(mod.InstalledPath, currentPath)) &&
			Path.GetFileName(mod.InstalledPath).StartsWith("Managed", StringComparison.Ordinal))
		{
			FsHelpers.MarkOwnedDirectory(mod.InstalledPath, ownerId);
			return true;
		}

		return !Path.GetFileName(mod.InstalledPath).StartsWith("Managed", StringComparison.Ordinal);
	}


	private static bool PathsEqual(string left, string right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return false;
		}
		try
		{
			return string.Equals(FsHelpers.CanonicalPath(left), FsHelpers.CanonicalPath(right),
				OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}


	private static bool IsValidGameId(string gameId) =>
		!string.IsNullOrWhiteSpace(gameId) && Regex.IsMatch(gameId, "^[A-Fa-f0-9]{16}$");


	private async void LoadNextPage()
	{
		if (_isSearchMode || string.IsNullOrEmpty(_currentGameId) || !_hasMorePages || !BeginOperation())
		{
			return;
		}

		try
		{
			var gameId = _currentGameId;
			var currentPage = _modsPageByGame.TryGetValue(gameId, out var loadedPage) ? loadedPage : 1;
			var nextPage = currentPage + 1;
			_loadingPanel.Visible = true;
			DisableInteraction();
			var generation = _operationGeneration;
			RemoveInstalledFromBrowse(gameId);
			var oldModCount = _browseMods.TryGetValue(gameId, out var oldMods) ? oldMods.Count : 0;
			var mods = await _bananaManager.GetAvailableMods(
				gameId, _installedGames, (int)Sources.Banana, nextPage, _lifetimeCancellation.Token);
			if (generation != _operationGeneration || gameId != _currentGameId)
			{
				return;
			}
			_modsPageByGame[gameId] = nextPage;
			AppendBrowseMods(gameId, mods);
			RemoveInstalledFromBrowse(gameId);
			var noNewMods = !_browseMods.TryGetValue(gameId, out var browseList) || browseList.Count <= oldModCount;
			if (noNewMods)
			{
				_exhaustedPages.Add(gameId);
			}
			else
			{
				_exhaustedPages.Remove(gameId);
			}
			_hasMorePages = !noNewMods;
			if (!_lifetimeCancellation.IsCancellationRequested && IsInstanceValid(_gamePickerButton))
			{
				SelectGame(_gamePickerButton.Selected);
			}
			_loadMoreButton.Disabled = noNewMods;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception pageError)
		{
			Tools.Instance?.AddError($"Failed to load more mods for '{_currentGameId}': {pageError.Message}");
		}
		finally
		{
			if (IsInstanceValid(_loadingPanel))
			{
				_loadingPanel.Visible = false;
			}
			EndOperation();
		}
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
				var compatibleVersions = mod.CompatibleVersions ?? new List<string> { "NA" };
				var modIndex = _modList.AddItem($@"  {mod.ModName} || Supports:{string.Join(", ", compatibleVersions)}  ",
					icon: _installedIcon);
				_modList.SetItemMetadata(modIndex, ModKey(mod));
			}
		}

		if (ActiveSourceMods.TryGetValue(gameId, out var sourceMods))
		{
			foreach (var mod in sourceMods)
			{
				var compatibleVersions = mod.CompatibleVersions ?? new List<string> { "NA" };
				var modIndex = _modList.AddItem($@"  {mod.ModName} || Supports:{string.Join(", ", compatibleVersions)}  ");
				_modList.SetItemMetadata(modIndex, ModKey(mod));
			}
		}

		_loadingPanel.Visible = false;
	}


	private void GetTitles(long result, long responseCode, string[] headers, byte[] body)
	{
		if (_titleRequestGeneration != _operationGeneration || _lifetimeCancellation.IsCancellationRequested ||
			!IsInstanceValid(this))
		{
			return;
		}
		// Fall back to the built-in list whether or not the network request succeeded.
		AddBuiltInTitles();

		if (result != (int)HttpRequest.Result.Success || responseCode is < 200 or >= 400)
		{
			Tools.Instance?.AddError($@"cannot retrieve titles, result: {result}, HTTP status: {responseCode}");
			if (IsInstanceValid(_loadingPanel))
			{
				_loadingPanel.Visible = false;
			}
			return;
		}

		foreach (var (gameId, gameName) in ParseTitles(Encoding.UTF8.GetString(body)))
		{
			_titles[gameId] = gameName;
		}
	}


	internal static Dictionary<string, string> ParseTitles(string html)
	{
		var titles = new Dictionary<string, string>();
		if (string.IsNullOrWhiteSpace(html))
		{
			return titles;
		}

		var document = new HtmlDocument();
		document.LoadHtml(html);
		var rows = document.DocumentNode.SelectNodes("//tr");
		if (rows == null)
		{
			return titles;
		}

		foreach (var row in rows)
		{
			var cells = row.SelectNodes("./th|./td");
			if (cells == null)
			{
				continue;
			}

			var values = cells
				.Select(cell => Regex.Replace(HtmlEntity.DeEntitize(cell.InnerText ?? ""), @"\s+", " ").Trim())
				.ToList();
			var idIndex = values.FindIndex(IsValidGameId);
			if (idIndex < 0)
			{
				continue;
			}

			var gameName = values.Skip(idIndex + 1)
				.Select(value => value.Replace("™", "").Trim())
				.FirstOrDefault(value => value.Length > 0);
			if (!string.IsNullOrEmpty(gameName))
			{
				titles[values[idIndex].ToUpperInvariant()] = gameName;
			}
		}

		return titles;
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
		if (!BeginOperation())
		{
			return;
		}
		try
		{
			if (!await Tools.Instance.ConfirmationPopup("Update all mods?"))
			{
				return;
			}

			foreach (var installedGame in _installedGames)
			{
				foreach (var mod in new List<Mod>(_installedMods[installedGame.Key]))
				{
					if (mod.ModUrl == null || !IsManagerOwnedMod(installedGame.Key, mod))
					{
						continue;
					}
					if (!await UpdateMod(installedGame.Key, mod, true))
					{
						Tools.Instance?.AddError($@"failed to update:{mod.ModName}");
						return;
					}
				}
			}
			if (!_lifetimeCancellation.IsCancellationRequested && IsInstanceValid(_gamePickerButton))
			{
				SelectGame(_gamePickerButton.Selected);
			}
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception updateAllError)
		{
			Tools.Instance?.AddError($"Update all mods failed: {updateAllError.Message}");
		}
		finally
		{
			if (IsInstanceValid(_loadingPanel))
			{
				_loadingPanel.Visible = false;
			}
			EndOperation();
		}
	}


	private async Task<bool> UpdateMod(string gameId, Mod mod, bool noConfirmation = false)
	{
		if (!IsManagerOwnedMod(gameId, mod))
		{
			Tools.Instance?.AddError(
				$"'{mod.ModName}' remains read-only because its folder was not installed by EmuStack. Install the online result separately to create a managed copy.");
			return false;
		}
		if (!noConfirmation && !await Tools.Instance.ConfirmationPopup($@"Update {mod.ModName}?"))
		{
			return false;
		}

		var successful = await _standardModManager.UpdateMod(
			gameId, mod, _installedMods, ActiveSourceMods, SaveInstalledMods, _lifetimeCancellation.Token);
		if (successful)
		{
			RemoveModFromInactiveSource(gameId, mod);
		}
		return successful;
	}


	private static bool IsManagerOwnedMod(string gameId, Mod mod)
	{
		return FsHelpers.IsOwnedDirectory(
			mod.InstalledPath,
			ModPaths.OwnerId(Globals.Instance.AppMode.Id, gameId, mod));
	}


	private void SelectGame(int gameIndex)
	{
		if (gameIndex < 0 || gameIndex >= _gamePickerButton.ItemCount)
		{
			return;
		}

		_currentGameId = _gamePickerButton.GetItemMetadata(gameIndex).AsString();
		_hasMorePages = !_exhaustedPages.Contains(_currentGameId);
		if (!_operationInProgress)
		{
			_loadMoreButton.Disabled = _isSearchMode || !_hasMorePages;
		}
		_modList.Clear();

		_sourcePickerButton.Clear();
		AddSources();

		AddMods(_currentGameId);
	}


	private async Task Refresh(Sources source = Sources.Banana)
	{
		ExitSearchMode();
		_modsPageByGame.Clear();
		_exhaustedPages.Clear();
		_hasMorePages = true;
		_operationGeneration++;
		_currentGameId = null;

		_modList.Clear();
		_browseMods.Clear();
		_installedGames.Clear();
		_titles.Clear();

		var selectedGame = _gamePickerButton.Selected;
		_gamePickerButton.Clear();
		_selectedSource = source;

		await GetGamesAndMods(selectedGame);
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
		_loadMoreButton.Disabled = interactionDisabled || _isSearchMode || !_hasMorePages || string.IsNullOrEmpty(_currentGameId);
		_addGameButton.Disabled = interactionDisabled;
		_manualGameIdLineEdit.Editable = !interactionDisabled;
		_searchBar.Editable = !interactionDisabled;
	}


	private bool BeginOperation()
	{
		if (_operationInProgress || _lifetimeCancellation.IsCancellationRequested)
		{
			return false;
		}
		_operationInProgress = true;
		_operationGeneration++;
		DisableInteraction();
		return true;
	}


	private void EndOperation()
	{
		_operationInProgress = false;
		if (IsInstanceValid(this))
		{
			DisableInteraction(false);
		}
	}


	private async Task RunOperation(Func<Task> operation, string failureContext)
	{
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			await operation();
		}
		catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
		{
			// Scene-owned work is canceled during provider switches and shutdown.
		}
		catch (Exception operationError)
		{
			Tools.Instance?.AddError($"{failureContext}: {operationError.Message}");
		}
		finally
		{
			EndOperation();
		}
	}


	private async Task SelectSource(int sourceIndex)
	{
		var sourceId = _sourcePickerButton.GetItemId(sourceIndex);
		if (!Enum.IsDefined(typeof(Sources), sourceId))
		{
			Tools.Instance?.AddError("source not found, please file a bug report. Defaulting back to GameBanana");
			_selectedSource = Sources.Banana;
			_sourcePickerButton.Select(GetSourceButtonIndex(_selectedSource));
			return;
		}

		_selectedSource = (Sources)sourceId;

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
			if (_gamePickerButton.GetItemMetadata(itemIndex).AsString() == gameId)
			{
				return itemIndex;
			}
		}

		return -1;
	}


	private async Task ClearMods(string gameId = "")
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
				if (IsManagerOwnedMod(gameId, mod))
				{
					await DeleteMod(gameId, mod, true);
				}
			}
		}
		catch (Exception deleteError)
		{
			Tools.Instance?.AddError($@"failed to delete mod:{deleteError}");
		}

		if (!_lifetimeCancellation.IsCancellationRequested && IsInstanceValid(_gamePickerButton))
		{
			SelectGame(_gamePickerButton.Selected);
		}
	}


	private void LoadInstalledIndex()
	{
		var loadPath = File.Exists(_installedModsPath)
			? _installedModsPath
			: File.Exists(_legacyInstalledModsPath) ? _legacyInstalledModsPath : null;
		if (loadPath == null)
		{
			_installedMods = new Dictionary<string, List<Mod>>();
			return;
		}

		try
		{
			FsHelpers.EnsureNoReparsePoints(loadPath);
			_installedMods = JsonSerializer.Deserialize<Dictionary<string, List<Mod>>>(File.ReadAllText(loadPath))
			                 ?? new Dictionary<string, List<Mod>>();
			FilterIndexToCurrentProvider();
		}
		catch (Exception indexError)
		{
			var corruptPath = loadPath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
			try
			{
				FsHelpers.EnsureNoReparsePoints(loadPath);
				FsHelpers.EnsureNoReparsePoints(corruptPath);
				File.Copy(loadPath, corruptPath, true);
			}
			catch (Exception quarantineError)
			{
				Tools.Instance?.AddError($"Could not quarantine the corrupt mod index safely: {quarantineError.Message}");
			}
			var backupPath = loadPath + ".bak";
			if (File.Exists(backupPath))
			{
				try
				{
					FsHelpers.EnsureNoReparsePoints(backupPath);
					_installedMods = JsonSerializer.Deserialize<Dictionary<string, List<Mod>>>(File.ReadAllText(backupPath))
					                 ?? new Dictionary<string, List<Mod>>();
					FilterIndexToCurrentProvider();
					Tools.Instance?.AddError($"Recovered the {Globals.Instance.AppMode.Name} mod index from backup; corrupt data was quarantined.");
					return;
				}
				catch (Exception)
				{
					// Preserve both files and rebuild only from owned folder markers below.
				}
			}

			_installedMods = new Dictionary<string, List<Mod>>();
			Tools.Instance?.AddError($"Could not read the {Globals.Instance.AppMode.Name} mod index; corrupt data was quarantined: {indexError.Message}");
		}
	}


	private void FilterIndexToCurrentProvider()
	{
		foreach (var (gameId, mods) in _installedMods.ToList())
		{
			if (!IsValidGameId(gameId))
			{
				_installedMods.Remove(gameId);
				continue;
			}

			var gameRoot = ModPaths.GameRoot(ProviderSettings.ModsLocation, gameId);
			mods?.RemoveAll(mod => mod == null || string.IsNullOrWhiteSpace(mod.InstalledPath) ||
				!FsHelpers.IsPathContainedBy(gameRoot, mod.InstalledPath));
			if (mods == null)
			{
				_installedMods.Remove(gameId);
			}
		}
	}


	private bool SaveInstalledMods()
	{
		if (!_initialized || string.IsNullOrWhiteSpace(_installedModsPath) || _installedModsPath.Contains("://", StringComparison.Ordinal))
		{
			return false;
		}

		try
		{
			var serializedMods = JsonSerializer.Serialize(_installedMods);
			FsHelpers.AtomicWriteText(_installedModsPath, serializedMods);
			return true;
		}
		catch (Exception saveError)
		{
			Tools.Instance?.AddError($"Failed to save the {Globals.Instance.AppMode.Name} mod index: {saveError.Message}");
			return false;
		}
	}


	private async Task<bool> InstallMod(string gameId, Mod mod)
	{
		var successful = await _standardModManager.InstallMod(
			gameId, mod, _installedMods, ActiveSourceMods, SaveInstalledMods, _lifetimeCancellation.Token);
		if (successful)
		{
			_downloadBar.Value = 100;
			RemoveModFromInactiveSource(gameId, mod);
		}
		return successful;
	}


	private async Task<bool> DeleteMod(string gameId, Mod mod, bool noConfirmation = false)
	{
		var successful = await _standardModManager.DeleteMod(
			gameId, mod, _installedMods, ActiveSourceMods, SaveInstalledMods, noConfirmation,
			_lifetimeCancellation.Token);

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
		if (_lifetimeCancellation.IsCancellationRequested)
		{
			return;
		}
		var query = newSearch.Trim();
		var token = ++_searchToken;

		// Debounce: wait before issuing the search so we don't hit the API on
		// every keystroke.
		await ToSignal(GetTree().CreateTimer(SearchDebounceSeconds), "timeout");

		// A newer keystroke arrived while we were waiting — discard this search.
		if (token != _searchToken || _lifetimeCancellation.IsCancellationRequested)
		{
			return;
		}

		try
		{
			await PerformSearch(query);
		}
		catch (OperationCanceledException)
		{
			// Superseded by scene shutdown.
		}
		catch (Exception searchError)
		{
			if (_lifetimeCancellation.IsCancellationRequested || !IsInstanceValid(_loadingPanel))
			{
				return;
			}
			_loadingPanel.Visible = false;
			Tools.Instance?.AddError($"Search failed: {searchError.Message}");
		}
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
		var token = _searchToken;
		var generation = _operationGeneration;
		_searchMods.Clear();
		_loadMoreButton.Disabled = true;
		_loadingPanel.Visible = true;

		try
		{
			var results = await _bananaManager.SearchMods(
				gameId, _installedGames, (int)Sources.Banana, query, _lifetimeCancellation.Token);
			if (token != _searchToken || generation != _operationGeneration || gameId != _currentGameId)
			{
				return;
			}
			_searchMods[gameId] = FilterInstalled(gameId, results);
		}
		catch (Exception ex)
		{
			if (token != _searchToken || generation != _operationGeneration)
			{
				return;
			}
			Tools.Instance?.AddError($"Search failed: {ex.Message}");
			_searchMods[gameId] = new List<Mod>();
		}

		if (token != _searchToken || generation != _operationGeneration || gameId != _currentGameId)
		{
			return;
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
		_hasMorePages = string.IsNullOrEmpty(_currentGameId) || !_exhaustedPages.Contains(_currentGameId);
		_loadMoreButton.Disabled = _operationInProgress || string.IsNullOrEmpty(_currentGameId) || !_hasMorePages;
	}


	private Mod GetSelectedInstalledMod()
	{
		var selectedItems = _modList.GetSelectedItems();
		if (selectedItems.Length == 0 || string.IsNullOrEmpty(_currentGameId))
		{
			return null;
		}

		var selectedKey = _modList.GetItemMetadata(selectedItems[0]).AsString();
		return FindMod(_installedMods, _currentGameId, selectedKey);
	}


	private static Mod FindMod(
		Dictionary<string, List<Mod>> modsByGame,
		string gameId,
		string stableId)
	{
		return gameId != null && modsByGame.TryGetValue(gameId, out var mods)
			? mods.FirstOrDefault(mod => ModKey(mod) == stableId)
			: null;
	}


	private void RefreshSelectedGame()
	{
		if (!_lifetimeCancellation.IsCancellationRequested && IsInstanceValid(_gamePickerButton))
		{
			SelectGame(_gamePickerButton.Selected);
		}
	}


	private async void ModClicked(int modIndex)
	{
		if (!BeginOperation())
		{
			return;
		}
		try
		{
			var selectedKey = _modList.GetItemMetadata(modIndex).AsString();
			var installedMod = FindMod(_installedMods, _currentGameId, selectedKey);
			if (installedMod != null)
			{
				if (!IsManagerOwnedMod(_currentGameId, installedMod))
				{
					Tools.Instance?.AddError($"'{installedMod.ModName}' is unmanaged and read-only. EmuStack will not delete its folder.");
					return;
				}
				await DeleteMod(_currentGameId, installedMod);
				RefreshSelectedGame();
				return;
			}

			var sourceMod = FindMod(ActiveSourceMods, _currentGameId, selectedKey);
			if (sourceMod != null)
			{
				await InstallMod(_currentGameId, sourceMod);
				RefreshSelectedGame();
			}
		}
		catch (Exception modActionError)
		{
			Tools.Instance?.AddError($"Mod action failed: {modActionError.Message}");
		}
		finally
		{
			EndOperation();
		}
	}


	private async void UpdateSelectedPressed()
	{
		if (!BeginOperation())
		{
			return;
		}
		try
		{
			var mod = GetSelectedInstalledMod();
			if (mod == null)
			{
				return;
			}

			if (mod.ModUrl != null)
			{
				await UpdateMod(_currentGameId, mod);
			}
			else
			{
				// Linking records the remote source but deliberately leaves the local
				// folder unmanaged and read-only. The online result can then be installed
				// separately as an EmuStack-owned copy.
				await LinkLocalModOnline(_currentGameId, mod);
			}

			RefreshSelectedGame();
		}
		catch (Exception updateError)
		{
			Tools.Instance?.AddError($"Selected mod update failed: {updateError.Message}");
		}
		finally
		{
			EndOperation();
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

		var match = await _bananaManager.FindOnlineSource(
			gameId, _installedGames, mod.ModName, (int)Sources.Banana, _lifetimeCancellation.Token);
		if (match == null)
		{
			Tools.Instance?.AddError($"Could not find an unambiguous online source for '{mod.ModName}'.");
			return;
		}

		var oldId = mod.ModId;
		var oldUrl = mod.ModUrl;
		var oldSource = mod.Source;
		var oldVersions = mod.CompatibleVersions;
		mod.ModId = match.ModId;
		mod.ModUrl = match.ModUrl;
		mod.Source = match.Source;
		if (match.CompatibleVersions != null && match.CompatibleVersions.Count > 0)
		{
			mod.CompatibleVersions = match.CompatibleVersions;
		}

		if (!SaveInstalledMods())
		{
			mod.ModId = oldId;
			mod.ModUrl = oldUrl;
			mod.Source = oldSource;
			mod.CompatibleVersions = oldVersions;
			throw new IOException("Failed to persist linked mod metadata.");
		}
	}


	private async void LinkOnlinePressed()
	{
		if (!BeginOperation())
		{
			return;
		}
		try
		{
			if (_currentGameId == null)
			{
				return;
			}

			var mod = GetSelectedInstalledMod();
			if (mod == null)
			{
				Tools.Instance?.AddError("Select an installed local mod first.");
				return;
			}

			if (mod.ModUrl != null)
			{
				Tools.Instance?.AddError($"'{mod.ModName}' is already linked to an online source.");
				return;
			}

			await LinkLocalModOnline(_currentGameId, mod);
			RefreshSelectedGame();
		}
		catch (Exception linkError)
		{
			Tools.Instance?.AddError($"Mod linking failed: {linkError.Message}");
		}
		finally
		{
			EndOperation();
		}
	}


	private async void ModLocationPressed()
	{
		if (!BeginOperation())
		{
			return;
		}
		try
		{
			var picked = await Tools.Instance.PickFolder(ProviderSettings.ModsLocation);
			if (picked != null && !_lifetimeCancellation.IsCancellationRequested)
			{
				var previousLocation = ProviderSettings.ModsLocation;
				ProviderSettings.ModsLocation = picked;
				if (!EnsureModsLocation())
				{
					ProviderSettings.ModsLocation = previousLocation;
					throw new IOException("The selected mod location is not usable.");
				}
				if (!Globals.Instance.SyncCurrentProviderSettings())
				{
					ProviderSettings.ModsLocation = previousLocation;
					Globals.Instance.SyncCurrentProviderSettings();
					throw new IOException("Failed to persist the mod location.");
				}
			}
			if (_lifetimeCancellation.IsCancellationRequested)
			{
				return;
			}

			_modLocationButton.Text = PadLabel(ProviderSettings.ModsLocation ?? "");
			await Refresh(_selectedSource);
		}
		catch (Exception locationError)
		{
			Tools.Instance?.AddError($"Failed to change the mod location: {locationError.Message}");
		}
		finally
		{
			EndOperation();
		}
	}


	private async void RefreshPressed() =>
		await RunOperation(() => Refresh(_selectedSource), "Refresh failed");


	private void UpdateDownloadProgress()
	{
		var bodySize = (long)_downloadRequester.GetBodySize();
		var downloadedBytes = (long)_downloadRequester.GetDownloadedBytes();
		if (bodySize > SafeArchive.MaximumDownloadBytes || downloadedBytes > SafeArchive.MaximumDownloadBytes)
		{
			_downloadRequester.CancelRequest();
			return;
		}

		if (bodySize <= 0)
		{
			return;
		}

		_downloadBar.Value = (float)downloadedBytes / bodySize * 100;
	}


	private async void SourceSelected(int selectedSource) =>
		await RunOperation(() => SelectSource(selectedSource), "Failed to change mod source");


	private async void AddGamePressed() =>
		await RunOperation(() => AddGameFromInput(_manualGameIdLineEdit.Text), "Failed to add game");


	private async void AddGameTextSubmitted(string gameId) =>
		await RunOperation(() => AddGameFromInput(gameId), "Failed to add game");


	private async void ClearModsPressed()
	{
		if (!BeginOperation())
		{
			return;
		}
		try
		{
			if (!await Tools.Instance.ConfirmationPopup("Remove all manager-owned mods for selected title?"))
			{
				return;
			}
			await ClearMods();
		}
		catch (OperationCanceledException)
		{
			// Scene-owned work is canceled during provider switches and shutdown.
		}
		catch (Exception clearError)
		{
			Tools.Instance?.AddError($"Failed to clear mods: {clearError.Message}");
		}
		finally
		{
			EndOperation();
		}
	}


	private static string PadLabel(string text) => text.PadLeft(text.Length + LabelLeftPadding, ' ');
}
