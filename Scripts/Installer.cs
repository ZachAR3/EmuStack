using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mono.Unix;
using Label = Godot.Label;
using CancellationTokenSource = System.Threading.CancellationTokenSource;


public partial class Installer : Control
{
	[ExportGroup("General")]
	[Export] private Label _latestVersionLabel;

	[ExportGroup("Installer")]
	[Export] private Image _icon;
	[Export] private OptionButton _versionButton;
	[Export] private CheckBox _createShortcutButton;
	[Export] private CheckBox _autoUpdateButton;
	[Export] private LineEdit _executableNameLineEdit;
	[Export] private Button _installLocationButton;
	[Export] private Button _downloadButton;
	[Export] private Panel _downloadWindow;
	[Export] private Label _downloadLabel;
	[Export] private Timer _downloadUpdateTimer;
	[Export] private ProgressBar _downloadProgressBar;
	[Export] private CheckBox _autoUnpackButton;
	[Export] private CheckBox _customVersionCheckBox;
	[Export] private LineEdit _customVersionLineEdit;
	[Export] private HttpRequest _downloadRequester;
	[Export] private TextureRect _extractWarning;
	[Export] private TextureRect _downloadWarning;

	private readonly OsKind _os = Os.Current;
	private string _executableName;
	private string _downloadFileName;
	private ProviderRelease _latestRelease;
	private ProviderRelease _activeDownloadRelease;
	private List<ProviderRelease> _availableReleases = new();
	private bool _autoUpdate;
	private bool _installInProgress;
	private string _operationRoot;
	private string _operationOwner;
	private string _downloadPath;
	private bool _preserveOperationForRecovery;
	private readonly CancellationTokenSource _lifetimeCancellation = new();
	private bool _exiting;

	private Mode AppMode => Globals.Instance.AppMode;
	private SettingsResource Settings => Globals.Instance.Settings;
	private ProviderSettingsResource ProviderSettings => Globals.Instance.CurrentProviderSettings;


	public override void _ExitTree()
	{
		_exiting = true;
		_lifetimeCancellation.Cancel();
		if (_installInProgress)
		{
			_downloadRequester?.CancelRequest();
		}
	}


	private void Initiate()
	{
		RecoverInterruptedInstallOperations();
		_downloadRequester.Timeout = 60;
		_createShortcutButton.Disabled = false;
		_autoUpdateButton.Disabled = !_createShortcutButton.ButtonPressed || !AppMode.SupportsAutoUnpack(_os);
		_autoUnpackButton.Disabled = !AppMode.SupportsAutoUnpack(_os);

		_executableName = string.IsNullOrEmpty(ProviderSettings.ExecutableName)
			? AppMode.DefaultExecutableName
			: ProviderSettings.ExecutableName;
		_executableNameLineEdit.Text = _executableName;
		_installLocationButton.Text = ProviderSettings.SaveDirectory;
		_downloadButton.Disabled = true;
		_downloadWindow.Visible = false;
		_customVersionLineEdit.Editable = false;
		_customVersionCheckBox.Disabled = !AppMode.SupportsCustomVersions;
		_extractWarning.Visible = false;
		_downloadWarning.Visible = false;

		AddVersions();
	}


	private async void InstallSelectedVersion()
	{
		if (_installInProgress)
		{
			return;
		}
		_installInProgress = true;
		DisableDownloadControls();

		try
		{
			if (!await Tools.Instance.ConfirmationPopup())
			{
				ResetDownloadControls();
				return;
			}

			string selectedVersion;
			if (_customVersionCheckBox.ButtonPressed)
			{
				if (!AppMode.SupportsCustomVersions)
				{
					Tools.Instance?.AddError($"{AppMode.Name} does not support custom version downloads.");
					ResetDownloadControls();
					return;
				}

				if (!AppMode.IsValidCustomVersion(_customVersionLineEdit.Text))
				{
					Tools.Instance?.AddError("Invalid version selected, please enter a valid version number.");
					ResetDownloadControls();
					return;
				}
				selectedVersion = _customVersionLineEdit.Text.Trim();
			}
			else
			{
				selectedVersion = _versionButton.GetItemMetadata(_versionButton.Selected).AsString();
			}
			await InstallVersion(selectedVersion);
		}
		catch (OperationCanceledException) when (_exiting)
		{
			return;
		}
		catch (Exception installError)
		{
			Tools.Instance?.AddError($"Failed to start {AppMode.Name} install: {installError.Message}");
			CleanupInstallOperation();
			ResetDownloadControls();
		}
	}


