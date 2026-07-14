using System;
using System.IO;


public static class FsHelpers
{
	public const string OwnerMarkerFileName = ".emustack-owner";


	public static string CanonicalPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is empty.", nameof(path));
		}
		if (!Path.IsPathRooted(path))
		{
			throw new ArgumentException("Path must be absolute.", nameof(path));
		}

		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
	}


	public static bool IsPathContainedBy(string rootPath, string candidatePath, bool allowRoot = false)
	{
		var root = CanonicalPath(rootPath);
		var candidate = CanonicalPath(candidatePath);
		var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		if (string.Equals(root, candidate, comparison))
		{
			return allowRoot;
		}

		return candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
	}


	public static void EnsureNoReparsePoints(string path, string stopAt = null)
	{
		var current = CanonicalPath(path);
		var stop = string.IsNullOrWhiteSpace(stopAt) ? null : CanonicalPath(stopAt);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				var attributes = File.GetAttributes(current);
				if ((attributes & FileAttributes.ReparsePoint) != 0)
				{
					throw new IOException($"Refusing to follow a link or reparse point: {current}");
				}
			}
			catch (FileNotFoundException)
			{
				// The path component does not exist yet.
			}
			catch (DirectoryNotFoundException)
			{
				// The path component does not exist yet.
			}

			if (stop != null && string.Equals(current, stop, PathComparison))
			{
				break;
			}

			var parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathComparison))
			{
				break;
			}
			current = parent;
		}
	}


	public static void MarkOwnedDirectory(string directoryPath, string ownerId)
	{
		if (string.IsNullOrWhiteSpace(ownerId))
		{
			throw new ArgumentException("Owner ID is empty.", nameof(ownerId));
		}

		var canonicalDirectory = CanonicalPath(directoryPath);
		RejectDangerousRoot(canonicalDirectory);
		EnsureNoReparsePoints(canonicalDirectory);
		Directory.CreateDirectory(canonicalDirectory);
		EnsureNoReparseTree(canonicalDirectory);
		var markerPath = Path.Join(canonicalDirectory, OwnerMarkerFileName);
		if (File.Exists(markerPath))
		{
			RejectReparsePoint(markerPath);
		}
		ReplaceFileContents(markerPath, ownerId);
	}


	public static bool IsOwnedDirectory(string directoryPath, string ownerId)
	{
		if (!Directory.Exists(directoryPath) || string.IsNullOrWhiteSpace(ownerId))
		{
			return false;
		}

		try
		{
			EnsureNoReparsePoints(directoryPath);
			var markerPath = Path.Join(directoryPath, OwnerMarkerFileName);
			if (File.Exists(markerPath))
			{
				RejectReparsePoint(markerPath);
			}
			return File.Exists(markerPath) && string.Equals(File.ReadAllText(markerPath).Trim(), ownerId, StringComparison.Ordinal);
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


	public static void DeleteOwnedDirectory(string directoryPath, string parentRoot, string ownerId)
	{
		var canonicalDirectory = CanonicalPath(directoryPath);
		RejectDangerousRoot(canonicalDirectory);
		if (!IsPathContainedBy(parentRoot, canonicalDirectory) || !IsOwnedDirectory(canonicalDirectory, ownerId))
		{
			throw new InvalidOperationException($"Refusing to delete an unowned directory: {canonicalDirectory}");
		}

		DeleteDirectoryTree(canonicalDirectory);
	}


	public static void DeleteOwnedDirectoryContents(string directoryPath, string ownerId)
	{
		var canonicalDirectory = CanonicalPath(directoryPath);
		RejectDangerousRoot(canonicalDirectory);
		if (!IsOwnedDirectory(canonicalDirectory, ownerId))
		{
			throw new InvalidOperationException($"Refusing to clear an unowned directory: {canonicalDirectory}");
		}
		EnsureNoReparseTree(canonicalDirectory);

		foreach (var entry in Directory.EnumerateFileSystemEntries(canonicalDirectory))
		{
			if (Path.GetFileName(entry) == OwnerMarkerFileName)
			{
				continue;
			}

			DeleteEntry(entry);
		}
	}


	public static void MoveFilesAndDirs(string sourceDirectory, string targetDirectory)
	{
		if (!Directory.Exists(sourceDirectory))
		{
			return;
		}

		EnsureNoReparseTree(sourceDirectory);
		Directory.CreateDirectory(targetDirectory);
		EnsureNoReparseTree(targetDirectory);

		foreach (var file in Directory.GetFiles(sourceDirectory))
		{
			RejectReparsePoint(file);
			var targetPath = Path.Combine(targetDirectory, Path.GetFileName(file));
			File.Move(file, targetPath, true);
		}

		foreach (var directory in Directory.GetDirectories(sourceDirectory))
		{
			RejectReparsePoint(directory);
			var targetPath = Path.Combine(targetDirectory, Path.GetFileName(directory));
			if (!Directory.Exists(targetPath))
			{
				Directory.Move(directory, targetPath);
			}
		}

		if (!Directory.EnumerateFileSystemEntries(sourceDirectory).GetEnumerator().MoveNext())
		{
			Directory.Delete(sourceDirectory);
		}
	}


	public static void TransactionalCopyDirectory(
		string sourceDir,
		string destinationDir,
		bool preserveDestinationFiles,
		string requiredSourceOwner = null,
		string requiredDestinationOwner = null)
	{
		if (!Directory.Exists(sourceDir))
		{
			throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDir}");
		}

		var source = CanonicalPath(sourceDir);
		var destination = CanonicalPath(destinationDir);
		RejectDangerousRoot(source);
		RejectDangerousRoot(destination);
		if (IsPathContainedBy(source, destination, true) || IsPathContainedBy(destination, source, true))
		{
			throw new InvalidOperationException("Source and destination directories must not overlap.");
		}
		if (requiredSourceOwner != null && !IsOwnedDirectory(source, requiredSourceOwner))
		{
			throw new InvalidOperationException("The selected source is not an EmuStack backup.");
		}
		if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).GetEnumerator().MoveNext() &&
		    requiredDestinationOwner != null && !IsOwnedDirectory(destination, requiredDestinationOwner))
		{
			throw new InvalidOperationException("The selected destination is non-empty and is not owned by this backup operation.");
		}
		if (Directory.Exists(destination))
		{
			EnsureNoReparseTree(destination);
		}

		EnsureNoReparsePoints(source);
		var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Destination has no parent.");
		EnsureNoReparsePoints(parent);
		Directory.CreateDirectory(parent);
		EnsureNoReparsePoints(parent);
		var operationOwner = $"copy-operation:{Guid.NewGuid():N}";
		var previousOwner = $"copy-previous:{Guid.NewGuid():N}";
		var destinationOwner = requiredDestinationOwner ?? $"copy-destination:{Guid.NewGuid():N}";
		var operationRoot = Path.Join(parent, $".{Path.GetFileName(destination)}-copy-{Guid.NewGuid():N}");
		var staged = Path.Join(operationRoot, "staged");
		var previous = Path.Join(operationRoot, "previous");
		string previousMarkerContents = null;
		var filesystemCommitted = false;
		var preserveOperationForRecovery = false;
		EnsureNoReparsePoints(operationRoot);
		Directory.CreateDirectory(staged);
		MarkOwnedDirectory(operationRoot, operationOwner);
		AtomicWriteText(Path.Join(operationRoot, "destination.path"), destination);
		AtomicWriteText(Path.Join(operationRoot, "destination.owner"), destinationOwner);
		AtomicWriteText(Path.Join(operationRoot, "destination.marker-temporary"), requiredDestinationOwner == null ? "true" : "false");
		AtomicWriteText(Path.Join(operationRoot, "phase"), "staging");

		try
		{
			if (preserveDestinationFiles && Directory.Exists(destination))
			{
				EnsureNoReparsePoints(destination);
				CopyDirectory(destination, staged, true);
			}
			CopyDirectory(source, staged, true);
			var copiedMarker = Path.Join(staged, OwnerMarkerFileName);
			if (File.Exists(copiedMarker))
			{
				File.Delete(copiedMarker);
			}
			MarkOwnedDirectory(staged, destinationOwner);

			if (Directory.Exists(destination))
			{
				AtomicWriteText(Path.Join(operationRoot, "phase"), "swapping");
				Directory.Move(destination, previous);
				var previousMarkerPath = Path.Join(previous, OwnerMarkerFileName);
				if (File.Exists(previousMarkerPath))
				{
					previousMarkerContents = File.ReadAllText(previousMarkerPath);
				}
				AtomicWriteText(Path.Join(operationRoot, "previous.marker"), previousMarkerContents ?? "");
				MarkOwnedDirectory(previous, previousOwner);
			}
			Directory.Move(staged, destination);
			filesystemCommitted = true;
			AtomicWriteText(Path.Join(operationRoot, "phase"), "filesystem-committed");
			AtomicWriteText(Path.Join(operationRoot, "phase"), "state-committed");
			if (Directory.Exists(previous))
			{
				try
				{
					DeleteOwnedDirectory(previous, operationRoot, previousOwner);
				}
				catch
				{
					preserveOperationForRecovery = true;
				}
			}
			if (requiredDestinationOwner == null && !preserveOperationForRecovery && Directory.Exists(destination))
			{
				try
				{
					if (!IsOwnedDirectory(destination, destinationOwner))
					{
						throw new IOException("The committed destination ownership marker changed unexpectedly.");
					}
					var markerPath = Path.Join(destination, OwnerMarkerFileName);
					RejectReparsePoint(markerPath);
					File.Delete(markerPath);
				}
				catch
				{
					preserveOperationForRecovery = true;
				}
			}
		}
		catch
		{
			try
			{
				AtomicWriteText(Path.Join(operationRoot, "phase"), "rollback");
				if (filesystemCommitted && Directory.Exists(destination))
				{
					EnsureNoReparsePoints(destination);
					if (!IsOwnedDirectory(destination, destinationOwner))
					{
						throw new IOException("Refusing to roll back an unowned destination.");
					}
					Directory.Move(destination, Path.Join(operationRoot, "rollback-destination"));
				}
				if (Directory.Exists(previous) && !Directory.Exists(destination))
				{
					var previousMarkerPath = Path.Join(previous, OwnerMarkerFileName);
					if (previousMarkerContents == null)
					{
						File.Delete(previousMarkerPath);
					}
					else
					{
						ReplaceFileContents(previousMarkerPath, previousMarkerContents);
					}
					Directory.Move(previous, destination);
				}
			}
			catch
			{
				preserveOperationForRecovery = true;
			}
			throw;
		}
		finally
		{
			if (!preserveOperationForRecovery && Directory.Exists(operationRoot) && IsOwnedDirectory(operationRoot, operationOwner))
			{
				try
				{
					DeleteOwnedDirectory(operationRoot, parent, operationOwner);
				}
				catch (IOException)
				{
					// The committed destination remains valid; leave owned staging for a safe retry.
				}
				catch (UnauthorizedAccessException)
				{
					// Preserve the operation result when cleanup alone is blocked.
				}
			}
		}
	}


	public static void RecoverTransactionalCopy(string destinationDir)
	{
		var destination = CanonicalPath(destinationDir);
		RejectDangerousRoot(destination);
		EnsureNoReparsePoints(destination);
		var parent = Path.GetDirectoryName(destination);
		if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
		{
			return;
		}

		var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		foreach (var operationRoot in Directory.GetDirectories(
			parent, $".{Path.GetFileName(destination)}-copy-*", SearchOption.TopDirectoryOnly))
		{
			var markerPath = Path.Join(operationRoot, OwnerMarkerFileName);
			if (!File.Exists(markerPath))
			{
				continue;
			}

			EnsureNoReparseTree(operationRoot);
			var operationOwner = File.ReadAllText(markerPath).Trim();
			if (!operationOwner.StartsWith("copy-operation:", StringComparison.Ordinal))
			{
				continue;
			}

			var journalDestination = File.ReadAllText(Path.Join(operationRoot, "destination.path")).Trim();
			var phase = File.ReadAllText(Path.Join(operationRoot, "phase")).Trim();
			var destinationOwnerPath = Path.Join(operationRoot, "destination.owner");
			var destinationOwner = File.Exists(destinationOwnerPath)
				? File.ReadAllText(destinationOwnerPath).Trim()
				: "";
			var temporaryMarkerPath = Path.Join(operationRoot, "destination.marker-temporary");
			var temporaryDestinationMarker = File.Exists(temporaryMarkerPath) &&
				string.Equals(File.ReadAllText(temporaryMarkerPath).Trim(), "true", StringComparison.OrdinalIgnoreCase);
			if (!string.Equals(CanonicalPath(journalDestination), destination, comparison))
			{
				continue;
			}

			var previous = Path.Join(operationRoot, "previous");
			var restoredPrevious = false;
			if (Directory.Exists(previous))
			{
				var previousOwnerPath = Path.Join(previous, OwnerMarkerFileName);
				RejectReparsePoint(previousOwnerPath);
				var previousOwner = File.ReadAllText(previousOwnerPath).Trim();
				if (!previousOwner.StartsWith("copy-previous:", StringComparison.Ordinal) ||
					!IsOwnedDirectory(previous, previousOwner))
				{
					throw new IOException("Refusing to recover an unowned previous copy.");
				}
				if (phase == "state-committed" && Directory.Exists(destination))
				{
					if (string.IsNullOrEmpty(destinationOwner) || !IsOwnedDirectory(destination, destinationOwner))
					{
						throw new IOException("Refusing to finalize a copy with an unowned destination.");
					}
					DeleteOwnedDirectory(previous, operationRoot, previousOwner);
				}
				else
				{
					if (Directory.Exists(destination))
					{
						EnsureNoReparsePoints(destination);
						if (string.IsNullOrEmpty(destinationOwner) || !IsOwnedDirectory(destination, destinationOwner))
						{
							throw new IOException("Refusing to roll back an unowned destination.");
						}
						Directory.Move(destination, Path.Join(operationRoot, "recovery-destination"));
					}
					Directory.Move(previous, destination);
					restoredPrevious = true;
					var originalMarkerPath = Path.Join(operationRoot, "previous.marker");
					var destinationMarkerPath = Path.Join(destination, OwnerMarkerFileName);
					var originalMarker = File.Exists(originalMarkerPath)
						? File.ReadAllText(originalMarkerPath)
						: "";
					if (string.IsNullOrEmpty(originalMarker))
					{
						if (File.Exists(destinationMarkerPath))
						{
							RejectReparsePoint(destinationMarkerPath);
							File.Delete(destinationMarkerPath);
						}
					}
					else
					{
						ReplaceFileContents(destinationMarkerPath, originalMarker);
					}
				}
			}
			else if (phase == "filesystem-committed" && Directory.Exists(destination))
			{
				if (string.IsNullOrEmpty(destinationOwner) || !IsOwnedDirectory(destination, destinationOwner))
				{
					throw new IOException("Refusing to discard an unowned destination.");
				}
				Directory.Move(destination, Path.Join(operationRoot, "recovery-destination"));
			}
			else if (phase is not ("staging" or "swapping" or "rollback" or "state-committed"))
			{
				throw new InvalidDataException($"Unknown copy recovery phase: {phase}");
			}

			if (phase == "state-committed" && temporaryDestinationMarker && !restoredPrevious && Directory.Exists(destination))
			{
				if (string.IsNullOrEmpty(destinationOwner) || !IsOwnedDirectory(destination, destinationOwner))
				{
					throw new IOException("Refusing to finalize a copy with an unowned destination.");
				}
				var destinationMarkerPath = Path.Join(destination, OwnerMarkerFileName);
				RejectReparsePoint(destinationMarkerPath);
				File.Delete(destinationMarkerPath);
			}

			if (Directory.Exists(operationRoot))
			{
				DeleteOwnedDirectory(operationRoot, parent, operationOwner);
			}
		}
	}


	public static void AtomicWriteText(string destinationPath, string contents)
	{
		var destination = CanonicalPath(destinationPath);
		var directory = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Destination has no parent directory.");
		EnsureNoReparsePoints(directory);
		Directory.CreateDirectory(directory);
		EnsureNoReparsePoints(directory);
		RejectExistingPath(destination);

		var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
		var backup = destination + ".bak";
		try
		{
			using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			using (var writer = new StreamWriter(stream))
			{
				writer.Write(contents);
				writer.Flush();
				stream.Flush(true);
			}

			if (File.Exists(destination))
			{
				RejectExistingPath(backup);
				File.Copy(destination, backup, true);
			}
			File.Move(temporary, destination, true);
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}


	public static void AtomicReplaceFile(string temporaryPath, string destinationPath, bool retainBackup = true)
	{
		var temporary = CanonicalPath(temporaryPath);
		var destination = CanonicalPath(destinationPath);
		if (!File.Exists(temporary))
		{
			throw new FileNotFoundException("Temporary file was not created.", temporary);
		}
		RejectReparsePoint(temporary);

		var directory = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Destination has no parent directory.");
		EnsureNoReparsePoints(directory);
		Directory.CreateDirectory(directory);
		EnsureNoReparsePoints(directory);
		RejectExistingPath(destination);
		if (retainBackup && File.Exists(destination))
		{
			RejectExistingPath(destination + ".bak");
			File.Copy(destination, destination + ".bak", true);
		}
		File.Move(temporary, destination, true);
	}


	private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;


	private static void CopyDirectory(string source, string destination, bool overwriteFiles)
	{
		foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
		{
			RejectReparsePoint(directory);
			var target = Path.Join(destination, Path.GetFileName(directory));
			Directory.CreateDirectory(target);
			CopyDirectory(directory, target, overwriteFiles);
		}

		foreach (var file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
		{
			RejectReparsePoint(file);
			File.Copy(file, Path.Join(destination, Path.GetFileName(file)), overwriteFiles);
		}
	}


	private static void DeleteDirectoryTree(string path)
	{
		EnsureNoReparseTree(path);
		foreach (var entry in Directory.EnumerateFileSystemEntries(path))
		{
			DeleteEntry(entry);
		}
		Directory.Delete(path);
	}


	private static void DeleteEntry(string path)
	{
		RejectReparsePoint(path);
		if (Directory.Exists(path))
		{
			DeleteDirectoryTree(path);
		}
		else
		{
			File.Delete(path);
		}
	}


	private static void RejectReparsePoint(string path)
	{
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException($"Refusing to follow a link or reparse point: {path}");
		}
	}


	public static void EnsureNoReparseTree(string path)
	{
		EnsureNoReparsePoints(path);
		if (!Directory.Exists(path))
		{
			return;
		}

		foreach (var entry in Directory.EnumerateFileSystemEntries(path))
		{
			RejectReparsePoint(entry);
			if (Directory.Exists(entry))
			{
				EnsureNoReparseTree(entry);
			}
		}
	}


	private static void RejectExistingPath(string path)
	{
		if (File.Exists(path) || Directory.Exists(path))
		{
			RejectReparsePoint(path);
		}
		else
		{
			try
			{
				RejectReparsePoint(path);
			}
			catch (FileNotFoundException)
			{
				// The path does not exist.
			}
			catch (DirectoryNotFoundException)
			{
				// The path does not exist.
			}
		}
	}


	public static void RejectDangerousRoot(string path)
	{
		var canonicalPath = CanonicalPath(path);
		var root = Path.GetPathRoot(canonicalPath);
		var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var home = string.IsNullOrWhiteSpace(homePath) ? null : CanonicalPath(homePath);
		if (string.Equals(canonicalPath, root, PathComparison) || (home != null && string.Equals(canonicalPath, home, PathComparison)))
		{
			throw new InvalidOperationException($"Refusing to operate on a filesystem root or user home directory: {canonicalPath}");
		}
	}


	private static void ReplaceFileContents(string destinationPath, string contents)
	{
		var destination = CanonicalPath(destinationPath);
		RejectExistingPath(destination);
		var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			using (var writer = new StreamWriter(stream))
			{
				writer.Write(contents);
				writer.Flush();
				stream.Flush(true);
			}

			File.Move(temporary, destination, true);
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}
}
