using System.Collections.Generic;


public class Mod
{
	public string ModId { get; set; } = "";
	public string ModName { get; set; }
	public string ModUrl { get; set; }
	public List<string> CompatibleVersions { get; set; }
	public int Source { get; set; } = -1;
	public string InstalledPath { get; set; }
}
