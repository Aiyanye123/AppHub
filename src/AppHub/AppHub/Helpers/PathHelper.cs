using System;
using System.IO;

namespace AppHub.Helpers;

public static class PathHelper
{
	public static string GetAppDataDirectory()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppHub");
	}

	public static string GetConfigPath()
	{
		return Path.Combine(GetAppDataDirectory(), "config.json");
	}

	public static string GetDefaultLogDirectory()
	{
		return Path.Combine(GetAppDataDirectory(), "logs");
	}

	public static string GetIconCacheDirectory()
	{
		return Path.Combine(GetAppDataDirectory(), "icons");
	}

	public static void EnsureDirectory(string path)
	{
		if (!Directory.Exists(path))
		{
			Directory.CreateDirectory(path);
		}
	}

	public static string NormalizePath(string path)
	{
		return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
	}
}
