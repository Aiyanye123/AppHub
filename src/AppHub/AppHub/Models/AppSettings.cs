namespace AppHub.Models;

public sealed class AppSettings
{
	public bool AutoStart { get; set; }

	public int RefreshIntervalMs { get; set; } = 3000;

	public int CloseGracePeriodMs { get; set; } = 5000;

	public bool AllowForceKill { get; set; } = true;

	public LaunchBehavior LaunchBehavior { get; set; }

	public string LogDirectory { get; set; } = string.Empty;

	public int LogMaxSizeMb { get; set; }

	public bool AlwaysOnTop { get; set; }

	public bool IsDarkMode { get; set; }

	public string LightBackgroundImagePath { get; set; } = string.Empty;

	public BackgroundStyle LightBackgroundStyle { get; set; }

	public int LightBackgroundEffectStrength { get; set; } = 40;

	public int LightSidebarOpacity { get; set; } = 92;

	public int LightProgramOpacity { get; set; } = 92;

	public string DarkBackgroundImagePath { get; set; } = string.Empty;

	public BackgroundStyle DarkBackgroundStyle { get; set; }

	public int DarkBackgroundEffectStrength { get; set; } = 40;

	public int DarkSidebarOpacity { get; set; } = 88;

	public int DarkProgramOpacity { get; set; } = 88;

	public string BackgroundImagePath { get; set; } = string.Empty;

	public BackgroundStyle BackgroundStyle { get; set; }

	public int BackgroundEffectStrength { get; set; } = 40;
}
