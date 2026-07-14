using System;
using System.IO;


public static class ModPaths
{
	public const string ManagedPrefix = "Managed-";
	public const string OwnerPrefix = "mod:";
	public static string GameRoot(string modsLocation, string gameId)
		=> Path.Join(modsLocation, gameId);


	public static string ManagedFolder(string modsLocation, string gameId, Mod mod)
		=> Path.Join(GameRoot(modsLocation, gameId), $"{ManagedPrefix}{StableId(mod)}-{Sanitize(mod.ModName)}");


	public static bool IsManaged(string folderPath)
	{
		if (!Directory.Exists(folderPath) || !File.Exists(Path.Join(folderPath, FsHelpers.OwnerMarkerFileName)))
		{
			return false;
		}

		try
		{
			FsHelpers.EnsureNoReparsePoints(folderPath);
			FsHelpers.EnsureNoReparsePoints(Path.Join(folderPath, FsHelpers.OwnerMarkerFileName));
			return true;
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


	public static string OwnerId(string providerId, string gameId, Mod mod)
		=> $"{OwnerPrefix}{providerId}:{gameId}:{StableId(mod)}";


	public static string StableId(Mod mod)
	{
		var value = mod.StableId;
		if (string.IsNullOrWhiteSpace(value))
		{
			value = mod.ModId;
		}
		if (string.IsNullOrWhiteSpace(value))
		{
			value = Guid.NewGuid().ToString("N");
		}

		var sanitized = Sanitize(value).ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
		{
			sanitized = Guid.NewGuid().ToString("N");
		}
		mod.StableId = sanitized;
		return mod.StableId;
	}


	public static string Sanitize(string modName)
	{
		var safeName = modName ?? "";
		foreach (var invalidChar in Path.GetInvalidFileNameChars())
		{
			safeName = safeName.Replace(invalidChar, '.');
		}

		return safeName.TrimEnd(' ', '.');
	}
}
