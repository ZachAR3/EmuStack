using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;


public partial class Home : Control
{
	[ExportGroup("App")]
	[Export()] private float _appVersion = 2f;
	[Export()] private OptionButton _appModesButton;
	[Export()] private TextureRect _darkBg;
	[Export()] private TextureRect _lightBg;
	[Export()] private ColorRect _downloadWindowApp;
	[Export()] private AudioStreamPlayer _backgroundAudio;
	[Export()] private CheckButton _muteButton;
	[Export()] private CheckButton _enableLightTheme;
	[Export()] private Array<Theme> _themes;
	[Export()] private Array<StyleBoxLine> _themesSeparator;
	[Export()] private ColorRect _header;
	[Export()] private Label _headerLabel;
	[Export()] private Label _latestVersionLabel;
	[Export()] private Control _errorConsole;

	[ExportGroup("ModManager")]
	[Export()] private ItemList _modList;
	[Export()] private AnimatedSprite2D _modMangerLoadingSprite;
	[Export()] private Label _modManagerLoadingLabel;


	// Internal variables
	private Theme _currentTheme;
	private System.Collections.Generic.Dictionary<int, string> _providerIds = new();
	private SettingsResource Settings => Globals.Instance.Settings;


	// Godot functions
	private void Initiate()
	{
		// Sets minimum window size and display mode.
		DisplayServer.WindowSetMinSize(new Vector2I(1024, 576));
		DisplayServer.WindowSetMode((DisplayServer.WindowMode)Settings.DisplayMode);

		// Set the theme
		SetTheme(Settings.LightModeEnabled);
		PopulateModes();
		SetMode(0, Settings.AppMode);

		// Sets scaling (Called manually to hopefully fix #31
		WindowResized();

		// Signals
		Resized += WindowResized;
	}


	// Custom functions
	private void SetTheme(bool enableLight)
	{
		_lightBg.Visible = enableLight;
		_darkBg.Visible = !enableLight;
		_currentTheme = enableLight ? _themes[1] : _themes[0];
		_header.Color = enableLight ? new Godot.Color(0.74117648601532f, 0.76470589637756f, 0.78039216995239f) : new Godot.Color(0.16862745583057f, 0.1803921610117f, 0.18823529779911f);
		_downloadWindowApp.Color = enableLight ? new Godot.Color(0.74117648601532f, 0.76470589637756f, 0.78039216995239f) : new Godot.Color(0.16862745583057f, 0.1803921610117f, 0.18823529779911f);
		_enableLightTheme.ButtonPressed = enableLight;
		Settings.LightModeEnabled = enableLight;
		Globals.Instance.SaveManager.WriteSave(Settings);
		Theme = _currentTheme;
	}


	private void PopulateModes()
	{
		_appModesButton.Clear();
		_providerIds.Clear();

		for (var providerIndex = 0; providerIndex < Globals.Instance.ProviderRegistry.Providers.Count; providerIndex++)
		{
			var provider = Globals.Instance.ProviderRegistry.Providers[providerIndex];
			_appModesButton.AddItem(provider.Name, providerIndex);
			_appModesButton.SetItemMetadata(providerIndex, provider.Id);
			_providerIds[providerIndex] = provider.Id;
		}
	}


	private void SetMode(int newMode, string forcedMode = "")
	{
		string providerId;
		// if manually setting a mode set it to that, otherwise for the button event updates use the indexed mode given
		if (forcedMode != "")
		{
			providerId = Globals.Instance.ProviderRegistry.NormalizeProviderId(forcedMode);
		}
		else
		{
			providerId = _providerIds.TryGetValue(newMode, out var selectedProviderId)
				? selectedProviderId
				: Globals.Instance.ProviderRegistry.DefaultProvider.Id;
		}

		Globals.Instance.SetAppMode(providerId);
		for (var itemIndex = 0; itemIndex < _appModesButton.ItemCount; itemIndex++)
		{
			if (_appModesButton.GetItemMetadata(itemIndex).AsString() == Globals.Instance.AppMode.Id)
			{
				_appModesButton.Selected = itemIndex;
				return;
			}
		}
	}


	private void ModeChanged(int newModeIndex)
	{
		SetMode(newModeIndex);
	}
	

	private void OpenConsole()
	{
		_errorConsole.Visible = !_errorConsole.Visible;
	}
	
	
	// Signal functions
	private void WindowResized()
	{
		float scaleRatio = (((float)GetWindow().Size.X / 1920) + ((float)GetWindow().Size.Y / 1080)) / 2;
		_modList.IconScale = scaleRatio;
		_modMangerLoadingSprite.Scale = new Vector2(scaleRatio, scaleRatio);
		_modManagerLoadingLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * 64));
		_headerLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * 49));
		_latestVersionLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * 32));
		_currentTheme.DefaultFontSize = Mathf.Clamp((int)(scaleRatio * 35), 20, 50);
	}


	private void ExitButtonPressed()
	{
		GetTree().Quit();
	}

}
