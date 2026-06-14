using Godot;
using System;
using System.IO;

public partial class ResourceSaveManager : Resource
{
    public const String SaveGameBasePath = "user://InternalSave";
    private const string LegacyAppName = "YuzuToolbox";
	
    [Export()] public float Version;
    [Export()] public SettingsResource _settings;

    public void WriteSave(SettingsResource settings = null)
    {
        // If no settings are passed, use the current settings
        _settings = settings ?? _settings;
        ResourceSaver.Save(this, GetSavePath());
    }
	

    private static bool SaveExists()
    {
        return ResourceLoader.Exists(GetSavePath());
    }

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


    public SettingsResource GetSettings()
    {
        if (SaveExists())
        {
            var lastSave = (ResourceSaveManager)LoadSave();
			
            // If the save is from a previous version, reset the settings
            if (lastSave.Version != Version)
            {
                Tools.Instance.CallDeferred("AddError", "Save version is incompatible with app, resetting user settings.");
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
	

    static String GetSavePath()
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
