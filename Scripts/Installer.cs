using Godot;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mono.Unix;
using NativeFileDialogSharp;
using Octokit;
using WindowsShortcutFactory;
using EmuStack.Scripts.Modes;
using Label = Godot.Label;
using SharpCompress.Common;
using SharpCompress.Readers;


public partial class Installer : Control
{
	// Exported variables (Primarily for the UI / Interactions)
	[ExportGroup("General")]
	[Export] private Label _latestVersionLabel;

	[ExportGroup("Installer")]
	[Export] private string _titlesKeySite;
	[Export] private int _versionsPerPage = 10;
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

	// Internal variables
	private String _osUsed = OS.GetName();
	// private string _yuzuExtensionString;
	private string _executableName;
	private string _downloadFileName;
	private ProviderRelease _latestRelease;
	private ProviderRelease _activeDownloadRelease;
	private List<ProviderRelease> _availableReleases = new();
	private bool _autoUpdate;
	private Mode AppMode => Globals.Instance.AppMode;
	private SettingsResource Settings => Globals.Instance.Settings;
	private ProviderSettingsResource ProviderSettings => Globals.Instance.CurrentProviderSettings;
	
	private readonly System.Net.Http.HttpClient _httpClient = new();


	// Godot functions
	private void Initiate()
	{
		_httpClient.DefaultRequestHeaders.Add("User-Agent", "EmuStack");
		_createShortcutButton.Disabled = _osUsed == "Windows";
		_autoUnpackButton.Disabled = !AppMode.SupportsAutoUnpack(_osUsed);

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



	// Custom functions
	private async void InstallSelectedVersion()
	{
		// Launches confirmation window, and cancels if not confirmed.
		var confirm = await Tools.Instance.ConfirmationPopup();
		if (confirm != true)
		{
			return;
		}

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
			int versionIndex = _versionButton.Selected;
			selectedVersion = _versionButton.GetItemMetadata(versionIndex).AsString();
		}
		await InstallVersion(selectedVersion);
	}



