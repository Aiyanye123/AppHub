using System;

namespace AppHub.Infrastructure;

public sealed class CommandLineOptions
{
	public bool? AutoStart { get; init; }

	public string? LogDirectory { get; init; }

	public bool StartInBackground { get; init; }

	public static CommandLineOptions Parse(string[] args)
	{
		bool? autoStart = null;
		bool startInBackground = false;
		string logDirectory = null;
		foreach (string arg in args)
		{
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
		}
		return new CommandLineOptions
		{
			AutoStart = autoStart,
			LogDirectory = logDirectory,
			StartInBackground = startInBackground
		};
	}
}
