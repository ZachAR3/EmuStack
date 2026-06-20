using Godot;
using System.IO;


public partial class ResourceSaveManager : Resource
{
	public const string SaveGameBasePath = "user://InternalSave";
	private const string LegacyAppName = "YuzuToolbox";

	[Export] public int Version;
	[Export] public SettingsResource _settings;


	public void WriteSave(SettingsResource settings = null)
	{
		_settings = settings ?? _settings;
		ResourceSaver.Save(this, GetSavePath());
	}


	public SettingsResource GetSettings()
	{
		if (SaveExists())
		{
			var lastSave = (ResourceSaveManager)LoadSave();

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
				_settings = lastSave._settings;
			}
		}
		else
		{
			_settings = new SettingsResource();
			WriteSave();
		}

		return _settings;
	}


	private static bool SaveExists() => ResourceLoader.Exists(GetSavePath());


	private static Resource LoadSave()
	{
		if (SaveExists())
		{
			return ResourceLoader.Load(GetSavePath());
		}

		if (CopyLegacySave())
		{
			return ResourceLoader.Load(GetSavePath());
		}

		return null;
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
		if (!File.Exists(legacySavePath))
		{
			return false;
		}

		Directory.CreateDirectory(currentDirectory);
		File.Copy(legacySavePath, currentSavePath, false);
		return true;
	}
}
