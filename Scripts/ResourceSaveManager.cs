using Godot;
using System;
using System.IO;


public partial class ResourceSaveManager : Resource
{
	public const string SaveGameBasePath = "user://InternalSave";
	private const string LegacyAppName = "YuzuToolbox";

	[Export] public int Version;
	[Export] public SettingsResource _settings;


	public bool WriteSave(SettingsResource settings = null)
	{
		_settings = settings ?? _settings;
		var savePath = GetSavePath();
		var extension = Path.GetExtension(savePath);
		var temporaryPath = SaveGameBasePath + $".tmp-{Guid.NewGuid():N}" + extension;
		var temporaryGlobalPath = ProjectSettings.GlobalizePath(temporaryPath);
		try
		{
			FsHelpers.EnsureNoReparsePoints(temporaryGlobalPath);
			var error = ResourceSaver.Save(this, temporaryPath);
			if (error != Error.Ok)
			{
				Tools.Instance?.AddError($"Failed to write settings: {error}");
				return false;
			}

			FsHelpers.AtomicReplaceFile(
				temporaryGlobalPath,
				ProjectSettings.GlobalizePath(savePath));
			return true;
		}
		catch (Exception saveError)
		{
			Tools.Instance?.AddError($"Failed to commit settings: {saveError.Message}");
			return false;
		}
		finally
		{
			if (File.Exists(temporaryGlobalPath))
			{
				try
				{
					FsHelpers.EnsureNoReparsePoints(temporaryGlobalPath);
					File.Delete(temporaryGlobalPath);
				}
				catch (Exception cleanupError)
				{
					Tools.Instance?.AddError($"Settings temporary-file cleanup failed: {cleanupError.Message}");
				}
			}
		}
	}


	public SettingsResource GetSettings()
	{
		if (!SaveExists())
		{
			CopyLegacySave();
		}

		if (SaveExists())
		{
			var lastSave = LoadSave() as ResourceSaveManager;

			// lastSave is null when the save file exists but is corrupt or fails to
			// deserialize into the current schema. Treat that the same as a version
			// mismatch: reset to defaults so the app stays usable.
			if (lastSave == null || lastSave.Version != Version)
			{
				// Tools may not be in the tree yet — defer the warning until it is.
				Tools.Instance?.CallDeferred(nameof(Tools.AddError),
					"Save version is incompatible with app, resetting user settings.");
				_settings = new SettingsResource();
				WriteSave();
			}
			else
			{
				_settings = lastSave._settings ?? new SettingsResource();
			}
		}
		else
		{
			_settings = new SettingsResource();
			WriteSave();
		}

		return _settings;
	}


	private static bool SaveExists()
	{
		var savePath = ProjectSettings.GlobalizePath(GetSavePath());
		return File.Exists(savePath);
	}


	private static Resource LoadSave()
	{
		if (!SaveExists())
		{
			return null;
		}

		Exception currentLoadError = null;
		try
		{
			var currentGlobalPath = ProjectSettings.GlobalizePath(GetSavePath());
			FsHelpers.EnsureNoReparsePoints(currentGlobalPath);
			var loaded = ResourceLoader.Load(GetSavePath());
			if (loaded is ResourceSaveManager)
			{
				return loaded;
			}
			currentLoadError = new InvalidDataException("The settings resource has an unexpected type.");
		}
		catch (Exception loadError)
		{
			currentLoadError = loadError;
		}

		var savePath = ProjectSettings.GlobalizePath(GetSavePath());
		var backupPath = savePath + ".bak";
		if (!File.Exists(backupPath))
		{
			Tools.Instance?.AddError($"Could not load settings safely: {currentLoadError?.Message ?? "invalid save"}");
			return null;
		}

		try
		{
			FsHelpers.EnsureNoReparsePoints(backupPath);
			var extension = Path.GetExtension(GetSavePath());
			var recoveryPath = SaveGameBasePath + ".recovery" + extension;
			var globalRecoveryPath = ProjectSettings.GlobalizePath(recoveryPath);
			try
			{
				FsHelpers.EnsureNoReparsePoints(globalRecoveryPath);
				File.Copy(backupPath, globalRecoveryPath, true);
				var recovered = ResourceLoader.Load(recoveryPath);
				if (recovered is not ResourceSaveManager)
				{
					throw new InvalidDataException("The settings backup has an unexpected type.");
				}

				FsHelpers.AtomicReplaceFile(globalRecoveryPath, savePath, retainBackup: false);
				return recovered;
			}
			finally
			{
				if (File.Exists(globalRecoveryPath))
				{
					try
					{
						FsHelpers.EnsureNoReparsePoints(globalRecoveryPath);
						File.Delete(globalRecoveryPath);
					}
					catch (Exception cleanupError)
					{
						Tools.Instance?.AddError($"Settings recovery temporary-file cleanup failed: {cleanupError.Message}");
					}
				}
			}
		}
		catch (Exception recoveryError)
		{
			Tools.Instance?.AddError($"Could not load settings safely: {currentLoadError?.Message ?? "invalid save"}; backup recovery failed: {recoveryError.Message}");
			return null;
		}
	}


	private static string GetSavePath()
	{
		var extension = OS.IsDebugBuild() ? ".tres" : ".res";
		return SaveGameBasePath + extension;
	}


	private static bool CopyLegacySave()
	{
		var currentSavePath = ProjectSettings.GlobalizePath(GetSavePath());
		var currentDirectory = Path.GetDirectoryName(currentSavePath);
		if (string.IsNullOrEmpty(currentDirectory))
		{
			return false;
		}

		var legacyDirectory = Path.Join(Path.GetDirectoryName(currentDirectory), LegacyAppName);
		var legacySavePath = Path.Join(legacyDirectory, Path.GetFileName(currentSavePath));
		try
		{
			if (!File.Exists(legacySavePath))
			{
				return false;
			}

			FsHelpers.EnsureNoReparsePoints(legacySavePath);
			Directory.CreateDirectory(currentDirectory);
			FsHelpers.EnsureNoReparsePoints(currentDirectory);
			var temporaryPath = currentSavePath + ".legacy-" + Guid.NewGuid().ToString("N");
			try
			{
				File.Copy(legacySavePath, temporaryPath, false);
				FsHelpers.AtomicReplaceFile(temporaryPath, currentSavePath, retainBackup: false);
				return true;
			}
			finally
			{
				if (File.Exists(temporaryPath))
				{
					try
					{
						FsHelpers.EnsureNoReparsePoints(temporaryPath);
						File.Delete(temporaryPath);
					}
					catch
					{
						// Preserve the source and leave an unsafe replacement untouched.
					}
				}
			}
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}
}
