using Godot;
using System;


public partial class SettingsPage : Control
{
	[Export] private ModManager _modManager;
	[Export] private OptionButton _displayModeButton;
	private bool _exiting;


	public override void _ExitTree()
	{
		_exiting = true;
	}


	private void Initiate()
	{
		_displayModeButton.Selected = _displayModeButton.GetItemIndex(Globals.Instance.Settings.DisplayMode);
	}


	private async void ResetSettingsPressed()
	{
		if (!await Tools.Instance.ConfirmationPopup())
		{
			return;
		}
		if (_exiting)
		{
			return;
		}
		var previousSettings = Globals.Instance.Settings;
		var previousMode = Globals.Instance.AppMode;
		try
		{
			Globals.Instance.Settings = new SettingsResource();
			Globals.Instance.SetAppMode(Globals.Instance.Settings.AppMode, false);
			if (!Globals.Instance.SetDefaultPaths())
			{
				Globals.Instance.Settings = previousSettings;
				Globals.Instance.SaveManager._settings = previousSettings;
				Globals.Instance.AppMode = previousMode;
				Tools.Instance?.AddError("Failed to persist reset settings; the previous settings were retained.");
				return;
			}

			GetTree().ReloadCurrentScene();
		}
		catch (Exception resetError)
		{
			Globals.Instance.Settings = previousSettings;
			Globals.Instance.SaveManager._settings = previousSettings;
			Globals.Instance.AppMode = previousMode;
			Tools.Instance?.AddError($"Failed to reset settings; the previous settings were retained: {resetError.Message}");
		}
	}


	private async void ResetInstalledModsPressed()
	{
		if (!await Tools.Instance.ConfirmationPopup())
		{
			return;
		}
		if (_exiting)
		{
			return;
		}
		_modManager.ResetInstalled();
	}


	private void SetDisplayMode(int modeSelected)
	{
		var displayMode = _displayModeButton.GetItemId(modeSelected);
		var previousMode = Globals.Instance.Settings.DisplayMode;
		DisplayServer.WindowSetMode((DisplayServer.WindowMode)displayMode);
		Globals.Instance.Settings.DisplayMode = displayMode;
		if (!Globals.Instance.SaveManager.WriteSave())
		{
			Globals.Instance.Settings.DisplayMode = previousMode;
			DisplayServer.WindowSetMode((DisplayServer.WindowMode)previousMode);
			Tools.Instance?.AddError("Failed to persist the display mode.");
		}
	}
}
