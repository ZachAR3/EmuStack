using Godot;

namespace EmuStack.Scripts.Modes;

public partial class ProviderSettingsResource : Resource
{
    [Export] public string ProviderId = "";
    [Export] public string SaveDirectory = "";
    [Export] public string ExecutablePath = "";
    [Export] public string FromSaveDirectory = "";
    [Export] public string ToSaveDirectory = "";
    [Export] public string ModsLocation = "";
    [Export] public string InstalledVersion = "";
    [Export] public string AppDataPath = "";
    [Export] public string ExecutableName = "";
    [Export] public string ReleaseChannel = "";
}
