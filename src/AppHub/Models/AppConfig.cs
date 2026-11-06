using System.Collections.Generic;

namespace AppHub.Models;

public sealed class AppConfig
{
	public const int CurrentSchemaVersion = 5;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public AppSettings Settings { get; set; } = new AppSettings();

	public List<ApplicationItem> Apps { get; set; } = new List<ApplicationItem>();
}