	private async Task InstallVersion(string version)
	{
		if (!_installInProgress)
		{
			_installInProgress = true;
			DisableDownloadControls();
		}
		// The list of releases was already fetched by AddVersions; use the cached
		// release when possible so the user isn't left waiting before the download
		// progress appears. Fall back to GetRelease for custom versions.
		_activeDownloadRelease = FindReleaseInCache(version) ?? await AppMode.GetRelease(
			version,
			Globals.Instance.HttpClient,
			_os,
			ProviderSettings.ReleaseChannel,
			_lifetimeCancellation.Token);
		if (_exiting)
		{
			return;
		}

		if (_activeDownloadRelease == null || string.IsNullOrEmpty(_activeDownloadRelease.DownloadUrl))
		{
			Tools.Instance?.AddError($"Unable to find {AppMode.Name} release: {version}");
			ResetDownloadControls();
			return;
		}
		if (!AppMode.IsValidCustomVersion(_activeDownloadRelease.Version))
		{
			throw new InvalidDataException("The provider returned an invalid release version.");
		}

		if (!string.IsNullOrEmpty(ProviderSettings.InstalledVersion))
		{
			var installedIndex = GetVersionButtonIndex(ProviderSettings.InstalledVersion);
			if (installedIndex >= 0)
			{
				_versionButton.SetItemDisabled(installedIndex, false);
			}
		}

		_downloadFileName = AppMode.GetDownloadFileName(_activeDownloadRelease, _os, _executableName);
		ValidateFileName(_downloadFileName, "download filename");
		ValidateHttpUrl(_activeDownloadRelease.DownloadUrl);
		ValidateExecutableName(_executableName);
		_downloadLabel.Text = "Downloading...";
		_downloadWindow.Visible = true;
		_downloadLabel.GrabFocus();

		PrepareInstallOperation();
		_downloadRequester.DownloadFile = _downloadPath;
		var requestError = _downloadRequester.Request(_activeDownloadRelease.DownloadUrl);
		if (requestError != Error.Ok)
		{
			Tools.Instance?.AddError($"Failed to start download for {AppMode.Name}: {requestError}");
			CleanupInstallOperation();
			ResetDownloadControls();
			return;
		}

		_downloadUpdateTimer.Start();
	}


	private async void VersionDownloadCompleted(long result, long responseCode, string[] headers, byte[] body)
	{
		if (_exiting)
		{
			return;
		}
		try
		{
			_downloadUpdateTimer.Stop();
			if (result != (int)HttpRequest.Result.Success || responseCode is < 200 or >= 300 ||
			    string.IsNullOrEmpty(_downloadPath) || !File.Exists(_downloadPath) ||
			    new FileInfo(_downloadPath).Length is <= 0 or > SafeArchive.MaximumDownloadBytes)
			{
				throw new IOException($"Download failed (result {result}, HTTP {responseCode}).");
			}
			FsHelpers.EnsureNoReparsePoints(_downloadPath);

			if (!ShouldDeployDownload())
			{
				CommitManualDownload();
				_downloadLabel.Text = "Download saved (not installed).";
			}
			else
			{
				await CommitManagedInstall();
				_downloadLabel.Text = "Successfully installed!";
				AddInstalledVersion();

				if (_createShortcutButton.ButtonPressed)
				{
					CreateShortcut();
				}

				if (Settings.LauncherMode)
				{
					Tools.Instance.LaunchEmulator();
				}
			}

			_downloadProgressBar.Value = 100;
		}
		catch (Exception installError)
		{
			if (!_exiting)
			{
				_downloadProgressBar.Value = 0;
				Tools.Instance?.AddError($"Failed to install {AppMode.Name} '{_activeDownloadRelease?.Version}': {installError.Message}");
			}
		}
		finally
		{
			if (!_exiting)
			{
				CleanupInstallOperation();
				ResetDownloadControls();
			}
		}

		// Let the user see the success message before hiding the overlay.
		// Guard against the node being freed during the wait (e.g. provider switch).
		if (_exiting)
		{
			return;
		}
		await ToSignal(GetTree().CreateTimer(2), "timeout");
		if (IsInstanceValid(this) && IsInstanceValid(_downloadWindow))
		{
			_downloadWindow.Visible = false;
		}
	}


