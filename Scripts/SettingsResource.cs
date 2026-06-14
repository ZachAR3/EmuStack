using Godot;
using Godot.Collections;
using EmuStack.Scripts.Modes;


public partial class SettingsResource : Resource
{
	[Export] public string SaveDirectory;
	[Export] public string ExecutablePath;
	[Export] public string FromSaveDirectory;
	[Export] public string ToSaveDirectory;
	[Export] public string ModsLocation;
	[Export] public int InstalledVersion = -1;
	[Export] public bool LightModeEnabled;
	[Export] public string AppMode = "ryubing";
	[Export] public bool Muted = true;
	[Export] public string AppDataPath;
	[Export] public string GithubApiToken;
	[Export] public bool GetCompatibleVersions;
	[Export] public int DisplayMode;
	[Export] public bool LauncherMode;
	[Export] public Array<ProviderSettingsResource> ProviderSettings = new();

	public ProviderSettingsResource GetProviderSettings(string providerId)
	{
		foreach (var providerSettings in ProviderSettings)
		{
			if (providerSettings.ProviderId == providerId)
			{
				return providerSettings;
			}
		}

		var newProviderSettings = new ProviderSettingsResource
		{
			ProviderId = providerId
		};
		ProviderSettings.Add(newProviderSettings);
		return newProviderSettings;
	}
}
