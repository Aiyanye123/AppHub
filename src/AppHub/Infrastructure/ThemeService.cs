using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AppHub.Infrastructure;

public static class ThemeService
{
	private const int DwmwaUseImmersiveDarkMode = 20;

	private static readonly Uri LightThemeUri = new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);

	private static readonly Uri DarkThemeUri = new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);

	public static void ApplyTheme(bool isDark)
	{
		ResourceDictionary? resources = Application.Current?.Resources;
		if (resources == null)
		{
			return;
		}

		ResourceDictionary themeDictionary = new ResourceDictionary
		{
			Source = isDark ? DarkThemeUri : LightThemeUri
		};

		int existingThemeIndex = -1;
		for (int i = 0; i < resources.MergedDictionaries.Count; i++)
		{
			Uri? source = resources.MergedDictionaries[i].Source;
			if (source != null && (source.OriginalString.EndsWith("light.xaml", StringComparison.OrdinalIgnoreCase) ||
			                      source.OriginalString.EndsWith("dark.xaml", StringComparison.OrdinalIgnoreCase)))
			{
				existingThemeIndex = i;
				break;
			}
		}

		if (existingThemeIndex >= 0)
		{
			resources.MergedDictionaries[existingThemeIndex] = themeDictionary;
		}
		else
		{
			resources.MergedDictionaries.Add(themeDictionary);
		}

		foreach (DictionaryEntry entry in themeDictionary)
		{
			if (entry.Value is SolidColorBrush sourceBrush)
			{
				if (resources[entry.Key] is SolidColorBrush existingBrush && !existingBrush.IsFrozen)
				{
					existingBrush.Color = sourceBrush.Color;
				}
				else
				{
					resources[entry.Key] = sourceBrush.Clone();
				}
			}
			else if (entry.Value is Freezable freezable)
			{
				resources[entry.Key] = freezable.Clone();
			}
			else
			{
				resources[entry.Key] = entry.Value;
			}
		}

		if (Application.Current != null)
		{
			foreach (Window window in Application.Current.Windows)
			{
				ApplyWindowChromeTheme(window, isDark);
			}
		}
	}

	public static void ApplyWindowChromeTheme(Window? window, bool isDark)
	{
		if (window == null)
		{
			return;
		}
		try
		{
			IntPtr hwnd = new WindowInteropHelper(window).Handle;
			if (hwnd != IntPtr.Zero)
			{
				int useDark = isDark ? 1 : 0;
				DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
			}
		}
		catch
		{
			// Ignore on older Windows builds that do not support DWM dark attribute.
		}
	}

	[DllImport("dwmapi.dll", PreserveSig = true)]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
