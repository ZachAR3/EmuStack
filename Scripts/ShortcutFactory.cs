using Godot;
using System;
using System.IO;
using WindowsShortcutFactory;


public static class ShortcutFactory
{
	public static void Create(Mode mode, OsKind os, string executable, string launcherFlag, string saveDirectory, Image icon)
	{
		try
		{
			var safeSaveDirectory = FsHelpers.CanonicalPath(saveDirectory);
			FsHelpers.RejectDangerousRoot(safeSaveDirectory);
			FsHelpers.EnsureNoReparsePoints(safeSaveDirectory);
			Directory.CreateDirectory(safeSaveDirectory);
			FsHelpers.EnsureNoReparsePoints(safeSaveDirectory);

			switch (os)
			{
				case OsKind.Linux:
					CreateLinux(mode, executable, launcherFlag, safeSaveDirectory, icon);
					break;
				case OsKind.Windows:
					CreateWindows(mode, executable, launcherFlag, safeSaveDirectory);
					break;
				default:
					Tools.Instance?.AddError("Shortcut creation is not supported on this platform.");
					break;
			}
		}
		catch (Exception shortcutError)
		{
			Tools.Instance?.AddError($"Shortcut creation failed: {shortcutError.Message}");
		}
	}


	private static void CreateLinux(Mode mode, string executable, string launcherFlag, string saveDirectory, Image icon)
	{
		var shortcutName = $"{mode.Id}.desktop";
		var iconPath = Path.Join(saveDirectory, "Icon.png");

		var arguments = string.IsNullOrWhiteSpace(launcherFlag) ? "" : " " + QuoteDesktopExecArg(launcherFlag);
		var shortcutContent = $@"
[Desktop Entry]
Comment={mode.DesktopEntryComment}
Exec={QuoteDesktopExecArg(executable)}{arguments}
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

		var dataHome = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
		if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathRooted(dataHome))
		{
			dataHome = Path.Join(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local", "share");
		}
		var applicationsPath = FsHelpers.CanonicalPath(Path.Join(dataHome, "applications"));
		FsHelpers.RejectDangerousRoot(applicationsPath);
		var shortcutPath = Path.Join(applicationsPath, shortcutName);
		string iconTemporary = null;
		try
		{
			iconTemporary = iconPath + ".tmp-" + Guid.NewGuid().ToString("N");
			FsHelpers.EnsureNoReparsePoints(iconPath);
			FsHelpers.EnsureNoReparsePoints(iconTemporary);
			icon.SavePng(iconTemporary);
			FsHelpers.AtomicReplaceFile(iconTemporary, iconPath);
			Directory.CreateDirectory(applicationsPath);
			FsHelpers.AtomicWriteText(shortcutPath, shortcutContent);
		}
		catch (Exception shortcutError)
		{
			var fallbackPath = Path.Join(saveDirectory, shortcutName);
			Tools.Instance?.AddError(
				$@"Error creating shortcut, creating new at {fallbackPath}. Error:{shortcutError}");
			try
			{
				FsHelpers.AtomicWriteText(fallbackPath, shortcutContent);
			}
			catch (Exception fallbackError)
			{
				Tools.Instance?.AddError($"Shortcut fallback failed: {fallbackError.Message}");
			}
		}
		finally
		{
			if (!string.IsNullOrEmpty(iconTemporary) && File.Exists(iconTemporary))
			{
				try
				{
					FsHelpers.EnsureNoReparsePoints(iconTemporary);
					File.Delete(iconTemporary);
				}
				catch (Exception cleanupError)
				{
					Tools.Instance?.AddError($"Shortcut temporary-file cleanup failed: {cleanupError.Message}");
				}
			}
		}
	}


	private static string QuoteDesktopExecArg(string value)
	{
		var escaped = value
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("`", "\\`")
			.Replace("$", "\\$")
			.Replace("%", "%%");
		return $"\"{escaped}\"";
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
			FsHelpers.EnsureNoReparsePoints(emulatorStartMenuPath);
			Directory.CreateDirectory(emulatorStartMenuPath);
			FsHelpers.EnsureNoReparsePoints(emulatorStartMenuPath);
			FsHelpers.EnsureNoReparsePoints(emulatorShortcutPath);
			windowsShortcut.Save(emulatorShortcutPath);
		}
		catch (Exception shortcutError)
		{
			var fallbackPath = Path.Join(saveDirectory, shortcutName);
			Tools.Instance?.AddError(
				$@"cannot create shortcut, ensure app is running as admin. Placing instead at {fallbackPath}. Exception:{shortcutError}");
			try
			{
				FsHelpers.EnsureNoReparsePoints(fallbackPath);
				windowsShortcut.Save(fallbackPath);
			}
			catch (Exception fallbackError)
			{
				Tools.Instance?.AddError($"Shortcut fallback failed: {fallbackError.Message}");
			}
		}
	}
}
