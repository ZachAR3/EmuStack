public static class LegacyVersion
{
	// Pre-rewrite saves stored InstalledVersion as a packed int. Used once during
	// the multi-provider migration to translate it back to a semver string.
	public static string FromInt(int version)
	{
		int major = (version >> 22) & 0x3FF;
		int minor = (version >> 12) & 0x3FF;
		int build = version & 0xFFF;
		return $"{major}.{minor}.{build}";
	}
}
