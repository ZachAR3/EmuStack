using Godot;


public enum OsKind { Windows, Linux, MacOS }


public static class Os
{
	public static OsKind Current => Parse(OS.GetName());

	public static OsKind Parse(string osName) => osName switch
	{
		"Windows" => OsKind.Windows,
		"Linux" => OsKind.Linux,
		_ => OsKind.MacOS,
	};
}
