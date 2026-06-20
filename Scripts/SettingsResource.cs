using Godot;
using Godot.Collections;


public partial class SettingsResource : Resource
{
	[Export] public bool LightModeEnabled;
	[Export] public string AppMode = "eden";
	[Export] public int DisplayMode;
	[Export] public bool LauncherMode;
	[Export] public Array<ProviderSettingsResource> ProviderSettings = new();

	// Legacy single-provider fields. Read once during migration in Globals; new code
	// must go through ProviderSettings.
	[Export] public string SaveDirectory;
	[Export] public string ExecutablePath;
	[Export] public string FromSaveDirectory;
	[Export] public string ToSaveDirectory;
	[Export] public string ModsLocation;
	[Export] public int InstalledVersion = -1;
	[Export] public string AppDataPath;

	public ProviderSettingsResource GetProviderSettings(string providerId)
	{
		foreach (var providerSettings in ProviderSettings)
		{
			if (providerSettings.ProviderId == providerId)
			{
				return providerSettings;
			}
		}

		var newProviderSettings = new ProviderSettingsResource { ProviderId = providerId };
		ProviderSettings.Add(newProviderSettings);
		return newProviderSettings;
	}
}

