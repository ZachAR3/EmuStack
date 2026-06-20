using Godot;
using System;
using System.Diagnostics;
using System.IO;
using WindowsShortcutFactory;


public static class ShortcutFactory
{
	public static void Create(Mode mode, OsKind os, string executable, string launcherFlag, string saveDirectory, Image icon)
	{
		switch (os)
		{
			case OsKind.Linux:
				CreateLinux(mode, executable, launcherFlag, saveDirectory, icon);
				break;
			case OsKind.Windows:
				CreateWindows(mode, executable, launcherFlag, saveDirectory);
				break;
			default:
				Tools.Instance.AddError("Shortcut creation is not supported on this platform.");
				break;
		}
	}


	private static void CreateLinux(Mode mode, string executable, string launcherFlag, string saveDirectory, Image icon)
	{
		var shortcutName = $"{mode.Id}.desktop";
		var iconPath = Path.Join(saveDirectory, "Icon.png");
		icon.SavePng(iconPath);

		var shortcutContent = $@"
[Desktop Entry]
Comment={mode.DesktopEntryComment}
Exec={executable} {launcherFlag}
GenericName=Switch Emulator
Icon={iconPath}
MimeType=
Name={mode.Name}
Path=
StartupNotify=true
Terminal=false
TerminalOptions=
Type=Application
Keywords=Nintendo;Switch;
Categories={mode.DesktopEntryCategories}
";

		const string systemApplicationsPath = "/usr/share/applications/";
		if (!Directory.Exists(systemApplicationsPath))
		{
			Tools.Instance.AddError("Cannot find shortcut directory, please place manually.");
			return;
		}

		var shortcutPath = Path.Join(systemApplicationsPath, shortcutName);
		try
		{
			var tempShortcutPath = Path.Join(saveDirectory, shortcutName);
			File.WriteAllText(tempShortcutPath, shortcutContent);
			using var process = Process.Start(new ProcessStartInfo
			{
				FileName = "pkexec",
				Arguments = $"mv {tempShortcutPath} {shortcutPath}",
				UseShellExecute = false,
			});
			process?.WaitForExit();
		}
		catch (Exception shortcutError)
		{
			var fallbackPath = Path.Join(saveDirectory, shortcutName);
			Tools.Instance.AddError(
				$@"Error creating shortcut, creating new at {fallbackPath}. Error:{shortcutError}");
			File.WriteAllText(fallbackPath, shortcutContent);
		}
	}


	private static void CreateWindows(Mode mode, string executable, string launcherFlag, string saveDirectory)
	{
		var shortcutName = $"{mode.Id}.lnk";
		var commonStartMenuPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonStartMenu);
		var emulatorStartMenuPath = Path.Combine(commonStartMenuPath, "Programs", "EmuStack", mode.Name);
		var emulatorShortcutPath = Path.Combine(emulatorStartMenuPath, shortcutName);

		var windowsShortcut = new WindowsShortcut
		{
			Path = executable,
			IconLocation = executable,
			Arguments = launcherFlag,
		};

		try
		{
			Directory.CreateDirectory(emulatorStartMenuPath);
			windowsShortcut.Save(emulatorShortcutPath);
		}
		catch (Exception shortcutError)
		{
			var fallbackPath = Path.Join(saveDirectory, shortcutName);
			Tools.Instance.AddError(
				$@"cannot create shortcut, ensure app is running as admin. Placing instead at {fallbackPath}. Exception:{shortcutError}");
			windowsShortcut.Save(fallbackPath);
		}
	}
}
