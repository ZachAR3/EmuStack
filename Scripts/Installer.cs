using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mono.Unix;
using SharpCompress.Common;
using SharpCompress.Readers;
using Label = Godot.Label;


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

	private Mode AppMode => Globals.Instance.AppMode;
	private SettingsResource Settings => Globals.Instance.Settings;
	private ProviderSettingsResource ProviderSettings => Globals.Instance.CurrentProviderSettings;


	private void Initiate()
	{
		_createShortcutButton.Disabled = _os == OsKind.Windows;
		_autoUpdateButton.Disabled = !_createShortcutButton.ButtonPressed;
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
		if (!await Tools.Instance.ConfirmationPopup())
		{
			return;
		}

		try
		{
			string selectedVersion;
			if (_customVersionCheckBox.ButtonPressed)
			{
				if (!AppMode.SupportsCustomVersions)
				{
					Tools.Instance.AddError($"{AppMode.Name} does not support custom version downloads.");
					return;
				}

				if (string.IsNullOrWhiteSpace(_customVersionLineEdit.Text))
				{
					Tools.Instance.AddError("Invalid version selected, please enter a valid version number.");
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
		catch (Exception installError)
		{
			Tools.Instance.AddError($"Failed to start {AppMode.Name} install: {installError.Message}");
			ResetDownloadControls();
		}
	}


	private async Task InstallVersion(string version)
	{
		// The list of releases was already fetched by AddVersions; use the cached
		// release when possible so the user isn't left waiting before the download
		// progress appears. Fall back to GetRelease for custom versions.
		_activeDownloadRelease = FindReleaseInCache(version) ?? await AppMode.GetRelease(
			version,
			Globals.Instance.HttpClient,
			_os,
			ProviderSettings.ReleaseChannel);

		if (_activeDownloadRelease == null || string.IsNullOrEmpty(_activeDownloadRelease.DownloadUrl))
		{
			Tools.Instance.AddError($"Unable to find {AppMode.Name} release: {version}");
			return;
		}

		DeleteOldVersion();

		if (!string.IsNullOrEmpty(ProviderSettings.InstalledVersion))
		{
			var installedIndex = GetVersionButtonIndex(ProviderSettings.InstalledVersion);
			if (installedIndex >= 0)
			{
				_versionButton.SetItemDisabled(installedIndex, false);
			}
		}

		_downloadFileName = AppMode.GetDownloadFileName(_activeDownloadRelease, _os, _executableName);
		_customVersionCheckBox.Disabled = true;
		_versionButton.Disabled = true;
		_downloadButton.Disabled = true;
		_installLocationButton.Disabled = true;
		_downloadLabel.Text = "Downloading...";
		_downloadWindow.Visible = true;
		_downloadLabel.GrabFocus();

		Directory.CreateDirectory(ProviderSettings.SaveDirectory);

		_downloadRequester.DownloadFile = Path.Join(ProviderSettings.SaveDirectory, _downloadFileName);
		var requestError = _downloadRequester.Request(_activeDownloadRelease.DownloadUrl);
		if (requestError != Error.Ok)
		{
			Tools.Instance.AddError($"Failed to start download for {AppMode.Name}: {requestError}");
			ResetDownloadControls();
			return;
		}

		_downloadUpdateTimer.Start();
	}


	private async void VersionDownloadCompleted(long result, long responseCode, string[] headers, byte[] body)
	{
		_downloadUpdateTimer.Stop();
		ResetDownloadControls();

		if (result != (int)HttpRequest.Result.Success || responseCode is < 200 or >= 400)
		{
			Tools.Instance.AddError($"Failed to download {AppMode.Name}, result: {result}, HTTP status: {responseCode}");
			_downloadProgressBar.Value = 0;
			_downloadWindow.Visible = false;
			return;
		}

		ProviderSettings.InstalledVersion = _activeDownloadRelease.Version;
		Globals.Instance.SyncCurrentProviderSettings();
		_downloadProgressBar.Value = 100;
		_downloadLabel.Text = "Successfully Downloaded!";

		AddInstalledVersion();
		UnpackAndSetPermissions();
		if (_createShortcutButton.ButtonPressed)
		{
			CreateShortcut();
		}

		if (Settings.LauncherMode)
		{
			Tools.Instance.LaunchEmulator();
		}

		Globals.Instance.SyncCurrentProviderSettings();

		// Let the user see the success message before hiding the overlay.
		// Guard against the node being freed during the wait (e.g. provider switch).
		await ToSignal(GetTree().CreateTimer(2), "timeout");
		if (IsInstanceValid(this) && IsInstanceValid(_downloadWindow))
		{
			_downloadWindow.Visible = false;
		}
	}


	private void UpdateDownloadBar()
	{
		if (_downloadRequester.GetBodySize() <= 0)
		{
			return;
		}

		_downloadProgressBar.Value =
			(float)_downloadRequester.GetDownloadedBytes() / _downloadRequester.GetBodySize() * 100;
	}


	private void ResetDownloadControls()
	{
		_customVersionCheckBox.Disabled = !AppMode.SupportsCustomVersions;
		_downloadButton.Disabled = false;
		_installLocationButton.Disabled = false;
		_versionButton.Disabled = false;
	}


	private void DeleteOldVersion()
	{
		if (!Directory.Exists(ProviderSettings.SaveDirectory))
		{
			return;
		}

		if (_autoUnpackButton.ButtonPressed || _autoUpdate || AppMode.IsSingleFileDownload(_os))
		{
			if (!FsHelpers.DeleteDirectoryContents(ProviderSettings.SaveDirectory))
			{
				Tools.Instance.AddError("Failed to clear old install files before downloading.");
			}
		}
	}


	private void CreateShortcut()
	{
		var executable = _autoUpdate ? OS.GetExecutablePath() : ProviderSettings.ExecutablePath;
		var launcherFlag = _autoUpdate ? "--launcher" : null;

		if (!File.Exists(executable))
		{
			Tools.Instance.AddError("No executable path found, shortcut creation failed... Please contact a developer...");
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
				ProviderSettings.ReleaseChannel);
			_latestRelease = _availableReleases.FirstOrDefault();
			if (_latestRelease == null)
			{
				Tools.Instance.AddError($"Unable to fetch latest {AppMode.Name} release.");
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
			_latestVersionLabel.Text = "Latest: unavailable";
			_downloadButton.Disabled = true;
			Tools.Instance.AddError($"Failed to get {AppMode.Name} versions: {versionPullException.Message}");
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


	private void UnpackAndSetPermissions()
	{
		var downloadPath = Path.Join(ProviderSettings.SaveDirectory, _downloadFileName);

		if (AppMode.IsSingleFileDownload(_os))
		{
			if (_os == OsKind.Linux)
			{
				SetUserExecutable(downloadPath);
			}

			ProviderSettings.ExecutablePath = downloadPath;
			Globals.Instance.SyncCurrentProviderSettings();
			return;
		}

		// macOS DMGs ship as a mountable image; we don't have a clean way to mount,
		// copy, and detach without elevated permissions, so leave the file in place
		// and point ExecutablePath at it. Launch will surface a clear error.
		if (downloadPath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
		{
			ProviderSettings.ExecutablePath = downloadPath;
			Globals.Instance.SyncCurrentProviderSettings();
			Tools.Instance.AddError("Downloaded a macOS DMG. Mount it manually and launch the bundled app — automatic unpack is not supported.");
			return;
		}

		if (!_autoUnpackButton.ButtonPressed && !_autoUpdate)
		{
			return;
		}

		using (var stream = File.OpenRead(downloadPath))
		{
			var reader = ReaderFactory.OpenReader(stream);
			while (reader.MoveToNextEntry())
			{
				if (!reader.Entry.IsDirectory)
				{
					reader.WriteEntryToDirectory(ProviderSettings.SaveDirectory, new ExtractionOptions
					{
						ExtractFullPath = true,
						Overwrite = true,
					});
				}
			}
		}

		foreach (var folderName in AppMode.GetExtractedDirectoriesToFlatten(_activeDownloadRelease, _os))
		{
			FsHelpers.MoveFilesAndDirs(Path.Join(ProviderSettings.SaveDirectory, folderName), ProviderSettings.SaveDirectory);
		}

		var executablePath = AppMode.FindExecutable(ProviderSettings.SaveDirectory, _os, _executableName);
		if (string.IsNullOrEmpty(executablePath))
		{
			Tools.Instance.AddError($"Unable to find {AppMode.Name} executable after extraction.");
			return;
		}

		executablePath = RenameExecutableIfRequested(executablePath);
		if (_os == OsKind.Linux && File.Exists(executablePath))
		{
			SetUserExecutable(executablePath);
		}

		ProviderSettings.ExecutablePath = executablePath;
		Globals.Instance.SyncCurrentProviderSettings();
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


	private async void OnInstallLocationButtonPressed()
	{
		var picked = await Tools.Instance.PickFolder(ProviderSettings.SaveDirectory);
		if (picked == null)
		{
			return;
		}

		ProviderSettings.SaveDirectory = picked;
		Globals.Instance.SyncCurrentProviderSettings();
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

		_autoUpdateButton.Disabled = !createShortcut;
	}


	private void CustomVersionEditable(bool editable)
	{
		_customVersionLineEdit.Editable = editable;
		_versionButton.Disabled = editable;
	}


	private void ExecutableNameChanged(string newName)
	{
		_executableName = newName;
		ProviderSettings.ExecutableName = newName;
		Globals.Instance.SyncCurrentProviderSettings();
	}


	private void AutoUpdateToggled(bool autoUpdate)
	{
		if (_createShortcutButton.ButtonPressed || !autoUpdate)
		{
			_autoUpdate = autoUpdate;
		}
		_autoUpdateButton.ButtonPressed = _autoUpdate;
	}
}
