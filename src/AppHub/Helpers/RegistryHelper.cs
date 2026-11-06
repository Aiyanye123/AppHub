using System;
using Microsoft.Win32;

namespace AppHub.Helpers;

public static class RegistryHelper
{
	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	public static bool IsAutoStartEnabled(string appName)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
		return key?.GetValue(appName) is string;
	}

	public static void SetAutoStart(string appName, string executablePath, string arguments)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true) ?? throw new InvalidOperationException("Run registry key not available.");
		string value = ("\"" + executablePath + "\" " + arguments).Trim();
		key.SetValue(appName, value);
	}

	public static void RemoveAutoStart(string appName)
	{
		using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
		key?.DeleteValue(appName, throwOnMissingValue: false);
	}
}