	private async Task InstallVersion(string version)
	{
		_activeDownloadRelease = await AppMode.GetRelease(
			version,
			Globals.Instance.LocalGithubClient,
			_httpClient,
			_osUsed,
			ProviderSettings.ReleaseChannel);

		if (_activeDownloadRelease == null || string.IsNullOrEmpty(_activeDownloadRelease.DownloadUrl))
		{
			Tools.Instance.AddError($"Unable to find {AppMode.Name} release: {version}");
			return;
		}

		DeleteOldVersion();
		
		// Set old install (if it exists) to not be disabled anymore.
		if (!string.IsNullOrEmpty(ProviderSettings.InstalledVersion))
		{
			var installedIndex = GetVersionButtonIndex(ProviderSettings.InstalledVersion);
			if (installedIndex >= 0)
			{
				_versionButton.SetItemDisabled(installedIndex, false);
			}
		}
		
		_downloadFileName = AppMode.GetDownloadFileName(_activeDownloadRelease, _osUsed, _executableName);
		_customVersionCheckBox.Disabled = true;
		_versionButton.Disabled = true;
		_downloadButton.Disabled = true;
		_installLocationButton.Disabled = true;
		_downloadLabel.Text = "Downloading...";
		_downloadWindow.Visible = true;
		_downloadLabel.GrabFocus();

		// Ensures save directory exists
		if (!Directory.Exists(ProviderSettings.SaveDirectory))
		{
			Directory.CreateDirectory(ProviderSettings.SaveDirectory);
		}
		
		_downloadRequester.DownloadFile = Path.Join(ProviderSettings.SaveDirectory, _downloadFileName);
		var requestError = _downloadRequester.Request(_activeDownloadRelease.DownloadUrl);
		if (requestError != Error.Ok)
		{
			Tools.Instance.AddError($"Failed to start download for {AppMode.Name}: {requestError}");
			ResetDownloadControls();
			return;
		}

		_downloadUpdateTimer.Start();
		_downloadLabel.Text = "Downloading...";
	}
	
	
	private void VersionDownloadCompleted(long result, long responseCode, string[] headers, byte[] body)
	{
		_downloadUpdateTimer.Stop();
		ResetDownloadControls();
		if (result == (int)HttpRequest.Result.Success && responseCode is >= 200 and < 400)
		{
			ProviderSettings.InstalledVersion = _activeDownloadRelease.Version;
			// Used to save version installed after download.
			Globals.Instance.SyncCurrentProviderSettings();
			_downloadProgressBar.Value = 100;
			_downloadLabel.Text = "Successfully Downloaded!";

			AddInstalledVersion();
			UnpackAndSetPermissions();
			if (_createShortcutButton.ButtonPressed)
			{
				_downloadWindow.Visible = false;
				CreateShortcut();
			}

			_downloadWindow.Visible = false;

			if (Settings.LauncherMode)
			{
				Tools.Instance.LaunchEmulator();
			}
			
			Globals.Instance.SyncCurrentProviderSettings();
		}
		else
		{
			Tools.Instance.AddError($"Failed to download {AppMode.Name}, result: {result}, HTTP status: {responseCode}");
			_downloadProgressBar.Value = 0;
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

		if (_autoUnpackButton.ButtonPressed || _autoUpdate || AppMode.IsSingleFileDownload(_osUsed))
		{
			if (!Tools.DeleteDirectoryContents(ProviderSettings.SaveDirectory))
			{
				Tools.Instance.AddError("Failed to clear old install files before downloading.");
			}
		}
	}


	private void CreateShortcut()
	{
		String linuxShortcutName = $"{AppMode.Id}.desktop";
		String windowsShortcutName = $"{AppMode.Id}.lnk";
		String iconPath = Path.Join(ProviderSettings.SaveDirectory, "Icon.png");

		string executable = _autoUpdate ? OS.GetExecutablePath() : ProviderSettings.ExecutablePath;
		string launcherFlag = null;
		if (_autoUpdate)
		{
			launcherFlag = "--launcher";
		}
		else
		{
			GetExistingVersion();
		}

		if (!File.Exists(executable))
		{
			Tools.Instance.AddError("No executable path found, shortcut creation failed... Please contact a developer...");
			return;
		}
		
		if (_osUsed == "Linux")
		{
			_icon.SavePng(iconPath);
			string shortcutContent = $@"
[Desktop Entry]
Comment=Nintendo Switch video game console emulator
Exec={executable} {launcherFlag}
GenericName=Switch Emulator
Icon={iconPath}
MimeType=
Name={AppMode.Name}
Path=
StartupNotify=true
Terminal=false
TerminalOptions=
Type=Application
Keywords=Nintendo;Switch;
Categories=Game;Emulator;Qt;
";

			if (Directory.Exists("/usr/share/applications/"))
			{
				string shortcutPath = $@"/usr/share/applications/{linuxShortcutName}";

				try
				{
					string tempShortcutPath = Path.Join(ProviderSettings.SaveDirectory, linuxShortcutName);
					File.WriteAllText(tempShortcutPath, shortcutContent);
					ProcessStartInfo startInfo = new ProcessStartInfo
					{
						FileName = "pkexec",
						Arguments = $"mv {tempShortcutPath} {shortcutPath}",
						UseShellExecute = false
					};

					Process process = new Process { StartInfo = startInfo };
					process.Start();
					process.WaitForExit();
				}
				catch (Exception shortcutError)
				{
					shortcutPath = Path.Join(ProviderSettings.SaveDirectory, linuxShortcutName);
					Tools.Instance.AddError(
						$@"Error creating shortcut, creating new at {shortcutPath}. Error:{shortcutError}");
					File.WriteAllText(shortcutPath, shortcutContent);
				}
			}
			else
			{
				Tools.Instance.AddError("Cannot find shortcut directory, please place manually.");
			}
		}
		else if (_osUsed == "Windows")
		{
			string commonStartMenuPath =
				System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonStartMenu);
			string emulatorStartMenuPath = Path.Combine(commonStartMenuPath, "Programs", "EmuStack", AppMode.Name);
			string emulatorShortcutPath = Path.Combine(emulatorStartMenuPath, windowsShortcutName);
			var windowsShortcut = new WindowsShortcut
			{
				Path = executable,
				IconLocation = ProviderSettings.ExecutablePath,
				Arguments = launcherFlag
			};


			try
			{
				if (!Directory.Exists(emulatorStartMenuPath))
				{
					Directory.CreateDirectory(emulatorStartMenuPath);
				}

				windowsShortcut.Save(emulatorShortcutPath);
			}
			catch (Exception shortcutError)
			{
				emulatorShortcutPath = Path.Join(ProviderSettings.SaveDirectory, windowsShortcutName);
				Tools.Instance.AddError(
					$@"cannot create shortcut, ensure app is running as admin. Placing instead at {emulatorShortcutPath}. Exception:{shortcutError}");
				windowsShortcut.Save(emulatorShortcutPath);
			}

		}
	}


