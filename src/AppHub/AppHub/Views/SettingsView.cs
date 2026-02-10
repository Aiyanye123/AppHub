using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppHub.Infrastructure;
using AppHub.Models;
using AppHub.ViewModels;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace AppHub.Views;

public partial class SettingsView : UserControl
{
	private SettingsViewModel ViewModel => (SettingsViewModel)base.DataContext;

	public SettingsView()
	{
		InitializeComponent();
	}

	private void OnBrowseLogDirectory(object sender, RoutedEventArgs e)
	{
		using WinForms.FolderBrowserDialog dialog = new WinForms.FolderBrowserDialog
		{
			Description = "\u9009\u62e9\u65e5\u5fd7\u76ee\u5f55",
			UseDescriptionForTitle = true
		};
		if (!string.IsNullOrWhiteSpace(ViewModel.LogDirectory) && Directory.Exists(ViewModel.LogDirectory))
		{
			dialog.InitialDirectory = ViewModel.LogDirectory;
		}
		if (dialog.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
		{
			ViewModel.LogDirectory = dialog.SelectedPath;
		}
	}

	private void OnOpenLogs(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(ViewModel.LogDirectory))
		{
			Process.Start(new ProcessStartInfo("explorer.exe", ViewModel.LogDirectory)
			{
				UseShellExecute = true
			});
		}
	}

	private void OnDeleteAllLogs(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show("\u786e\u8ba4\u5220\u9664\u6240\u6709\u65e5\u5fd7\u6587\u4ef6\u5417\uff1f", "\u5220\u9664\u65e5\u5fd7", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
		{
			return;
		}
		int deleted = AppServices.Logger.DeleteAllLogs();
		MessageBox.Show($"\u5df2\u5220\u9664 {deleted} \u4e2a\u65e5\u5fd7\u6587\u4ef6\u3002", "\u5220\u9664\u5b8c\u6210", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void OnBrowseLightBackgroundImage(object sender, RoutedEventArgs e)
	{
		string? selected = SelectBackgroundImage("\u9009\u62e9\u4eae\u8272\u80cc\u666f\u56fe\u7247", ViewModel.LightBackgroundImagePath);
		if (!string.IsNullOrWhiteSpace(selected))
		{
			ViewModel.LightBackgroundImagePath = selected;
			if (ViewModel.LightBackgroundStyle == BackgroundStyle.None)
			{
				ViewModel.LightBackgroundStyle = BackgroundStyle.ImageOnly;
			}
		}
	}

	private void OnResetLightBackgroundImage(object sender, RoutedEventArgs e)
	{
		ViewModel.ResetLightBackground();
	}

	private void OnResetLightBackgroundStrength(object sender, RoutedEventArgs e)
	{
		ViewModel.ResetLightBackgroundStrength();
	}

	private void OnBrowseDarkBackgroundImage(object sender, RoutedEventArgs e)
	{
		string? selected = SelectBackgroundImage("\u9009\u62e9\u6697\u8272\u80cc\u666f\u56fe\u7247", ViewModel.DarkBackgroundImagePath);
		if (!string.IsNullOrWhiteSpace(selected))
		{
			ViewModel.DarkBackgroundImagePath = selected;
			if (ViewModel.DarkBackgroundStyle == BackgroundStyle.None)
			{
				ViewModel.DarkBackgroundStyle = BackgroundStyle.ImageOnly;
			}
		}
	}

	private void OnResetDarkBackgroundImage(object sender, RoutedEventArgs e)
	{
		ViewModel.ResetDarkBackground();
	}

	private void OnResetDarkBackgroundStrength(object sender, RoutedEventArgs e)
	{
		ViewModel.ResetDarkBackgroundStrength();
	}

	private void OnResetPanelOpacity(object sender, RoutedEventArgs e)
	{
		ViewModel.ResetPanelOpacity();
	}

	private static string? SelectBackgroundImage(string title, string currentPath)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = title,
			Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
			Multiselect = false
		};
		if (!string.IsNullOrWhiteSpace(currentPath))
		{
			string directory = Path.GetDirectoryName(currentPath) ?? string.Empty;
			if (Directory.Exists(directory))
			{
				dialog.InitialDirectory = directory;
			}
		}
		return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName) ? dialog.FileName : null;
	}
}
