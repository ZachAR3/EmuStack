using Godot;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;


public partial class Tools : Node
{
	[Export] private Control _errorConsoleContainer;
	[Export] private TextEdit _errorConsole;
	[Export] private RichTextLabel _errorNotifier;
	[Export] private ConfirmationDialog _confirmationPopup;

	public static Tools Instance;

	private TaskCompletionSource<bool> _confirmationSource;
	private FileDialog _folderDialog;
	private TaskCompletionSource<string> _folderPickerSource;


	public override void _Ready()
	{
		Instance = this;
		_confirmationPopup.Confirmed += () => _confirmationSource?.TrySetResult(true);
		// Canceled covers the Cancel button, Escape, and the window close button,
		// so awaiters never leak.
		_confirmationPopup.Canceled += () => _confirmationSource?.TrySetResult(false);

		// Godot's built-in FileDialog works on all platforms with no native deps.
		// UseNativeDialog uses the OS native picker on Windows/macOS when available.
		_folderDialog = new FileDialog
		{
			Title = "Select Folder",
			FileMode = FileDialog.FileModeEnum.OpenDir,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
			Size = new Vector2I(800, 600),
		};
		_folderDialog.DirSelected += OnFolderSelected;
		_folderDialog.Canceled += () => _folderPickerSource?.TrySetResult(null);
		AddChild(_folderDialog);
	}


	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("OpenConsole"))
		{
			ToggleConsole();
		}
	}


	public void LaunchEmulator()
	{
		var executablePath = Globals.Instance.CurrentProviderSettings.ExecutablePath;
		var emulatorName = Globals.Instance.AppMode.Name;

		try
		{
			Process.Start(new ProcessStartInfo(executablePath));
			GetTree().Quit();
		}
		catch (Exception launchException)
		{
			AddError($"Unable to launch {emulatorName}: " + launchException.Message);
		}
	}


	private void ToggleConsole()
	{
		_errorConsoleContainer.Visible = !_errorConsoleContainer.Visible;
	}


	public Task<bool> ConfirmationPopup(string message = "Are you sure?")
	{
		// Resolve any prior popup as cancelled before opening a new one.
		_confirmationSource?.TrySetResult(false);
		_confirmationSource = new TaskCompletionSource<bool>();
		_confirmationPopup.DialogText = message;
		// Window-based popups don't inherit Control themes; sync from parent so
		// the dialog matches the rest of the UI (font size, colors, styles).
		SyncThemeFromParent(_confirmationPopup);
		_confirmationPopup.PopupCentered();
		return _confirmationSource.Task;
	}


	/// <summary>
	/// Shows a folder picker dialog. Returns the selected path, or null if the
	/// user cancelled. The caller is responsible for assigning the result and
	/// syncing settings.
	/// </summary>
	public Task<string> PickFolder(string current)
	{
		// Resolve any prior picker as cancelled before opening a new one.
		_folderPickerSource?.TrySetResult(null);
		_folderPickerSource = new TaskCompletionSource<string>();

		if (!string.IsNullOrEmpty(current))
		{
			_folderDialog.CurrentDir = current;
		}

		SyncThemeFromParent(_folderDialog);
		_folderDialog.PopupCentered();
		return _folderPickerSource.Task;
	}


	/// <summary>
	/// Window-based popups (ConfirmationDialog, FileDialog) don't inherit the
	/// theme from a parent Control. This copies the parent's theme so popup
	/// fonts and styles match the rest of the UI.
	/// </summary>
	private void SyncThemeFromParent(Window popup)
	{
		if (GetParent() is Control parent && parent.Theme != null)
		{
			popup.Theme = parent.Theme;
		}
	}


	private void OnFolderSelected(string dir)
	{
		_folderPickerSource?.TrySetResult(dir);
	}


	public async void AddError(string error)
	{
		var formattedError = FormatError(error);
		Callable.From(() =>
		{
			_errorConsole.Text += $"\n [{DateTime.Now:h:mm:ss}]	{formattedError}";
			_errorNotifier.Visible = true;
		}).CallDeferred();

		await ToSignal(GetTree().CreateTimer(5), "timeout");

		// The Tools node (and its UI refs) can be freed while the timer runs, e.g. when
		// the user switches provider and the scene reloads. Guard before touching nodes.
		if (!IsInstanceValid(this) || !IsInstanceValid(_errorNotifier))
		{
			return;
		}

		_errorNotifier.Visible = false;
	}


	private static string FormatError(string error)
	{
		if (string.IsNullOrWhiteSpace(error))
		{
			return "Unknown error.";
		}

		var formattedError = error.Replace("\r", "\n");
		if (LooksLikeHtml(formattedError))
		{
			formattedError = StripHtml(formattedError);
		}

		const int maxErrorLength = 1200;
		if (formattedError.Length > maxErrorLength)
		{
			formattedError = formattedError[..maxErrorLength] + "...";
		}

		return formattedError;
	}


	private static bool LooksLikeHtml(string error)
	{
		return Regex.IsMatch(error, @"<\s*!doctype|<\s*html|<\s*body|<\s*head", RegexOptions.IgnoreCase);
	}


	private static string StripHtml(string html)
	{
		var document = new HtmlDocument();
		document.LoadHtml(html);

		var noiseNodes = document.DocumentNode.SelectNodes("//script|//style|//noscript|//template");
		if (noiseNodes != null)
		{
			foreach (var node in noiseNodes)
			{
				node.Remove();
			}
		}

		return Regex.Replace(document.DocumentNode.InnerText ?? "", @"\s+", " ").Trim();
	}
}
