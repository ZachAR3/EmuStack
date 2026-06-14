using Godot;
using System;
using NativeFileDialogSharp;

public partial class ToolsPage : Control
{
	[ExportGroup("Tools")] 
	[Export()] private Button _clearInstallFolderButton;
	[Export()] private Button _backupSavesButton;
	[Export()] private Button _restoreSavesButton;
	[Export()] private Button _fromSaveDirectoryButton;
	[Export()] private Button _toSaveDirectoryButton;
	
	private string _osUsed = OS.GetName();
	
	
	// Godot Functions
	private void Initiate()
	{
		_fromSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.FromSaveDirectory;
		_toSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.ToSaveDirectory;
	}
	
	
	// Signal functions
	private async void ClearInstallFolderButtonPressed()
	{
		var confirm = await Tools.Instance.ConfirmationPopup();
		if (confirm == false)
		{
			return;
		}
		
		// Clears the install folder, if failed notifies user
		if (!Tools.Instance.ClearInstallationFolder(Globals.Instance.CurrentProviderSettings.SaveDirectory))
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
			Tools.Instance.DuplicateDirectoryContents(
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
			Tools.Instance.DuplicateDirectoryContents(
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
	
	
	private void OnFromSaveDirectoryButtonPressed()
	{
		var fromSaveDirectoryInput = Dialog.FolderPicker(Globals.Instance.CurrentProviderSettings.FromSaveDirectory).Path;
		if (fromSaveDirectoryInput != null)
		{
			Globals.Instance.CurrentProviderSettings.FromSaveDirectory = fromSaveDirectoryInput;
		}

		_fromSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.FromSaveDirectory;
		Globals.Instance.SyncCurrentProviderSettings();
	}
	
	private void OnToSaveDirectoryButtonPressed()
	{
		var toSaveDirectoryInput = Dialog.FolderPicker(Globals.Instance.CurrentProviderSettings.ToSaveDirectory).Path;
		if (toSaveDirectoryInput != null)
		{
			Globals.Instance.CurrentProviderSettings.ToSaveDirectory = toSaveDirectoryInput;
		}

		_toSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.ToSaveDirectory;
		Globals.Instance.SyncCurrentProviderSettings();
	}


}
