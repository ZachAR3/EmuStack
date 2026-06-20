using Godot;
using System.Linq;
using HttpClient = System.Net.Http.HttpClient;
using Node = Godot.Node;


public partial class Globals : Node
{
	public static Globals Instance;

	public ProviderRegistry ProviderRegistry = new();
	public Mode AppMode;
	public ResourceSaveManager SaveManager = new();
	public SettingsResource Settings = new();

	public readonly HttpClient HttpClient = new();


	public override void _Ready()
	{
		Instance = this;
		HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EmuStack/1.0");
		SaveManager.Version = 4;
		Settings = SaveManager.GetSettings();

		// Resolve the active provider first so legacy fields migrate into the right
		// provider before defaults are applied — otherwise the migration's empty-check
		// sees freshly defaulted values and skips.
		AppMode = ProviderRegistry.Get(Settings.AppMode);
		Settings.AppMode = AppMode.Id;
		MigrateLegacySettings(CurrentProviderSettings);
		SetDefaultPaths();

		Settings.LauncherMode = OS.GetCmdlineArgs().Contains("--launcher");
		SaveManager.WriteSave();
	}


	public ProviderSettingsResource CurrentProviderSettings => Settings.GetProviderSettings(AppMode.Id);


	public void SetDefaultPaths()
	{
		foreach (var provider in ProviderRegistry.Providers)
		{
			ApplyProviderDefaults(provider, Settings.GetProviderSettings(provider.Id));
		}

		SaveManager.WriteSave(Settings);
	}


	public void SetAppMode(string providerId, bool persist = true)
	{
		AppMode = ProviderRegistry.Get(providerId);
		Settings.AppMode = AppMode.Id;
		MigrateLegacySettings(CurrentProviderSettings);
		ApplyProviderDefaults(AppMode, CurrentProviderSettings);
		if (persist)
		{
			SaveManager.WriteSave(Settings);
		}
	}


	public void SyncCurrentProviderSettings()
	{
		SaveManager.WriteSave(Settings);
	}


	private void MigrateLegacySettings(ProviderSettingsResource providerSettings)
	{
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
			? LegacyVersion.FromInt(Settings.InstalledVersion)
			: "";
	}


	private static void ApplyProviderDefaults(Mode provider, ProviderSettingsResource providerSettings)
	{
		var os = Os.Current;

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
			providerSettings.SaveDirectory = provider.GetDefaultInstallDirectory(os);
		}

		if (string.IsNullOrEmpty(providerSettings.AppDataPath))
		{
			providerSettings.AppDataPath = provider.GetDefaultAppDataPath(os);
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
}
