using Godot;
using System;


public partial class ToolsPage : Control
{
	[ExportGroup("Tools")]
	[Export] private Button _clearInstallFolderButton;
	[Export] private Button _backupSavesButton;
	[Export] private Button _restoreSavesButton;
	[Export] private Button _fromSaveDirectoryButton;
	[Export] private Button _toSaveDirectoryButton;


	private void Initiate()
	{
		_fromSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.FromSaveDirectory;
		_toSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.ToSaveDirectory;
	}


	private async void ClearInstallFolderButtonPressed()
	{
		if (!await Tools.Instance.ConfirmationPopup())
		{
			return;
		}

		if (!FsHelpers.DeleteDirectoryContents(Globals.Instance.CurrentProviderSettings.SaveDirectory))
		{
			Tools.Instance.AddError("failed to clear installation folder");
			_clearInstallFolderButton.Text = "Clear failed!";
		}
		else
		{
			_clearInstallFolderButton.Text = "Cleared successfully!";
		}
	}


	private void OnBackupSavesButtonPressed()
	{
		try
		{
			FsHelpers.DuplicateDirectoryContents(
				Globals.Instance.CurrentProviderSettings.FromSaveDirectory,
				Globals.Instance.CurrentProviderSettings.ToSaveDirectory,
				true);
			_backupSavesButton.Text = "Backup successful!";
		}
		catch (Exception backupError)
		{
			Tools.Instance.AddError("failed to create save backup exception:" + backupError);
		}
	}


	private void OnRestoreSavesPressed()
	{
		try
		{
			FsHelpers.DuplicateDirectoryContents(
				Globals.Instance.CurrentProviderSettings.ToSaveDirectory,
				Globals.Instance.CurrentProviderSettings.FromSaveDirectory,
				true);
			_restoreSavesButton.Text = "Saves restored successfully!";
		}
		catch (Exception restoreError)
		{
			Tools.Instance.AddError("failed to restore saves, exception: " + restoreError);
		}
	}


	private async void OnFromSaveDirectoryButtonPressed()
	{
		var settings = Globals.Instance.CurrentProviderSettings;
		var picked = await Tools.Instance.PickFolder(settings.FromSaveDirectory);
		if (picked == null)
		{
			return;
		}

		settings.FromSaveDirectory = picked;
		Globals.Instance.SyncCurrentProviderSettings();
		_fromSaveDirectoryButton.Text = settings.FromSaveDirectory;
	}


	private async void OnToSaveDirectoryButtonPressed()
	{
		var settings = Globals.Instance.CurrentProviderSettings;
		var picked = await Tools.Instance.PickFolder(settings.ToSaveDirectory);
		if (picked == null)
		{
			return;
		}

		settings.ToSaveDirectory = picked;
		Globals.Instance.SyncCurrentProviderSettings();
		_toSaveDirectoryButton.Text = settings.ToSaveDirectory;
	}
}

