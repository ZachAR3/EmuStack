using System;
using System.IO;
using System.Threading.Tasks;


public static class FsHelpers
{
	public static bool DeleteDirectoryContents(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return false;
		}

		// Refuse to wipe drive roots or the user's home directory.
		var fullPath = Path.GetFullPath(directoryPath);
		var rootPath = Path.GetPathRoot(fullPath);
		if (fullPath == rootPath || fullPath == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
		{
			return false;
		}

		foreach (var file in Directory.GetFiles(directoryPath))
		{
			File.Delete(file);
		}

		foreach (var directory in Directory.GetDirectories(directoryPath))
		{
			DeleteDirectoryContents(directory);
			Directory.Delete(directory);
		}

		return true;
	}


	public static void MoveFilesAndDirs(string sourceDirectory, string targetDirectory)
	{
		if (!Directory.Exists(sourceDirectory))
		{
			return;
		}

		if (!Directory.Exists(targetDirectory) && !string.IsNullOrEmpty(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		foreach (var file in Directory.GetFiles(sourceDirectory))
		{
			var targetPath = Path.Combine(targetDirectory, Path.GetFileName(file));
			File.Move(file, targetPath, true);
		}

		foreach (var directory in Directory.GetDirectories(sourceDirectory))
		{
			var targetPath = Path.Combine(targetDirectory, Path.GetFileName(directory));
			if (!Directory.Exists(targetPath))
			{
				Directory.Move(directory, targetPath);
			}
		}

		if (Directory.GetFiles(sourceDirectory).Length == 0 && Directory.GetDirectories(sourceDirectory).Length == 0)
		{
			Directory.Delete(sourceDirectory);
		}
	}


	public static void DuplicateDirectoryContents(string sourceDir, string destinationDir, bool overwriteFiles)
	{
		if (!Directory.Exists(sourceDir) || string.IsNullOrEmpty(destinationDir))
		{
			throw new DirectoryNotFoundException($"Cannot copy from {sourceDir} to {destinationDir}");
		}

		Directory.CreateDirectory(destinationDir);

		foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, dir)));
		}

		foreach (var filePath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
		{
			File.Copy(filePath, Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, filePath)), overwriteFiles);
		}
	}
}
