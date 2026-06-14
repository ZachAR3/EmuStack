using Godot;
using System;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ContentType = Octokit.ContentType;
using HttpClient = System.Net.Http.HttpClient;

public partial class Tools : Node
{
	[Export()] private Control _errorConsoleContainer;
	[Export] private TextEdit _errorConsole;
	[Export] private RichTextLabel _errorNotifier;
	[Export] private PopupMenu _confirmationPopup;

	public static Tools Instance;
	
	// Internal variables
	private bool? _confirmationChoice;


	// Godot functions
	public override void _Ready()
	{
		Instance = this;
	}
	
	
	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("OpenConsole"))
		{
			ToggleConsole();
		}
	}


	// General functions
	public void LaunchEmulator()
	{
		string executablePath = Globals.Instance.CurrentProviderSettings.ExecutablePath;
		string emulatorName = Globals.Instance.AppMode.Name;

		try
		{
			ProcessStartInfo emulatorProcessInfo = new(executablePath);

			Process.Start(emulatorProcessInfo);
			GetTree().Quit();
		}
		catch (Exception launchException)
		{
			AddError($"Unable to launch {emulatorName}: " + launchException.Message);
		}	
	}

	// Bit math I don't understand to convert to and from an int for indexing
	public static int ToInt(string version)
	{
		if (TryToVersionInt(version, out var versionInt))
		{
			return versionInt;
		}

		throw new FormatException($"Invalid version number: {version}");
	}


	public static bool TryToVersionInt(string version, out int versionInt)
	{
		versionInt = -1;
		if (string.IsNullOrWhiteSpace(version))
		{
			return false;
		}

		// Override of sorts so this function can be used on both ints and strings
		if (int.TryParse(version, out versionInt))
		{
			return true;
		}

		var semanticVersion = Regex.Match(version.Trim().TrimStart('v'), @"^\d+\.\d+\.\d+");
		if (!semanticVersion.Success)
		{
			return false;
		}

		string[] versionParts = semanticVersion.Value.Split('.');
		if (!int.TryParse(versionParts[0], out var major) ||
		    !int.TryParse(versionParts[1], out var minor) ||
		    !int.TryParse(versionParts[2], out var build))
		{
			return false;
		}

		versionInt = (major << 22) | (minor << 12) | build;
		return true;
	}


	public static string FromInt(int version)
	{
		int major = (version >> 22) & 0x3FF;
		int minor = (version >> 12) & 0x3FF;
		int build = version & 0xFFF;
		return @$"{major}.{minor}.{build}";
	}
	
	
	private void ToggleConsole()
	{
		_errorConsoleContainer.Visible = !_errorConsoleContainer.Visible;
	}
	
	
	public async Task<bool?> ConfirmationPopup(string titleText = "Are you sure?")
	{
		// Checks if the confirmationPopup is already connected to the ConfirmationPressed signal, if not, connect it.
		if (!_confirmationPopup.IsConnected("index_pressed", new Callable(this, nameof(ConfirmationPressed))))
		{
			_confirmationPopup.Connect("index_pressed", new Callable(this, nameof(ConfirmationPressed)));
		}

		_confirmationPopup.Title = titleText;
		_confirmationPopup.PopupCentered();
		await ToSignal(_confirmationPopup, "index_pressed");
		return _confirmationChoice;
	}
	
	public bool ClearInstallationFolder(string saveDirectory)
	{
		bool clearedSuccessfully = DeleteDirectoryContents(saveDirectory);
		return clearedSuccessfully;
	}
	
	public static bool DeleteDirectoryContents(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return false;
		}

		var fullPath = Path.GetFullPath(directoryPath);
		var rootPath = Path.GetPathRoot(fullPath);
		if (fullPath == rootPath || fullPath == System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
		{
			return false;
		}

		// Delete all files within the directory
		string[] files = Directory.GetFiles(directoryPath);
		foreach (string file in files)
		{
			File.Delete(file);
		}

		// Delete all subdirectories within the directory
		string[] directories = Directory.GetDirectories(directoryPath);
		foreach (string directory in directories)
		{
			// Recursively delete subdirectory contents
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

		// Create the target directory if it doesn't exist
		if (!Directory.Exists(targetDirectory) && !string.IsNullOrEmpty(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		// Get all files and directories from the source directory
		string[] files = Directory.GetFiles(sourceDirectory);
		string[] directories = Directory.GetDirectories(sourceDirectory);

		// Move files to the target directory
		foreach (string file in files)
		{
			string fileName = Path.GetFileName(file);
			string targetPath = Path.Combine(targetDirectory, fileName);
			File.Move(file, targetPath, true);
		}

		// Move directories to the target directory
		foreach (string directory in directories)
		{
			string directoryName = Path.GetFileName(directory);
			string targetPath = Path.Combine(targetDirectory, directoryName);
			if (!Directory.Exists(targetPath))
			{
				Directory.Move(directory, targetPath);
			}
		}

		// Remove the source directory if it is empty
		if (Directory.GetFiles(sourceDirectory).Length == 0 && Directory.GetDirectories(sourceDirectory).Length == 0)
		{
			Directory.Delete(sourceDirectory);
		}
	}
	
	
	public void DuplicateDirectoryContents(string sourceDir, string destinationDir, bool overwriteFiles)
	{
		if (!Directory.Exists(sourceDir) || string.IsNullOrEmpty(destinationDir))
		{
			throw new DirectoryNotFoundException($"Cannot copy from {sourceDir} to {destinationDir}");
		}

		// Get all directories in the source directory
		string[] allDirectories = Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories);

		foreach (string dir in allDirectories)
		{
			string dirToCreate = dir.Replace(sourceDir, destinationDir);
			Directory.CreateDirectory(dirToCreate);
		}

		// Get all files in the source directory
		string[] allFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);

		foreach (string filePath in allFiles)
		{
			string newFilePath = filePath.Replace(sourceDir, destinationDir);
			File.Copy(filePath, newFilePath, overwriteFiles);
		}
	}


	public async void AddError(String error)
	{
		Callable.From(() =>
		{
			_errorConsole.Text += $"\n [{DateTime.Now:h:mm:ss}]	{error}";
			_errorNotifier.Visible = true;
		}).CallDeferred();
		
		await Task.Run(async () =>
		{
			await ToSignal(GetTree().CreateTimer(5), "timeout");
			_errorNotifier.SetThreadSafe("visible", false);
		});

	}


	// Signal functions
	private void ConfirmationPressed(long itemIndex)
	{
		_confirmationChoice = itemIndex == 0;
	}


	public async Task<Exception> DownloadFolder(string owner, string repo, string folderPath, string destinationPath)
	{
		try
		{
			HttpClient httpClient = new();
			var gitHubClient = Globals.Instance.LocalGithubClient;

			// Retrieve the repository content for the specified folder
			var contents = await gitHubClient.Repository.Content.GetAllContents(owner, repo, folderPath);

			// Create the destination folder
			Directory.CreateDirectory(destinationPath);

			// Download and copy each file in the folder
			foreach (var content in contents)
			{
				if (content.Type == ContentType.File)
				{
					var fileContent = await httpClient.GetByteArrayAsync(content.DownloadUrl);
					var filePath = Path.Combine(destinationPath, content.Name);

					// Write the file content to disk
					await File.WriteAllBytesAsync(filePath, fileContent);
				}
				else if (content.Type == ContentType.Dir)
				{
					var subFolderPath = Path.Combine(destinationPath, content.Name);

					// Recursively download and copy the contents of sub-folders
					await DownloadFolder(owner, repo, content.Path, subFolderPath);
				}
			}

			return null;
		}
		catch (Exception downloadException)
		{
			return downloadException;
		}
	}
}