	private void UpdateDownloadBar()
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

		_downloadProgressBar.Value =
			(float)downloadedBytes / bodySize * 100;
	}


	private void ResetDownloadControls()
	{
		_installInProgress = false;
		_customVersionCheckBox.Disabled = !AppMode.SupportsCustomVersions;
		_executableNameLineEdit.Editable = true;
		_createShortcutButton.Disabled = false;
		_autoUnpackButton.Disabled = !AppMode.SupportsAutoUnpack(_os);
		_autoUpdateButton.Disabled = !_createShortcutButton.ButtonPressed || !AppMode.SupportsAutoUnpack(_os);
		_downloadButton.Disabled = false;
		_installLocationButton.Disabled = false;
		_versionButton.Disabled = false;
	}


	private void DisableDownloadControls()
	{
		_customVersionCheckBox.Disabled = true;
		_executableNameLineEdit.Editable = false;
		_createShortcutButton.Disabled = true;
		_autoUnpackButton.Disabled = true;
		_autoUpdateButton.Disabled = true;
		_versionButton.Disabled = true;
		_downloadButton.Disabled = true;
		_installLocationButton.Disabled = true;
	}


	private void CreateShortcut()
	{
		var executable = _autoUpdate ? OS.GetExecutablePath() : ProviderSettings.ExecutablePath;
		var launcherFlag = _autoUpdate ? "--launcher" : null;

		var isMacApp = _os == OsKind.MacOS && Directory.Exists(executable) &&
			executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase);
		if (!File.Exists(executable) && !isMacApp)
		{
			Tools.Instance?.AddError("No executable path found, shortcut creation failed... Please contact a developer...");
			return;
		}

		ShortcutFactory.Create(AppMode, _os, executable, launcherFlag, ProviderSettings.SaveDirectory, _icon);
	}


	private async void AddVersions()
	{
		try
		{
			_versionButton.Clear();
			_availableReleases = await AppMode.GetAvailableReleases(
				Globals.Instance.HttpClient,
				_os,
				ProviderSettings.ReleaseChannel,
				_lifetimeCancellation.Token);
			if (_exiting)
			{
				return;
			}
			_latestRelease = _availableReleases.FirstOrDefault();
			if (_latestRelease == null)
			{
				Tools.Instance?.AddError($"Unable to fetch latest {AppMode.Name} release.");
				return;
			}

			_latestVersionLabel.Text = $"Latest: {_latestRelease.DisplayName}";

			foreach (var release in _availableReleases)
			{
				_versionButton.AddItem(release.DisplayName);
				_versionButton.SetItemMetadata(_versionButton.ItemCount - 1, release.Version);
			}

			if (!string.IsNullOrEmpty(ProviderSettings.InstalledVersion))
			{
				AddInstalledVersion();
			}

			_downloadButton.Disabled = false;

			if (Settings.LauncherMode)
			{
				if (_latestRelease.Version != ProviderSettings.InstalledVersion)
				{
					await InstallVersion(_latestRelease.Version);
				}
				else
				{
					Tools.Instance.LaunchEmulator();
				}
			}
		}
		catch (Exception versionPullException)
		{
			if (_exiting)
			{
				return;
			}
			_latestVersionLabel.Text = "Latest: unavailable";
			_downloadButton.Disabled = true;
			if (_installInProgress)
			{
				CleanupInstallOperation();
				ResetDownloadControls();
			}
			Tools.Instance?.AddError($"Failed to get {AppMode.Name} versions: {versionPullException.Message}");
		}
	}


	private void AddInstalledVersion()
	{
		var installedVersion = ProviderSettings.InstalledVersion;
		var selectedIndex = GetVersionButtonIndex(installedVersion);
		_customVersionLineEdit.Text = installedVersion;

		if (selectedIndex >= 0)
		{
			_versionButton.Selected = selectedIndex;
		}
		else
		{
			_versionButton.AddItem(installedVersion);
			selectedIndex = _versionButton.ItemCount - 1;
			_versionButton.SetItemMetadata(selectedIndex, installedVersion);
			_versionButton.Selected = selectedIndex;
		}

		_versionButton.SetItemDisabled(selectedIndex, true);
	}


	private ProviderRelease FindReleaseInCache(string version)
	{
		return _availableReleases?.FirstOrDefault(release =>
			string.Equals(release.Version, version, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(release.DisplayName, version, StringComparison.OrdinalIgnoreCase));
	}


	private int GetVersionButtonIndex(string version)
	{
		for (var itemIndex = 0; itemIndex < _versionButton.ItemCount; itemIndex++)
		{
			if (_versionButton.GetItemMetadata(itemIndex).AsString() == version)
			{
				return itemIndex;
			}
		}

		return -1;
	}


	private bool ShouldDeployDownload()
	{
		return AppMode.IsSingleFileDownload(_os) ||
			(AppMode.SupportsAutoUnpack(_os) && (_autoUnpackButton.ButtonPressed || _autoUpdate));
	}


	private string InstallOwnerId => $"install:{AppMode.Id}";


	private void PrepareInstallOperation()
	{
		_preserveOperationForRecovery = false;
		var installPath = ValidateInstallDirectory(ProviderSettings.SaveDirectory);

		var parent = Path.GetDirectoryName(installPath) ?? throw new InvalidOperationException("Install directory has no parent.");
		Directory.CreateDirectory(parent);
		FsHelpers.EnsureNoReparsePoints(parent);

		_operationOwner = $"operation:{Guid.NewGuid():N}";
		_operationRoot = Path.Join(parent, $".{Path.GetFileName(installPath)}-emustack-{Guid.NewGuid():N}");
		FsHelpers.EnsureNoReparsePoints(_operationRoot);
		Directory.CreateDirectory(_operationRoot);
		FsHelpers.MarkOwnedDirectory(_operationRoot, _operationOwner);
		FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "install.path"), installPath);
		FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "owner"), InstallOwnerId);
		FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "previous.version"), ProviderSettings.InstalledVersion ?? "");
		FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "previous.executable"), ProviderSettings.ExecutablePath ?? "");
		FsHelpers.AtomicWriteText(
			Path.Join(_operationRoot, "previous.present"),
			Directory.Exists(installPath) && Directory.EnumerateFileSystemEntries(installPath).Any()
				? "true"
				: "false");
		FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "phase"), "staging");
		_downloadPath = Path.Join(_operationRoot, "download.part");
		FsHelpers.EnsureNoReparsePoints(_downloadPath);
	}


	private string ValidateInstallDirectory(string path)
	{
		var installPath = FsHelpers.CanonicalPath(path);
		FsHelpers.RejectDangerousRoot(installPath);
		var parent = Path.GetDirectoryName(installPath) ?? throw new InvalidOperationException("Install directory has no parent.");
		FsHelpers.EnsureNoReparsePoints(parent);
		if (Directory.Exists(installPath))
		{
			FsHelpers.EnsureNoReparseTree(installPath);
			if (Directory.EnumerateFileSystemEntries(installPath).Any() &&
				!FsHelpers.IsOwnedDirectory(installPath, InstallOwnerId))
			{
				throw new InvalidOperationException("The selected install directory is non-empty and is not owned by EmuStack. Choose an empty directory or an existing EmuStack install.");
			}
		}
		return installPath;
	}


	private void CommitManualDownload()
	{
		var installPath = FsHelpers.CanonicalPath(ProviderSettings.SaveDirectory);
		Directory.CreateDirectory(installPath);
		FsHelpers.EnsureNoReparsePoints(installPath);
		FsHelpers.EnsureNoReparsePoints(_downloadPath);
		var finalPath = Path.Join(installPath, _downloadFileName);
		FsHelpers.EnsureNoReparsePoints(finalPath);
		if (!FsHelpers.IsPathContainedBy(installPath, finalPath) || File.Exists(finalPath))
		{
			throw new IOException($"Refusing to overwrite an existing download: {finalPath}");
		}
		File.Move(_downloadPath, finalPath);
	}


	private async Task CommitManagedInstall()
	{
		var installPath = FsHelpers.CanonicalPath(ProviderSettings.SaveDirectory);
		var deployPath = Path.Join(_operationRoot, "deploy");
		var backupPath = Path.Join(_operationRoot, "previous");
		FsHelpers.EnsureNoReparsePoints(deployPath);
		Directory.CreateDirectory(deployPath);
		string stagedExecutable;

		if (AppMode.IsSingleFileDownload(_os))
		{
			FsHelpers.EnsureNoReparsePoints(_downloadPath);
			var stagedDownload = Path.Join(deployPath, _downloadFileName);
			File.Move(_downloadPath, stagedDownload);
			stagedExecutable = stagedDownload;
			if (_os == OsKind.Linux)
			{
				SetUserExecutable(stagedDownload);
			}
		}
		else
		{
			await Task.Run(
				() => SafeArchive.Extract(_downloadPath, deployPath, _lifetimeCancellation.Token),
				_lifetimeCancellation.Token);
			File.Delete(_downloadPath);
			foreach (var folderName in AppMode.GetExtractedDirectoriesToFlatten(_activeDownloadRelease, _os))
			{
				ValidateFileName(folderName, "archive folder");
				FsHelpers.MoveFilesAndDirs(Path.Join(deployPath, folderName), deployPath);
			}

			FsHelpers.EnsureNoReparseTree(deployPath);
			stagedExecutable = AppMode.FindExecutable(deployPath, _os, _executableName);
			if (string.IsNullOrEmpty(stagedExecutable))
			{
				throw new InvalidDataException($"Unable to find the {AppMode.Name} executable after extraction.");
			}
			if (!FsHelpers.IsPathContainedBy(deployPath, stagedExecutable))
			{
				throw new InvalidDataException("The extracted executable is outside the deployment directory.");
			}
		}
		stagedExecutable = RenameExecutableIfRequested(stagedExecutable);
		if (_os == OsKind.Linux && File.Exists(stagedExecutable))
		{
			SetUserExecutable(stagedExecutable);
		}
		if (_os == OsKind.MacOS && Directory.Exists(stagedExecutable) &&
			stagedExecutable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
		{
			SetMacAppExecutables(stagedExecutable);
		}
		FsHelpers.MarkOwnedDirectory(deployPath, InstallOwnerId);

		var oldVersion = ProviderSettings.InstalledVersion;
		var oldExecutable = ProviderSettings.ExecutablePath;
		var committed = false;
		try
		{
			FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "phase"), "swapping");
			if (Directory.Exists(installPath))
			{
				if (Directory.EnumerateFileSystemEntries(installPath).Any())
				{
					if (!FsHelpers.IsOwnedDirectory(installPath, InstallOwnerId))
					{
						throw new InvalidOperationException("The existing installation is not owned by EmuStack.");
					}
					Directory.Move(installPath, backupPath);
				}
				else
				{
					Directory.Delete(installPath);
				}
			}

			var relativeExecutable = Path.GetRelativePath(deployPath, stagedExecutable);
			Directory.Move(deployPath, installPath);
			committed = true;
			FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "phase"), "filesystem-committed");
			ProviderSettings.InstalledVersion = _activeDownloadRelease.Version;
			ProviderSettings.ExecutablePath = Path.Join(installPath, relativeExecutable);
			if (!Globals.Instance.SyncCurrentProviderSettings())
			{
				throw new IOException("Failed to persist installed version state.");
			}
			FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "phase"), "state-committed");

			if (Directory.Exists(backupPath))
			{
				try
				{
					if (!FsHelpers.IsOwnedDirectory(installPath, InstallOwnerId))
					{
						throw new IOException("The committed install directory is no longer owned by EmuStack.");
					}
					FsHelpers.DeleteOwnedDirectory(backupPath, _operationRoot, InstallOwnerId);
				}
				catch (Exception cleanupError)
				{
					_preserveOperationForRecovery = true;
					Tools.Instance?.AddError($"{AppMode.Name} update cleanup is pending: {cleanupError.Message}");
				}
			}
		}
		catch
		{
			if (!TryBeginInstallRollback())
			{
				_preserveOperationForRecovery = true;
				throw;
			}
			if (!TryRollbackInstallFilesystem(committed, installPath, backupPath))
			{
				_preserveOperationForRecovery = true;
				throw;
			}
			ProviderSettings.InstalledVersion = oldVersion;
			ProviderSettings.ExecutablePath = oldExecutable;
			if (!Globals.Instance.SyncCurrentProviderSettings())
			{
				_preserveOperationForRecovery = true;
				throw new IOException("Failed to persist rolled-back emulator install state.");
			}
			throw;
		}
	}


	private bool TryBeginInstallRollback()
	{
		try
		{
			if (string.IsNullOrEmpty(_operationRoot) || string.IsNullOrEmpty(_operationOwner) ||
				!Directory.Exists(_operationRoot) || !FsHelpers.IsOwnedDirectory(_operationRoot, _operationOwner))
			{
				return false;
			}
			FsHelpers.AtomicWriteText(Path.Join(_operationRoot, "phase"), "rollback");
			return true;
		}
		catch
		{
			return false;
		}
	}


	private bool TryRollbackInstallFilesystem(bool committed, string installPath, string backupPath)
	{
		try
		{
			if (committed && Directory.Exists(installPath))
			{
				if (!FsHelpers.IsOwnedDirectory(installPath, InstallOwnerId))
				{
					throw new IOException("Refusing to roll back an unowned install directory.");
				}
				FsHelpers.DeleteOwnedDirectory(installPath, Path.GetDirectoryName(installPath), InstallOwnerId);
			}
			if (Directory.Exists(backupPath))
			{
				if (!FsHelpers.IsOwnedDirectory(backupPath, InstallOwnerId))
				{
					throw new IOException("Refusing to restore an unowned install backup.");
				}
				if (Directory.Exists(installPath))
				{
					throw new IOException("Cannot restore the previous install because the target is occupied.");
				}
				Directory.Move(backupPath, installPath);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}


	private void RecoverInterruptedInstallOperations()
	{
		try
		{
			var installPath = FsHelpers.CanonicalPath(ProviderSettings.SaveDirectory);
			var parent = Path.GetDirectoryName(installPath);
			if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
			{
				return;
			}

			foreach (var operationRoot in Directory.GetDirectories(
				parent, $".{Path.GetFileName(installPath)}-emustack-*", SearchOption.TopDirectoryOnly))
			{
				FsHelpers.EnsureNoReparseTree(operationRoot);
				var operationMarker = File.ReadAllText(Path.Join(operationRoot, FsHelpers.OwnerMarkerFileName)).Trim();
				if (!operationMarker.StartsWith("operation:", StringComparison.Ordinal))
				{
					continue;
				}
				var operationTargetOwner = File.ReadAllText(Path.Join(operationRoot, "owner")).Trim();
				if (!string.Equals(operationTargetOwner, InstallOwnerId, StringComparison.Ordinal))
				{
					throw new InvalidDataException("Installer operation journal belongs to another install target.");
				}
				var journalInstallPath = File.ReadAllText(Path.Join(operationRoot, "install.path")).Trim();
				if (!string.Equals(FsHelpers.CanonicalPath(journalInstallPath), installPath,
					OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
				{
					throw new InvalidDataException("Installer operation journal targets another directory.");
				}
				var phase = File.ReadAllText(Path.Join(operationRoot, "phase")).Trim();
				if (phase is not ("staging" or "swapping" or "filesystem-committed" or "state-committed" or "rollback"))
				{
					throw new InvalidDataException($"Unknown installer recovery phase: {phase}");
				}
				var previousVersion = File.ReadAllText(Path.Join(operationRoot, "previous.version")).Trim();
				var previousExecutable = File.ReadAllText(Path.Join(operationRoot, "previous.executable")).Trim();
				bool? previousPresent = null;
				var previousPresentPath = Path.Join(operationRoot, "previous.present");
				if (File.Exists(previousPresentPath))
				{
					if (!bool.TryParse(File.ReadAllText(previousPresentPath).Trim(), out var parsedPreviousPresent))
					{
						throw new InvalidDataException("Installer recovery journal has an invalid previous-target flag.");
					}
					previousPresent = parsedPreviousPresent;
				}
				if (!string.IsNullOrWhiteSpace(previousExecutable))
				{
					previousExecutable = FsHelpers.CanonicalPath(previousExecutable);
					if (!FsHelpers.IsPathContainedBy(installPath, previousExecutable))
					{
						throw new InvalidDataException("Installer recovery journal contains an executable outside the install directory.");
					}
					FsHelpers.EnsureNoReparsePoints(previousExecutable);
				}
				var previous = Path.Join(operationRoot, "previous");
				var stateCommitted = phase == "state-committed";
				var commitConfirmed = stateCommitted && Directory.Exists(installPath);
				if (commitConfirmed && !FsHelpers.IsOwnedDirectory(installPath, InstallOwnerId))
				{
					throw new IOException("Interrupted install recovery found an unowned current directory.");
				}
				if (Directory.Exists(previous))
				{
					if (!FsHelpers.IsOwnedDirectory(previous, InstallOwnerId))
					{
						throw new IOException("Interrupted install recovery found an unowned previous directory.");
					}
					if (commitConfirmed)
					{
						FsHelpers.DeleteOwnedDirectory(previous, operationRoot, InstallOwnerId);
					}
					else
					{
						if (Directory.Exists(installPath))
						{
							if (!FsHelpers.IsOwnedDirectory(installPath, InstallOwnerId))
							{
								throw new IOException("Interrupted install recovery found an unowned current directory.");
							}
							FsHelpers.DeleteOwnedDirectory(installPath, parent, InstallOwnerId);
						}
						if (!Directory.Exists(installPath))
						{
							Directory.Move(previous, installPath);
						}
					}
				}
				else if (!commitConfirmed && Directory.Exists(installPath))
				{
					if (previousPresent == false && FsHelpers.ShouldRemoveUncommittedTarget(
						phase, hadPreviousTarget: false, previousSnapshotExists: false))
					{
						if (!FsHelpers.IsOwnedDirectory(installPath, InstallOwnerId))
						{
							throw new IOException("Interrupted install recovery found an unowned new directory.");
						}
						FsHelpers.DeleteOwnedDirectory(installPath, parent, InstallOwnerId);
					}
					else if ((previousPresent == null && phase is "swapping" or "filesystem-committed" or "rollback") ||
					         (previousPresent == true && phase == "filesystem-committed"))
					{
						throw new IOException("Interrupted install recovery cannot distinguish the current directory from the missing previous snapshot.");
					}
				}

				if (!commitConfirmed)
				{
					ProviderSettings.InstalledVersion = previousVersion;
					ProviderSettings.ExecutablePath = previousExecutable;
					if (!Globals.Instance.SyncCurrentProviderSettings())
					{
						throw new IOException("Could not persist recovered emulator install state.");
					}
				}

				if (Directory.Exists(operationRoot))
				{
					FsHelpers.DeleteOwnedDirectory(operationRoot, parent, operationMarker);
				}
			}
		}
		catch (Exception recoveryError)
		{
			Tools.Instance?.AddError($"Could not recover an interrupted {AppMode.Name} installation: {recoveryError.Message}");
		}
	}


	private void CleanupInstallOperation()
	{
		_downloadRequester.DownloadFile = "";
		if (!_preserveOperationForRecovery && !string.IsNullOrEmpty(_operationRoot) && Directory.Exists(_operationRoot) &&
		    FsHelpers.IsOwnedDirectory(_operationRoot, _operationOwner))
		{
			try
			{
				FsHelpers.DeleteOwnedDirectory(_operationRoot, Path.GetDirectoryName(_operationRoot), _operationOwner);
			}
			catch (Exception cleanupError)
			{
				Tools.Instance?.AddError($"Installer staging cleanup failed: {cleanupError.Message}");
			}
		}
		_operationRoot = null;
		_operationOwner = null;
		_downloadPath = null;
		_preserveOperationForRecovery = false;
	}


	private string RenameExecutableIfRequested(string executablePath)
	{
		if (string.IsNullOrEmpty(_executableName) || Directory.Exists(executablePath))
		{
			return executablePath;
		}

		var extension = Path.GetExtension(executablePath);
		var requestedExecutablePath = Path.Join(Path.GetDirectoryName(executablePath), $"{_executableName}{extension}");
		if (string.Equals(executablePath, requestedExecutablePath, StringComparison.OrdinalIgnoreCase) ||
		    File.Exists(requestedExecutablePath))
		{
			return executablePath;
		}

		File.Move(executablePath, requestedExecutablePath);
		return requestedExecutablePath;
	}


	private static void SetUserExecutable(string executablePath)
	{
		_ = new UnixFileInfo(executablePath)
		{
			FileAccessPermissions = FileAccessPermissions.UserReadWriteExecute,
		};
	}


	private static void SetMacAppExecutables(string appPath)
	{
		var executableDirectory = Path.Join(appPath, "Contents", "MacOS");
		FsHelpers.EnsureNoReparseTree(executableDirectory);
		var executableFiles = Directory.GetFiles(executableDirectory, "*", SearchOption.AllDirectories);
		if (executableFiles.Length == 0)
		{
			throw new InvalidDataException("The macOS application bundle has no executable files.");
		}

		foreach (var executableFile in executableFiles)
		{
			SetUserExecutable(executableFile);
		}
	}


	private async void OnInstallLocationButtonPressed()
	{
		if (_installInProgress)
		{
			return;
		}
		var picked = await Tools.Instance.PickFolder(ProviderSettings.SaveDirectory);
		if (picked == null || _exiting)
		{
			return;
		}
		try
		{
			picked = ValidateInstallDirectory(picked);
		}
		catch (Exception pathError)
		{
			Tools.Instance?.AddError($"Invalid emulator install location: {pathError.Message}");
			return;
		}

		var previousDirectory = ProviderSettings.SaveDirectory;
		ProviderSettings.SaveDirectory = picked;
		if (!Globals.Instance.SyncCurrentProviderSettings())
		{
			ProviderSettings.SaveDirectory = previousDirectory;
			Tools.Instance?.AddError("Failed to persist the emulator install location.");
			return;
		}
		_installLocationButton.Text = ProviderSettings.SaveDirectory;
	}


	private void AutoUnpackToggled(bool unpackEnabled)
	{
		_createShortcutButton.ButtonPressed = unpackEnabled && _createShortcutButton.ButtonPressed;
		_createShortcutButton.Disabled = !unpackEnabled;
		_downloadWarning.Visible = _extractWarning.Visible || unpackEnabled;
		_extractWarning.Visible = unpackEnabled;
	}


	private void CreateShortcutToggled(bool createShortcut)
	{
		if (!createShortcut)
		{
			_autoUpdate = false;
			_autoUpdateButton.ButtonPressed = false;
		}

		_autoUpdateButton.Disabled = !createShortcut || !AppMode.SupportsAutoUnpack(_os);
	}


	private void CustomVersionEditable(bool editable)
	{
		_customVersionLineEdit.Editable = editable;
		_versionButton.Disabled = editable;
	}


	private void ExecutableNameChanged(string newName)
	{
		try
		{
			ValidateExecutableName(newName);
		}
		catch (ArgumentException nameError)
		{
			Tools.Instance?.AddError(nameError.Message);
			_executableNameLineEdit.Text = _executableName;
			return;
		}
		var previousName = _executableName;
		_executableName = newName;
		ProviderSettings.ExecutableName = newName;
		if (!Globals.Instance.SyncCurrentProviderSettings())
		{
			_executableName = previousName;
			ProviderSettings.ExecutableName = previousName;
			_executableNameLineEdit.Text = previousName;
			Tools.Instance?.AddError("Failed to persist the emulator executable name.");
		}
	}


	private void AutoUpdateToggled(bool autoUpdate)
	{
		if (_createShortcutButton.ButtonPressed || !autoUpdate)
		{
			_autoUpdate = autoUpdate;
		}
		_autoUpdateButton.ButtonPressed = _autoUpdate;
	}


	private static void ValidateFileName(string value, string description)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 180 || value is "." or ".." ||
		    value.IndexOfAny(new[] { '/', '\\' }) >= 0 || Path.GetFileName(value) != value ||
		    value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			throw new ArgumentException($"Invalid {description}.");
		}
	}


	private static void ValidateExecutableName(string value)
	{
		ValidateFileName(value, "executable name");
		if (!Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9._ -]{0,63}$"))
		{
			throw new ArgumentException("Executable name contains unsupported characters.");
		}
	}


	private static void ValidateHttpUrl(string value)
	{
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			throw new ArgumentException("Release download URL must use HTTP or HTTPS.");
		}
	}
}
