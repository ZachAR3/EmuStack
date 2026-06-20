using System.IO;


public static class ModPaths
{
	public const string ManagedPrefix = "Managed";
	private const string DownloadSuffix = "-Download";
	private const string TempSuffix = "-temp";


	public static string GameRoot(string modsLocation, string gameId)
		=> Path.Join(modsLocation, gameId);


	public static string ManagedFolder(string modsLocation, string gameId, string modName)
		=> Path.Join(GameRoot(modsLocation, gameId), $"{ManagedPrefix}{Sanitize(modName)}");


	public static string DownloadArchive(string modsLocation, string gameId, string modName)
		=> Path.Join(GameRoot(modsLocation, gameId), $"{Sanitize(modName)}{DownloadSuffix}");


	public static string TempStaging(string installPath) => installPath + TempSuffix;


	public static bool IsManaged(string folderName) => folderName.StartsWith(ManagedPrefix);


	private static string Sanitize(string modName)
	{
		var safeName = modName ?? "";
		foreach (var invalidChar in Path.GetInvalidFileNameChars())
		{
			safeName = safeName.Replace(invalidChar, '.');
		}

		return safeName.TrimEnd(' ', '.');
	}
}
