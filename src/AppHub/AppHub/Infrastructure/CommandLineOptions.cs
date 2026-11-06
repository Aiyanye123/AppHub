using System;

namespace AppHub.Infrastructure;

public sealed class CommandLineOptions
{
	public bool? AutoStart { get; init; }

	public string? LogDirectory { get; init; }

	public bool StartInBackground { get; init; }

	public Guid? LaunchAppId { get; init; }

	public static CommandLineOptions Parse(string[] args)
	{
		bool? autoStart = null;
		bool startInBackground = false;
		Guid? launchAppId = null;
		string? logDirectory = null;
		for (int index = 0; index < args.Length; index++)
		{
			string arg = args[index];
			if (arg.StartsWith("--set-autostart=", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("--autostart=", StringComparison.OrdinalIgnoreCase))
			{
				string text = arg;
				int num = arg.IndexOf('=') + 1;
				string value = text.Substring(num, text.Length - num).Trim();
				autoStart = value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
			}
			else if (arg.StartsWith("--log-dir=", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("--log-path=", StringComparison.OrdinalIgnoreCase))
			{
				string text = arg;
				int num = arg.IndexOf('=') + 1;
				logDirectory = text.Substring(num, text.Length - num).Trim('"');
			}
			else if (string.Equals(arg, "--start-in-background", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase))
			{
				startInBackground = true;
			}
			else if (arg.StartsWith("--launch=", StringComparison.OrdinalIgnoreCase))
			{
				if (Guid.TryParse(arg.Substring("--launch=".Length), out Guid equalsLaunchAppId))
				{
					launchAppId = equalsLaunchAppId;
				}
			}
			else if (string.Equals(arg, "--launch", StringComparison.OrdinalIgnoreCase)
				&& index + 1 < args.Length && Guid.TryParse(args[index + 1], out Guid spacedLaunchAppId))
			{
				launchAppId = spacedLaunchAppId;
				index++;
			}
		}
		return new CommandLineOptions
		{
			AutoStart = autoStart,
			LogDirectory = logDirectory,
			StartInBackground = startInBackground,
			LaunchAppId = launchAppId
		};
	}
}
