using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;


public partial class ToolsPage : Control
{
	[ExportGroup("Tools")]
	[Export] private Button _clearInstallFolderButton;
	[Export] private Button _backupSavesButton;
	[Export] private Button _restoreSavesButton;
	[Export] private Button _fromSaveDirectoryButton;
	[Export] private Button _toSaveDirectoryButton;
	private bool _operationInProgress;
	private bool _exiting;
	private readonly CancellationTokenSource _lifetimeCancellation = new();


	public override void _ExitTree()
	{
		_exiting = true;
		_lifetimeCancellation.Cancel();
	}


	private async void Initiate()
	{
		_fromSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.FromSaveDirectory;
		_toSaveDirectoryButton.Text = Globals.Instance.CurrentProviderSettings.ToSaveDirectory;
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			var source = Globals.Instance.CurrentProviderSettings.FromSaveDirectory;
			var destination = Globals.Instance.CurrentProviderSettings.ToSaveDirectory;
			await Task.Run(() => RecoverTransactionalCopies(source, destination), _lifetimeCancellation.Token);
		}
		catch (OperationCanceledException)
		{
			// Scene-owned recovery is canceled during provider switches and shutdown.
		}
		catch (Exception recoveryError)
		{
			if (!_exiting)
			{
				Tools.Instance?.AddError($"Save recovery failed: {recoveryError.Message}");
			}
		}
		finally
		{
			EndOperation();
		}
	}


	private async void ClearInstallFolderButtonPressed()
	{
		if (!BeginOperation())
		{
			return;
		}

		if (!await Tools.Instance.ConfirmationPopup())
		{
			EndOperation();
			return;
		}

		var installDirectory = Globals.Instance.CurrentProviderSettings.SaveDirectory;
		var installOwner = $"install:{Globals.Instance.AppMode.Id}";
		try
		{
			_lifetimeCancellation.Token.ThrowIfCancellationRequested();
			await Task.Run(() => FsHelpers.DeleteOwnedDirectoryContents(
				installDirectory,
				installOwner), _lifetimeCancellation.Token);
			if (!_exiting && IsInstanceValid(_clearInstallFolderButton))
			{
				_clearInstallFolderButton.Text = "Cleared successfully!";
			}
		}
		catch (Exception clearError)
		{
			if (!_exiting)
			{
				Tools.Instance?.AddError($"Failed to clear installation folder: {clearError.Message}");
				_clearInstallFolderButton.Text = "Clear refused";
			}
		}
		finally
		{
			EndOperation();
		}
	}


	private async void OnBackupSavesButtonPressed()
	{
		if (!BeginOperation())
		{
			return;
		}

		var source = Globals.Instance.CurrentProviderSettings.FromSaveDirectory;
		var destination = Globals.Instance.CurrentProviderSettings.ToSaveDirectory;
		var backupOwner = $"save-backup:{Globals.Instance.AppMode.Id}";
		try
		{
			_lifetimeCancellation.Token.ThrowIfCancellationRequested();
			await Task.Run(() => RecoverTransactionalCopies(source, destination), _lifetimeCancellation.Token);
			_lifetimeCancellation.Token.ThrowIfCancellationRequested();
			await Task.Run(() => FsHelpers.TransactionalCopyDirectory(
				source,
				destination,
				preserveDestinationFiles: false,
				requiredDestinationOwner: backupOwner), _lifetimeCancellation.Token);
			if (!_exiting && IsInstanceValid(_backupSavesButton))
			{
				_backupSavesButton.Text = "Backup successful!";
			}
		}
		catch (Exception backupError)
		{
			if (!_exiting)
			{
				Tools.Instance?.AddError("failed to create save backup exception:" + backupError);
			}
		}
		finally
		{
			EndOperation();
		}
	}


	private async void OnRestoreSavesPressed()
	{
		if (!BeginOperation())
		{
			return;
		}

		var source = Globals.Instance.CurrentProviderSettings.ToSaveDirectory;
		var destination = Globals.Instance.CurrentProviderSettings.FromSaveDirectory;
		var backupOwner = $"save-backup:{Globals.Instance.AppMode.Id}";
		try
		{
			_lifetimeCancellation.Token.ThrowIfCancellationRequested();
			await Task.Run(() => RecoverTransactionalCopies(source, destination), _lifetimeCancellation.Token);
			_lifetimeCancellation.Token.ThrowIfCancellationRequested();
			await Task.Run(() => FsHelpers.TransactionalCopyDirectory(
				source,
				destination,
				preserveDestinationFiles: true,
				requiredSourceOwner: backupOwner), _lifetimeCancellation.Token);
			if (!_exiting && IsInstanceValid(_restoreSavesButton))
			{
				_restoreSavesButton.Text = "Saves restored successfully!";
			}
		}
		catch (Exception restoreError)
		{
			if (!_exiting)
			{
				Tools.Instance?.AddError("failed to restore saves, exception: " + restoreError);
			}
		}
		finally
		{
			EndOperation();
		}
	}


	private async void OnFromSaveDirectoryButtonPressed()
	{
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			var settings = Globals.Instance.CurrentProviderSettings;
			var picked = await Tools.Instance.PickFolder(settings.FromSaveDirectory);
			if (picked == null || _exiting)
			{
				return;
			}
			try
			{
				picked = ValidateSaveDirectory(picked);
			}
			catch (Exception pathError)
			{
				Tools.Instance?.AddError($"Invalid source save directory: {pathError.Message}");
				return;
			}

			var previousDirectory = settings.FromSaveDirectory;
			settings.FromSaveDirectory = picked;
			if (!Globals.Instance.SyncCurrentProviderSettings())
			{
				settings.FromSaveDirectory = previousDirectory;
				Tools.Instance?.AddError("Failed to persist the source save directory.");
				return;
			}
			_fromSaveDirectoryButton.Text = settings.FromSaveDirectory;
		}
		finally
		{
			EndOperation();
		}
	}


	private async void OnToSaveDirectoryButtonPressed()
	{
		if (!BeginOperation())
		{
			return;
		}

		try
		{
			var settings = Globals.Instance.CurrentProviderSettings;
			var picked = await Tools.Instance.PickFolder(settings.ToSaveDirectory);
			if (picked == null || _exiting)
			{
				return;
			}
			try
			{
				picked = ValidateSaveDirectory(picked);
			}
			catch (Exception pathError)
			{
				Tools.Instance?.AddError($"Invalid backup save directory: {pathError.Message}");
				return;
			}

			var previousDirectory = settings.ToSaveDirectory;
			settings.ToSaveDirectory = picked;
			if (!Globals.Instance.SyncCurrentProviderSettings())
			{
				settings.ToSaveDirectory = previousDirectory;
				Tools.Instance?.AddError("Failed to persist the backup save directory.");
				return;
			}
			_toSaveDirectoryButton.Text = settings.ToSaveDirectory;
		}
		finally
		{
			EndOperation();
		}
	}


	private bool BeginOperation()
	{
		if (_operationInProgress || _exiting)
		{
			return false;
		}

		_operationInProgress = true;
		_clearInstallFolderButton.Disabled = true;
		_backupSavesButton.Disabled = true;
		_restoreSavesButton.Disabled = true;
		_fromSaveDirectoryButton.Disabled = true;
		_toSaveDirectoryButton.Disabled = true;
		return true;
	}


	private void EndOperation()
	{
		_operationInProgress = false;
		if (_exiting)
		{
			return;
		}

		_clearInstallFolderButton.Disabled = false;
		_backupSavesButton.Disabled = false;
		_restoreSavesButton.Disabled = false;
		_fromSaveDirectoryButton.Disabled = false;
		_toSaveDirectoryButton.Disabled = false;
	}


	private static string ValidateSaveDirectory(string path)
	{
		var canonicalPath = FsHelpers.CanonicalPath(path);
		FsHelpers.RejectDangerousRoot(canonicalPath);
		FsHelpers.EnsureNoReparsePoints(canonicalPath);
		return canonicalPath;
	}


	private static void RecoverTransactionalCopies(string source, string destination)
	{
		if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
		{
			return;
		}

		FsHelpers.RecoverTransactionalCopy(destination);
		if (!string.Equals(
			FsHelpers.CanonicalPath(source),
			FsHelpers.CanonicalPath(destination),
			OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal))
		{
			FsHelpers.RecoverTransactionalCopy(source);
		}
	}
}
