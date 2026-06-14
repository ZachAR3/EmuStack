using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SharpCompress.Archives;
using SharpCompress.Common;


public partial class StandardModManagement : Node
{
	public Dictionary<string, List<Mod>> InstalledMods;
	public Dictionary<string, List<Mod>> SelectedSourceMods;

	public HttpRequest DownloadRequester;
    public Timer DownloadUpdateTimer;


    public async Task<bool> InstallMod(string gameId, Mod mod)
	{
		try
		{
			string downloadPath = Path
				.Join(Globals.Instance.CurrentProviderSettings.ModsLocation, gameId, $@"{mod.ModName.Replace(":", ".")}-Download");
			string installPath = Path
				.Join(Globals.Instance.CurrentProviderSettings.ModsLocation, gameId, $@"Managed{mod.ModName.Replace(":", ".")}");

			// Downloads the mod zip to the download path
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

				Directory.CreateDirectory(installPath + "-temp");

				foreach (var entry in reader.Entries)
				{
					if (!entry.IsDirectory)
					{
						entry.WriteToDirectory(installPath + "-temp", new ExtractionOptions()
						{
							ExtractFullPath = true,
							Overwrite = true
						});
					}
				}
			}

			if (Directory.Exists(installPath))
			{
				Tools.DeleteDirectoryContents(installPath);
			}
			// Moves the files from the temp folder into the install path
			Tools.MoveFilesAndDirs(installPath + "-temp", installPath);

			// Cleanup
			if (Directory.Exists(installPath + "-temp"))
			{
				Directory.Delete(installPath + "-temp", true);
			}
			File.Delete(downloadPath);

			// Sets the installed path and initializes the installed mods list for the given game if needed
			mod.InstalledPath = installPath;
			InstalledMods[gameId] = !InstalledMods.ContainsKey(gameId)
				? new List<Mod>()
				: InstalledMods[gameId];

			InstalledMods[gameId].Add(mod);
			if (SelectedSourceMods.ContainsKey(gameId))
			{
				SelectedSourceMods[gameId].Remove(mod);
			}
		}
		catch (Exception installError)
		{
			Tools.Instance.AddError($@"failed to install mod:{installError}");
			return false;
		}
		
		return true;
	}
	
	
	public async Task<bool> DeleteMod(string gameId, Mod mod, int source, int sourcesAll, bool noConfirmation = false)
	{
		try
		{
			if (!noConfirmation)
			{
				var confirm = await Tools.Instance.ConfirmationPopup($@"Delete {mod.ModName}?");
				if (confirm == false)
				{
					return false;
				}
			}
			
			InstalledMods[gameId].Remove(mod);
			
			// If the mod is available online re-adds it to the source list
			if (mod.ModUrl != null && (source == mod.Source || source == sourcesAll))
			{
				// If there is no mod list for the game id creates one
				SelectedSourceMods[gameId] =
					!SelectedSourceMods.ContainsKey(gameId) ? new List<Mod>() : SelectedSourceMods[gameId];
				SelectedSourceMods[gameId].Add(mod);
			}

			// Deletes directory contents, then the directory itself.
			Tools.DeleteDirectoryContents(mod.InstalledPath);
			Directory.Delete(mod.InstalledPath, true);
		}
		catch (Exception removeError)
		{
			Tools.Instance.AddError("failed to remove mod:" + removeError);
			return false;
		}
		
		return true;
	}
}
