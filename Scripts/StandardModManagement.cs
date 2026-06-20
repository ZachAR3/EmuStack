using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using SharpCompress.Archives;
using SharpCompress.Common;


public partial class StandardModManagement : Node
{
	public HttpRequest DownloadRequester;
	public Timer DownloadUpdateTimer;


	public async Task<bool> InstallMod(string gameId, Mod mod, Dictionary<string, List<Mod>> installedMods, Dictionary<string, List<Mod>> sourceMods)
	{
		try
		{
			var modsLocation = Globals.Instance.CurrentProviderSettings.ModsLocation;
			var downloadPath = ModPaths.DownloadArchive(modsLocation, gameId, mod.ModName);
			var installPath = ModPaths.ManagedFolder(modsLocation, gameId, mod.ModName);
			var stagingPath = ModPaths.TempStaging(installPath);

			DownloadRequester.DownloadFile = downloadPath;
			var requestError = DownloadRequester.Request(mod.ModUrl);
			if (requestError != Error.Ok)
			{
				throw new InvalidOperationException($@"Failed to start mod download: {requestError}");
			}
			DownloadUpdateTimer.Start();
			await ToSignal(DownloadRequester, "request_completed");
			DownloadUpdateTimer.Stop();

			await using (var stream = File.OpenRead(downloadPath))
			{
				var reader = ArchiveFactory.OpenArchive(stream);
				Directory.CreateDirectory(stagingPath);

				foreach (var entry in reader.Entries)
				{
					if (!entry.IsDirectory)
					{
						entry.WriteToDirectory(stagingPath, new ExtractionOptions
						{
							ExtractFullPath = true,
							Overwrite = true
						});
					}
				}
			}

			if (Directory.Exists(installPath))
			{
				FsHelpers.DeleteDirectoryContents(installPath);
			}
			FsHelpers.MoveFilesAndDirs(stagingPath, installPath);

			if (Directory.Exists(stagingPath))
			{
				Directory.Delete(stagingPath, true);
			}
			File.Delete(downloadPath);

			mod.InstalledPath = installPath;
			RecordInstalledMod(gameId, mod, installedMods, sourceMods);
		}
		catch (Exception installError)
		{
			Tools.Instance.AddError($@"failed to install mod:{installError}");
			return false;
		}

		return true;
	}


	public async Task<bool> DeleteMod(string gameId, Mod mod, Dictionary<string, List<Mod>> installedMods, Dictionary<string, List<Mod>> sourceMods, bool noConfirmation = false)
	{
		try
		{
			if (!noConfirmation)
			{
				if (!await Tools.Instance.ConfirmationPopup($@"Delete {mod.ModName}?"))
				{
					return false;
				}
			}

			installedMods[gameId].Remove(mod);

			// If the mod is available online re-add it to the source list.
			if (mod.ModUrl != null)
			{
				if (!sourceMods.TryGetValue(gameId, out var gameSourceMods))
				{
					gameSourceMods = new List<Mod>();
					sourceMods[gameId] = gameSourceMods;
				}
				gameSourceMods.Add(mod);
			}

			FsHelpers.DeleteDirectoryContents(mod.InstalledPath);
			Directory.Delete(mod.InstalledPath, true);
		}
		catch (Exception removeError)
		{
			Tools.Instance.AddError("failed to remove mod:" + removeError);
			return false;
		}

		return true;
	}


	public static void RecordInstalledMod(string gameId, Mod mod, Dictionary<string, List<Mod>> installedMods, Dictionary<string, List<Mod>> sourceMods)
	{
		if (!installedMods.TryGetValue(gameId, out var gameInstalledMods))
		{
			gameInstalledMods = new List<Mod>();
			installedMods[gameId] = gameInstalledMods;
		}
		gameInstalledMods.Add(mod);

		if (sourceMods.TryGetValue(gameId, out var gameSourceMods))
		{
			gameSourceMods.Remove(mod);
		}
	}
}

