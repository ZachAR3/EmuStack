using Godot;
using Godot.Collections;


public partial class Home : Control
{
	[ExportGroup("App")]
	[Export] private OptionButton _appModesButton;
	[Export] private TextureRect _darkBg;
	[Export] private TextureRect _lightBg;
	[Export] private ColorRect _downloadWindowApp;
	[Export] private Label _downloadLabel;
	[Export] private CheckButton _enableLightTheme;
	[Export] private Array<Theme> _themes;
	[Export] private ColorRect _header;
	[Export] private Label _headerLabel;
	[Export] private Label _latestVersionLabel;

	[ExportGroup("ModManager")]
	[Export] private ItemList _modList;
	[Export] private AnimatedSprite2D _modManagerLoadingSprite;
	[Export] private Label _modManagerLoadingLabel;


	private Theme _currentTheme;
	private readonly System.Collections.Generic.Dictionary<int, string> _providerIds = new();
	private SettingsResource Settings => Globals.Instance.Settings;

	private static readonly Color DarkBarColor = new(0.16862745583057f, 0.1803921610117f, 0.18823529779911f);
	private static readonly Color LightBarColor = new(0.74117648601532f, 0.76470589637756f, 0.78039216995239f);


	private void Initiate()
	{
		DisplayServer.WindowSetMinSize(new Vector2I(1024, 576));
		DisplayServer.WindowSetMode((DisplayServer.WindowMode)Settings.DisplayMode);

		SetTheme(Settings.LightModeEnabled);
		PopulateModes();
		SetMode(0, Settings.AppMode);

		WindowResized();

		Resized += WindowResized;
	}


	public override void _Ready()
	{
		// Globals is now an autoload singleton, so it no longer re-runs _Ready on scene
		// reloads. The Home scene root does re-instantiate every reload, so it owns the
		// deferred Initiate group kick for both the first launch and provider switches.
		GetTree().CallDeferred("call_group", "Initiate", "Initiate");
	}


	private void SetTheme(bool enableLight)
	{
		_lightBg.Visible = enableLight;
		_darkBg.Visible = !enableLight;
		_currentTheme = enableLight ? _themes[1] : _themes[0];
		var barColor = enableLight ? LightBarColor : DarkBarColor;
		_header.Color = barColor;
		_downloadWindowApp.Color = barColor;
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
		GetTree().ReloadCurrentScene();
	}


	private void WindowResized()
	{
		// Layout was authored at 1080p; scale fonts and icons proportionally for other window sizes.
		const float referenceWidth = 1920f;
		const float referenceHeight = 1080f;
		const int loadingFontSize = 64;
		const int headerFontSize = 49;
		const int versionFontSize = 32;
		const int defaultFontSize = 35;
		const int minDefaultFont = 20;
		const int maxDefaultFont = 50;

		var scaleRatio = ((GetWindow().Size.X / referenceWidth) + (GetWindow().Size.Y / referenceHeight)) / 2;
		_modList.IconScale = scaleRatio;
		_modManagerLoadingSprite.Scale = new Vector2(scaleRatio, scaleRatio);
		_modManagerLoadingLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * loadingFontSize));
		_downloadLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * loadingFontSize));
		_headerLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * headerFontSize));
		_latestVersionLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * versionFontSize));
		_currentTheme.DefaultFontSize = Mathf.Clamp((int)(scaleRatio * defaultFontSize), minDefaultFont, maxDefaultFont);
	}


	private void ExitButtonPressed()
	{
		GetTree().Quit();
	}
}

