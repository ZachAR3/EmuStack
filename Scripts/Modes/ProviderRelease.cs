namespace EmuStack.Scripts.Modes;

public class ProviderRelease
{
    public string Version { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsPrerelease { get; set; }
}