	private async void AddVersions()
	{
		try
		{
			_versionButton.Clear();
			_availableReleases = await AppMode.GetAvailableReleases(
				Globals.Instance.LocalGithubClient,
				_httpClient,
				_osUsed,
				ProviderSettings.ReleaseChannel);
			_latestRelease = _availableReleases.FirstOrDefault();
			if (_latestRelease == null)
			{
				Tools.Instance.AddError("Unable to fetch latest emulator release");
				return;
			}

			_latestVersionLabel.Text = $"Latest: {_latestRelease.DisplayName}";

			foreach (var release in _availableReleases)
			{
				_versionButton.AddItem(release.DisplayName);
				_versionButton.SetItemMetadata(_versionButton.ItemCount - 1, release.Version);
			}

		
			//Checks if there is already a version installed, and if so adds it.
			if (!string.IsNullOrEmpty(ProviderSettings.InstalledVersion))
			{
				AddInstalledVersion();
			}
			
			_downloadButton.Disabled = false;
			
			// If running in launcher mode updates and launches yuzu
			if (Settings.LauncherMode)
			{
				if (_latestRelease.Version != ProviderSettings.InstalledVersion)
				{
					// The emulator will launch after the download completes.
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
			Tools.Instance.AddError("Failed to get latest versions error code: " + versionPullException);
		}
	}


	private void AddInstalledVersion()
	{
		var installedVersion = ProviderSettings.InstalledVersion;
		var selectedIndex = GetVersionButtonIndex(installedVersion);
		// Set the custom version to default of the currently installed one
		_customVersionLineEdit.Text = installedVersion;

		// Checks if the item was already added, if so sets it as current, otherwise adds a new item entry for it.
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
		string downloadPath = Path.Join(ProviderSettings.SaveDirectory, _downloadFileName);
		if (AppMode.IsSingleFileDownload(_osUsed))
		{
			if (_osUsed == "Linux")
			{
				SetUserExecutable(downloadPath);
			}

			ProviderSettings.ExecutablePath = downloadPath;
			Globals.Instance.SyncCurrentProviderSettings();
		}
		else if (_autoUnpackButton.ButtonPressed || _autoUpdate)
		{
			if (downloadPath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
			{
				Tools.Instance.AddError("Downloaded a macOS DMG. Automatic unpack is not supported for this package yet.");
				return;
			}

			using Stream stream = File.OpenRead(downloadPath);
			var reader = ReaderFactory.OpenReader(stream);
			while (reader.MoveToNextEntry())
			{
				if (!reader.Entry.IsDirectory)
				{
					ExtractionOptions opt = new ExtractionOptions
					{
						ExtractFullPath = true,
						Overwrite = true
					};
					reader.WriteEntryToDirectory(ProviderSettings.SaveDirectory, opt);
				}
			}

			foreach (var folderName in AppMode.GetExtractedDirectoriesToFlatten(_activeDownloadRelease, _osUsed))
			{
				Tools.MoveFilesAndDirs(Path.Join(ProviderSettings.SaveDirectory, folderName), ProviderSettings.SaveDirectory);
			}

			var executablePath = AppMode.FindExecutable(ProviderSettings.SaveDirectory, _osUsed, _executableName);
			if (string.IsNullOrEmpty(executablePath))
			{
				Tools.Instance.AddError($"Unable to find {AppMode.Name} executable after extraction.");
				return;
			}

			executablePath = RenameExecutableIfRequested(executablePath);
			if (_osUsed == "Linux" && File.Exists(executablePath))
			{
				SetUserExecutable(executablePath);
			}

			ProviderSettings.ExecutablePath = executablePath;
			Globals.Instance.SyncCurrentProviderSettings();
		}
		
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
		var executableFile = new UnixFileInfo(executablePath)
		{
			FileAccessPermissions = FileAccessPermissions.UserReadWriteExecute
		};
	}


	private String GetExistingVersion()
	{
		if (DirAccess.DirExistsAbsolute(ProviderSettings.SaveDirectory))
		{
			var previousSave = DirAccess.Open(ProviderSettings.SaveDirectory);

			foreach (var file in previousSave.GetFiles())
			{
				if (file.GetExtension() == "AppImage" || file == _downloadFileName)
				{
					return Path.Join(ProviderSettings.SaveDirectory, file);
				}
			}
		}

		Tools.Instance.AddError("Unable to find existing version");
		return "";
	}


	// Signal functions
	private void OnInstallLocationButtonPressed()
	{
		var saveDirectoryLocationInput = Dialog.FolderPicker(ProviderSettings.SaveDirectory).Path;
		if (saveDirectoryLocationInput != null)
		{
			ProviderSettings.SaveDirectory = saveDirectoryLocationInput;
		}
		
		_installLocationButton.Text = ProviderSettings.SaveDirectory;
		Globals.Instance.SyncCurrentProviderSettings();
	}


	private void AutoUnpackToggled(bool unpackEnabled)
	{
		// If unpack is toggled off, ensures the create shortcut button is also disabled and turns off.
		_createShortcutButton.ButtonPressed = unpackEnabled && _createShortcutButton.ButtonPressed;
		_createShortcutButton.Disabled = !unpackEnabled;
		_downloadWarning.Visible = _extractWarning.Visible || unpackEnabled;
		_extractWarning.Visible = unpackEnabled;
	}


	private void CustomVersionSpinBoxEditable(bool editable)
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
