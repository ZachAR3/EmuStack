using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using CancellationToken = System.Threading.CancellationToken;


public partial class StandardModManagement : Node
{
	public HttpRequest DownloadRequester;
	public Timer DownloadUpdateTimer;


	public Task<bool> InstallMod(
		string gameId,
		Mod mod,
		Dictionary<string, List<Mod>> installedMods,
		Dictionary<string, List<Mod>> sourceMods,
		Func<bool> persist,
		CancellationToken cancellationToken = default)
	{
		return InstallOrUpdate(gameId, mod, installedMods, sourceMods, persist, null, cancellationToken);
	}


	public Task<bool> UpdateMod(
		string gameId,
		Mod mod,
		Dictionary<string, List<Mod>> installedMods,
		Dictionary<string, List<Mod>> sourceMods,
		Func<bool> persist,
		CancellationToken cancellationToken = default)
	{
		return InstallOrUpdate(gameId, mod, installedMods, sourceMods, persist, mod, cancellationToken);
	}


	public async Task<bool> DeleteMod(
		string gameId,
		Mod mod,
		Dictionary<string, List<Mod>> installedMods,
		Dictionary<string, List<Mod>> sourceMods,
		Func<bool> persist,
		bool noConfirmation = false,
		CancellationToken cancellationToken = default)
	{
		if (!noConfirmation && !await Tools.Instance.ConfirmationPopup($"Delete {mod.ModName}?"))
		{
			return false;
		}
		cancellationToken.ThrowIfCancellationRequested();

		var providerId = Globals.Instance.AppMode.Id;
		var gameRoot = ModPaths.GameRoot(Globals.Instance.CurrentProviderSettings.ModsLocation, gameId);
		var ownerId = ModPaths.OwnerId(providerId, gameId, mod);
		var installedPath = mod.InstalledPath;
		var operationOwner = $"operation:{Guid.NewGuid():N}";
		var operationRoot = Path.Join(gameRoot, $".emustack-delete-{Guid.NewGuid():N}");
		var quarantine = Path.Join(operationRoot, "quarantine");
		var preserveOperationForRecovery = false;

		try
		{
			FsHelpers.RejectDangerousRoot(gameRoot);
			FsHelpers.EnsureNoReparsePoints(gameRoot);
			Directory.CreateDirectory(gameRoot);
			FsHelpers.EnsureNoReparsePoints(gameRoot);
			ValidateOwnedModPath(gameRoot, installedPath, ownerId);
			FsHelpers.EnsureNoReparsePoints(operationRoot);
			Directory.CreateDirectory(operationRoot);
			FsHelpers.MarkOwnedDirectory(operationRoot, operationOwner);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "install.path"), installedPath);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "owner"), ownerId);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "mod.json"), JsonSerializer.Serialize(mod));
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "staging");
			Directory.Move(installedPath, quarantine);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "filesystem-committed");
			cancellationToken.ThrowIfCancellationRequested();
			mod.InstalledPath = installedPath;
			RemoveInstalledMod(gameId, mod, installedMods);
			AddSourceMod(gameId, mod, sourceMods);

			if (!persist())
			{
				throw new IOException("Failed to persist the mod index.");
			}
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "state-committed");

			FsHelpers.DeleteOwnedDirectory(quarantine, operationRoot, ownerId);
			return true;
		}
		catch (OperationCanceledException)
		{
			if (!RestoreDeletedMod(gameId, mod, installedMods, sourceMods, installedPath, quarantine, ownerId, persist))
			{
				preserveOperationForRecovery = true;
				Tools.Instance?.AddError($"Mod deletion was interrupted; recovery is pending for '{mod.ModName}'.");
			}
			return false;
		}
		catch (Exception removeError)
		{
			if (!RestoreDeletedMod(gameId, mod, installedMods, sourceMods, installedPath, quarantine, ownerId, persist))
			{
				preserveOperationForRecovery = true;
				Tools.Instance?.AddError($"Mod deletion rollback is pending for '{mod.ModName}'.");
			}
			Tools.Instance?.AddError($"Failed to remove mod '{mod.ModName}' from '{gameId}': {removeError.Message}");
			return false;
		}
		finally
		{
			if (!preserveOperationForRecovery && !Directory.Exists(quarantine) && Directory.Exists(operationRoot) &&
				FsHelpers.IsOwnedDirectory(operationRoot, operationOwner))
			{
				try
				{
					FsHelpers.DeleteOwnedDirectory(operationRoot, gameRoot, operationOwner);
				}
				catch (Exception cleanupError)
				{
					Tools.Instance?.AddError($"Mod deletion cleanup failed for '{mod.ModName}': {cleanupError.Message}");
				}
			}
		}
	}


	private static bool RestoreDeletedMod(
		string gameId,
		Mod mod,
		Dictionary<string, List<Mod>> installedMods,
		Dictionary<string, List<Mod>> sourceMods,
		string installedPath,
		string quarantine,
		string ownerId,
		Func<bool> persist)
	{
		try
		{
			if (Directory.Exists(quarantine))
			{
				if (!FsHelpers.IsOwnedDirectory(quarantine, ownerId))
				{
					return false;
				}
				var gameRoot = ModPaths.GameRoot(Globals.Instance.CurrentProviderSettings.ModsLocation, gameId);
				FsHelpers.EnsureNoReparsePoints(gameRoot);
				FsHelpers.EnsureNoReparsePoints(quarantine);
				if (!FsHelpers.IsPathContainedBy(gameRoot, installedPath))
				{
					return false;
				}
				FsHelpers.EnsureNoReparsePoints(Path.GetDirectoryName(FsHelpers.CanonicalPath(installedPath)));
				if (Directory.Exists(installedPath))
				{
					return false;
				}
				Directory.Move(quarantine, installedPath);
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

		mod.InstalledPath = installedPath;
		RecordInstalledMod(gameId, mod, installedMods, sourceMods);
		return persist();
	}


	private async Task<bool> InstallOrUpdate(
		string gameId,
		Mod mod,
		Dictionary<string, List<Mod>> installedMods,
		Dictionary<string, List<Mod>> sourceMods,
		Func<bool> persist,
		Mod existingMod,
		CancellationToken cancellationToken)
	{
		var providerId = Globals.Instance.AppMode.Id;
		var modsLocation = Globals.Instance.CurrentProviderSettings.ModsLocation;
		var gameRoot = ModPaths.GameRoot(modsLocation, gameId);
		var installPath = ModPaths.ManagedFolder(modsLocation, gameId, mod);
		var ownerId = ModPaths.OwnerId(providerId, gameId, mod);
		var operationOwner = $"operation:{Guid.NewGuid():N}";
		var operationRoot = Path.Join(gameRoot, $".emustack-operation-{Guid.NewGuid():N}");
		var downloadPath = Path.Join(operationRoot, "download.part");
		var stagedPath = Path.Join(operationRoot, "staged");
		var backupPath = Path.Join(operationRoot, "previous");
		var previousPath = existingMod?.InstalledPath;
		var committed = false;
		var metadataChanged = false;
		var preserveOperationForRecovery = false;
		var oldInstalledPath = mod.InstalledPath;

		try
		{
			FsHelpers.RejectDangerousRoot(gameRoot);
			FsHelpers.EnsureNoReparsePoints(gameRoot);
			Directory.CreateDirectory(gameRoot);
			FsHelpers.EnsureNoReparsePoints(gameRoot);
			if (!FsHelpers.IsPathContainedBy(modsLocation, gameRoot))
			{
				throw new InvalidOperationException("Game mod directory is outside the configured mod root.");
			}

			if (existingMod != null)
			{
				ValidateOwnedModPath(gameRoot, previousPath, ownerId);
			}
			else if (Directory.Exists(installPath))
			{
				throw new IOException($"A directory already exists for mod '{mod.ModName}'.");
			}

			FsHelpers.EnsureNoReparsePoints(operationRoot);
			Directory.CreateDirectory(operationRoot);
			FsHelpers.MarkOwnedDirectory(operationRoot, operationOwner);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "install.path"), installPath);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "previous.path"), previousPath ?? "");
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "owner"), ownerId);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "mod.json"), JsonSerializer.Serialize(mod));
			if (existingMod != null)
			{
				FsHelpers.AtomicWriteText(Path.Join(operationRoot, "previous.mod.json"), JsonSerializer.Serialize(existingMod));
			}
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "staging");
			FsHelpers.EnsureNoReparsePoints(downloadPath);
			ValidateHttpUrl(mod.ModUrl);
			DownloadRequester.DownloadFile = downloadPath;
			var requestError = DownloadRequester.Request(mod.ModUrl);
			if (requestError != Error.Ok)
			{
				throw new InvalidOperationException($"Failed to start mod download: {requestError}");
			}

			DownloadUpdateTimer.Start();
			var completion = await ToSignal(DownloadRequester, HttpRequest.SignalName.RequestCompleted);
			DownloadUpdateTimer.Stop();
			if (cancellationToken.IsCancellationRequested)
			{
				return false;
			}
			var result = completion[0].AsInt64();
			var responseCode = completion[1].AsInt64();
			if (result != (long)HttpRequest.Result.Success || responseCode is < 200 or >= 300 ||
			    !File.Exists(downloadPath) || new FileInfo(downloadPath).Length is <= 0 or > SafeArchive.MaximumDownloadBytes)
			{
				throw new IOException($"Download failed (result {result}, HTTP {responseCode}).");
			}

			FsHelpers.EnsureNoReparsePoints(stagedPath);
			Directory.CreateDirectory(stagedPath);
			await Task.Run(() => SafeArchive.Extract(downloadPath, stagedPath, cancellationToken), cancellationToken);
			FsHelpers.MarkOwnedDirectory(stagedPath, ownerId);
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "swapping");

			if (existingMod != null)
			{
				Directory.Move(previousPath, backupPath);
			}
			Directory.Move(stagedPath, installPath);
			committed = true;
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "filesystem-committed");

			mod.InstalledPath = installPath;
			RecordInstalledMod(gameId, mod, installedMods, sourceMods);
			metadataChanged = true;
			if (!persist())
			{
				mod.InstalledPath = oldInstalledPath;
				throw new IOException("Failed to persist the mod index.");
			}
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "state-committed");

			if (Directory.Exists(backupPath))
			{
				try
				{
					if (!FsHelpers.IsOwnedDirectory(installPath, ownerId))
					{
						throw new IOException("The committed mod directory is no longer owned by EmuStack.");
					}
					FsHelpers.DeleteOwnedDirectory(backupPath, operationRoot, ownerId);
				}
				catch (Exception cleanupError)
				{
					// The new install and metadata are committed. Leave the journal and
					// previous copy for startup recovery instead of rolling back a
					// successful update because cleanup was temporarily blocked.
					preserveOperationForRecovery = true;
					Tools.Instance?.AddError($"Mod update cleanup is pending for '{mod.ModName}': {cleanupError.Message}");
				}
			}
			return true;
		}
		catch (OperationCanceledException)
		{
			if (!TryBeginRollback(operationRoot, operationOwner))
			{
				preserveOperationForRecovery = true;
				Tools.Instance?.AddError($"Mod update cancellation requires recovery for '{mod.ModName}'.");
				return false;
			}
			if (!TryRollbackFilesystem(committed, installPath, gameRoot, ownerId, backupPath, previousPath))
			{
				preserveOperationForRecovery = true;
				Tools.Instance?.AddError($"Mod update cancellation requires recovery for '{mod.ModName}'.");
				return false;
			}
			if (!RestoreMetadataAfterRollback(
				metadataChanged, gameId, mod, existingMod, oldInstalledPath,
				installedMods, sourceMods, persist))
			{
				preserveOperationForRecovery = true;
			}
			return false;
		}
		catch (Exception installError)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				if (!TryBeginRollback(operationRoot, operationOwner) ||
					!TryRollbackFilesystem(committed, installPath, gameRoot, ownerId, backupPath, previousPath))
				{
					preserveOperationForRecovery = true;
					Tools.Instance?.AddError($"Mod update cancellation requires recovery for '{mod.ModName}'.");
					return false;
				}
				if (!RestoreMetadataAfterRollback(
					metadataChanged, gameId, mod, existingMod, oldInstalledPath,
					installedMods, sourceMods, persist))
				{
					preserveOperationForRecovery = true;
				}
				return false;
			}
			if (!TryBeginRollback(operationRoot, operationOwner))
			{
				preserveOperationForRecovery = true;
				Tools.Instance?.AddError($"Mod update rollback is pending for '{mod.ModName}'.");
				return false;
			}
			if (!TryRollbackFilesystem(committed, installPath, gameRoot, ownerId, backupPath, previousPath))
			{
				preserveOperationForRecovery = true;
				Tools.Instance?.AddError($"Mod update rollback is pending for '{mod.ModName}'.");
				return false;
			}
			if (!RestoreMetadataAfterRollback(
				metadataChanged, gameId, mod, existingMod, oldInstalledPath,
				installedMods, sourceMods, persist))
			{
				preserveOperationForRecovery = true;
			}
			Tools.Instance?.AddError($"Failed to install mod '{mod.ModName}' for '{gameId}': {installError.Message}");
			return false;
		}
		finally
		{
			DownloadUpdateTimer.Stop();
			DownloadRequester.DownloadFile = "";
			if (!preserveOperationForRecovery && Directory.Exists(operationRoot) && FsHelpers.IsOwnedDirectory(operationRoot, operationOwner))
			{
				try
				{
					FsHelpers.DeleteOwnedDirectory(operationRoot, gameRoot, operationOwner);
				}
				catch (Exception cleanupError)
				{
					Tools.Instance?.AddError($"Mod operation cleanup failed for '{mod.ModName}': {cleanupError.Message}");
				}
			}
		}
	}


	private static bool TryBeginRollback(string operationRoot, string operationOwner)
	{
		try
		{
			if (!Directory.Exists(operationRoot) || !FsHelpers.IsOwnedDirectory(operationRoot, operationOwner))
			{
				return false;
			}
			FsHelpers.AtomicWriteText(Path.Join(operationRoot, "phase"), "rollback");
			return true;
		}
		catch
		{
			return false;
		}
	}


	private static bool TryRollbackFilesystem(
		bool committed,
		string installPath,
		string gameRoot,
		string ownerId,
		string backupPath,
		string previousPath)
	{
		try
		{
			if (committed && Directory.Exists(installPath))
			{
				if (!FsHelpers.IsOwnedDirectory(installPath, ownerId))
				{
					throw new IOException("Refusing to roll back an unowned mod directory.");
				}
				FsHelpers.DeleteOwnedDirectory(installPath, gameRoot, ownerId);
			}
			if (Directory.Exists(backupPath))
			{
				if (!FsHelpers.IsOwnedDirectory(backupPath, ownerId))
				{
					throw new IOException("Refusing to restore an unowned mod backup.");
				}
				if (string.IsNullOrEmpty(previousPath) || Directory.Exists(previousPath))
				{
					throw new IOException("Cannot restore the previous mod because the target is occupied.");
				}
				Directory.Move(backupPath, previousPath);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}


	private static bool RestoreMetadataAfterRollback(
		bool metadataChanged,
		string gameId,
		Mod mod,
		Mod existingMod,
		string oldInstalledPath,
		Dictionary<string, List<Mod>> installedMods,
		Dictionary<string, List<Mod>> sourceMods,
		Func<bool> persist)
	{
		if (!metadataChanged)
		{
			return true;
		}

		if (existingMod != null)
		{
			mod.InstalledPath = oldInstalledPath;
			RecordInstalledMod(gameId, existingMod, installedMods, sourceMods);
		}
		else
		{
			RemoveInstalledMod(gameId, mod, installedMods);
			AddSourceMod(gameId, mod, sourceMods);
		}

		if (!persist())
		{
			Tools.Instance?.AddError($"Mod metadata rollback is pending for '{mod.ModName}'.");
			return false;
		}

		return true;
	}


	private static void ValidateOwnedModPath(string gameRoot, string installedPath, string ownerId)
	{
		if (string.IsNullOrWhiteSpace(installedPath) || !FsHelpers.IsPathContainedBy(gameRoot, installedPath) ||
		    !FsHelpers.IsOwnedDirectory(installedPath, ownerId))
		{
			throw new InvalidOperationException("The selected mod is unmanaged or outside the current game root; EmuStack will not delete it.");
		}
		FsHelpers.EnsureNoReparseTree(installedPath);
	}


	private static void ValidateHttpUrl(string value)
	{
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			throw new ArgumentException("Mod download URL must use HTTP or HTTPS.");
		}
	}


	public static void RecordInstalledMod(string gameId, Mod mod, Dictionary<string, List<Mod>> installedMods, Dictionary<string, List<Mod>> sourceMods)
	{
		if (!installedMods.TryGetValue(gameId, out var gameInstalledMods))
		{
			gameInstalledMods = new List<Mod>();
			installedMods[gameId] = gameInstalledMods;
		}
		if (!gameInstalledMods.Any(item => ModPaths.StableId(item) == ModPaths.StableId(mod)))
		{
			gameInstalledMods.Add(mod);
		}

		if (sourceMods.TryGetValue(gameId, out var gameSourceMods))
		{
			gameSourceMods.RemoveAll(item => ModPaths.StableId(item) == ModPaths.StableId(mod));
		}
	}


	private static void RemoveInstalledMod(string gameId, Mod mod, Dictionary<string, List<Mod>> installedMods)
	{
		if (installedMods.TryGetValue(gameId, out var gameInstalledMods))
		{
			gameInstalledMods.RemoveAll(item => ModPaths.StableId(item) == ModPaths.StableId(mod));
		}
	}


	private static void AddSourceMod(string gameId, Mod mod, Dictionary<string, List<Mod>> sourceMods)
	{
		if (mod.ModUrl == null)
		{
			return;
		}

		if (!sourceMods.TryGetValue(gameId, out var gameSourceMods))
		{
			gameSourceMods = new List<Mod>();
			sourceMods[gameId] = gameSourceMods;
		}
		if (!gameSourceMods.Any(item => ModPaths.StableId(item) == ModPaths.StableId(mod)))
		{
			gameSourceMods.Add(mod);
		}
	}
}
