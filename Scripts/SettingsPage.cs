using Godot;
using System;
using System.Threading.Tasks;


public partial class SettingsPage : Control
{
	[Export] private ModManager _modManager;
	[Export] private OptionButton _displayModeButton;


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
		Globals.Instance.Settings = new SettingsResource();
		Globals.Instance.SetAppMode(Globals.Instance.Settings.AppMode, false);
		Globals.Instance.SetDefaultPaths();
	}


	private async void ResetInstalledModsPressed()
	{
		if (!await Tools.Instance.ConfirmationPopup())
		{
			return;
		}
		_modManager.ResetInstalled();
	}


	private void SetDisplayMode(int modeSelected)
	{
		var displayMode = _displayModeButton.GetItemId(modeSelected);
		DisplayServer.WindowSetMode((DisplayServer.WindowMode)displayMode);
		Globals.Instance.Settings.DisplayMode = displayMode;
		Globals.Instance.SaveManager.WriteSave();
	}
}
