using Godot;


public partial class SettingsPage : Node
{
	[Export()] private ModManager _modManager;
	[Export()] private LineEdit _githubTokenLineEdit;

	[Export()] private CheckBox _getCompatibleVersionsButton;
	[Export()] private OptionButton _displayModeButton;


	private void Initiate()
	{
		_getCompatibleVersionsButton.ButtonPressed = Globals.Instance.Settings.GetCompatibleVersions;
		_displayModeButton.Selected = _displayModeButton.GetItemIndex(Globals.Instance.Settings.DisplayMode);
	}
	
	
	// Signal functions
	private async void ResetSettingsPressed()
	{
		var confirm = await Tools.Instance.ConfirmationPopup();
		if (confirm != true)
		{
			return;
		}
		Globals.Instance.Settings = new SettingsResource();
		Globals.Instance.SetAppMode(Globals.Instance.Settings.AppMode, false);
		Globals.Instance.SetDefaultPaths();
	}


	private async void ResetInstalledModsPressed()
	{
		var confirm = await Tools.Instance.ConfirmationPopup();
		if (confirm != true)
		{
			return;
		}
		_modManager.ResetInstalled();
	}


	private void GithubTokenEntered(string token)
	{
		Globals.Instance.Settings.GithubApiToken = token;
		Globals.Instance.SaveManager.WriteSave();
		Globals.Instance.AuthenticateGithubClient();
		_githubTokenLineEdit.Text = "Success!";
	}


	private void GetCompatibleToggled(bool getCompatible)
	{
		Globals.Instance.Settings.GetCompatibleVersions = getCompatible;
		Globals.Instance.SaveManager.WriteSave();
	}


	private void SetDisplayMode(int modeSelected)
	{
		var displayMode = _displayModeButton.GetItemId(modeSelected);
		DisplayServer.WindowSetMode((DisplayServer.WindowMode)displayMode);
		Globals.Instance.Settings.DisplayMode = displayMode;
		Globals.Instance.SaveManager.WriteSave();
	}
}
