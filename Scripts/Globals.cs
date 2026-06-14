using Godot;
using System.IO;
using System.Linq;
using Octokit;
using EmuStack.Scripts.Modes;
using Node = Godot.Node;

public partial class Globals : Node
{
	//private static Globals _instance;
	public static Globals Instance;

	public ProviderRegistry ProviderRegistry = new();
	public Mode AppMode;
	public ResourceSaveManager SaveManager = new();
	public SettingsResource Settings = new();
	
	public readonly GitHubClient LocalGithubClient = new(new ProductHeaderValue("EmuStack"));
	//public string DllsDirectory;

	public override void _Ready()
	{
		Instance = this;
		SaveManager.Version = 2.4f;
		Settings = SaveManager.GetSettings();
		SetAppMode(Settings.AppMode, false);
		MigrateLegacySettings();
		SetDefaultPaths();
		if (!string.IsNullOrEmpty(Settings.GithubApiToken))
		{
			AuthenticateGithubClient();
		}
		
		// Get launch options and update settings accordingly.
		var launchOptions = OS.GetCmdlineArgs();
		Settings.LauncherMode = launchOptions.Contains("--launcher");
		SaveManager.WriteSave();

		GetTree().CallDeferred("call_group", "Initiate", "Initiate");
	}

	public void SetDefaultPaths()
	{
		foreach (var provider in ProviderRegistry.Providers)
		{
			ApplyProviderDefaults(provider, Settings.GetProviderSettings(provider.Id));
		}

		SyncLegacySettings(CurrentProviderSettings);
		SaveManager.WriteSave(Settings);
	}


	public ProviderSettingsResource CurrentProviderSettings => Settings.GetProviderSettings(AppMode.Id);


	public void SetAppMode(string providerId, bool persist = true)
	{
		AppMode = ProviderRegistry.Get(providerId);
		Settings.AppMode = AppMode.Id;
		ApplyProviderDefaults(AppMode, CurrentProviderSettings);
		SyncLegacySettings(CurrentProviderSettings);
		if (persist)
		{
			SaveManager.WriteSave(Settings);
		}
	}


	public void SyncLegacySettings(ProviderSettingsResource providerSettings)
	{
		Settings.SaveDirectory = providerSettings.SaveDirectory;
		Settings.ExecutablePath = providerSettings.ExecutablePath;
		Settings.FromSaveDirectory = providerSettings.FromSaveDirectory;
		Settings.ToSaveDirectory = providerSettings.ToSaveDirectory;
		Settings.ModsLocation = providerSettings.ModsLocation;
		Settings.AppDataPath = providerSettings.AppDataPath;
		Settings.InstalledVersion = Tools.TryToVersionInt(providerSettings.InstalledVersion, out var legacyVersion)
			? legacyVersion
			: -1;
	}


	public void SyncCurrentProviderSettings()
	{
		SyncLegacySettings(CurrentProviderSettings);
		SaveManager.WriteSave(Settings);
	}


	public void AuthenticateGithubClient()
	{
		LocalGithubClient.Credentials = new Credentials(Settings.GithubApiToken);
	}


	private void MigrateLegacySettings()
	{
		var providerSettings = CurrentProviderSettings;
		if (!string.IsNullOrEmpty(providerSettings.SaveDirectory) ||
		    !string.IsNullOrEmpty(providerSettings.ExecutablePath) ||
		    !string.IsNullOrEmpty(providerSettings.AppDataPath))
		{
			return;
		}

		providerSettings.SaveDirectory = Settings.SaveDirectory ?? "";
		providerSettings.ExecutablePath = Settings.ExecutablePath ?? "";
		providerSettings.AppDataPath = Settings.AppDataPath ?? "";
		providerSettings.ModsLocation = Settings.ModsLocation ?? "";
		providerSettings.FromSaveDirectory = Settings.FromSaveDirectory ?? "";
		providerSettings.ToSaveDirectory = Settings.ToSaveDirectory ?? "";
		providerSettings.InstalledVersion = Settings.InstalledVersion >= 0
			? GetLegacyVersionString(Settings.InstalledVersion, AppMode)
			: "";
	}


	private void ApplyProviderDefaults(Mode provider, ProviderSettingsResource providerSettings)
	{
		var osName = OS.GetName();

		if (string.IsNullOrEmpty(providerSettings.ReleaseChannel))
		{
			providerSettings.ReleaseChannel = provider.DefaultReleaseChannel;
		}

		if (string.IsNullOrEmpty(providerSettings.ExecutableName))
		{
			providerSettings.ExecutableName = provider.DefaultExecutableName;
		}

		if (string.IsNullOrEmpty(providerSettings.SaveDirectory))
		{
			providerSettings.SaveDirectory = provider.GetDefaultInstallDirectory(osName);
		}

		if (string.IsNullOrEmpty(providerSettings.AppDataPath))
		{
			providerSettings.AppDataPath = provider.GetDefaultAppDataPath(osName);
		}

		if (string.IsNullOrEmpty(providerSettings.ModsLocation))
		{
			providerSettings.ModsLocation = provider.GetDefaultModsLocation(providerSettings.AppDataPath);
		}

		if (string.IsNullOrEmpty(providerSettings.FromSaveDirectory))
		{
			providerSettings.FromSaveDirectory = provider.GetDefaultSavesLocation(providerSettings.AppDataPath);
		}
	}


	private static string GetLegacyVersionString(int installedVersion, Mode provider)
	{
		return provider.Id == "yuzu" ? installedVersion.ToString() : Tools.FromInt(installedVersion);
	}

}
