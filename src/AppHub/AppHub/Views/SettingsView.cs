using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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

	private void OnExportConfig(object sender, RoutedEventArgs e)
	{
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "导出 AppHub 配置",
			FileName = $"AppHub-config-{DateTime.Now:yyyyMMdd-HHmmss}.json",
			DefaultExt = ".json",
			Filter = "JSON 配置文件 (*.json)|*.json"
		};
		if (dialog.ShowDialog() != true)
		{
			return;
		}

		try
		{
			AppServices.Storage.FlushNow();
			File.Copy(AppServices.Storage.GetPath("config.json"), dialog.FileName, overwrite: true);
			MessageBox.Show("配置已导出。", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception ex)
		{
			MessageBox.Show("导出失败：" + ex.Message, "导出配置", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	private void OnImportConfig(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "导入 AppHub 配置",
			Filter = "JSON 配置文件 (*.json)|*.json",
			Multiselect = false
		};
		if (dialog.ShowDialog() != true)
		{
			return;
		}

		try
		{
			string json = File.ReadAllText(dialog.FileName);
			using JsonDocument document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("settings", out _) || !document.RootElement.TryGetProperty("apps", out _))
			{
				throw new InvalidDataException("不是有效的 AppHub 配置文件。");
			}

			AppConfig? imported = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});
			if (imported?.Settings == null || imported.Apps == null || imported.SchemaVersion > AppConfig.CurrentSchemaVersion)
			{
				throw new InvalidDataException("配置文件无效或版本过新。");
			}

			if (MessageBox.Show("导入将覆盖当前配置，是否继续？", "导入配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
			{
				return;
			}

			AppServices.Storage.FlushNow();
			AppServices.Storage.Save(imported);
			MessageBox.Show("配置已导入。AppHub 将退出，请重新启动。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
			if (Application.Current.MainWindow is MainWindow mainWindow)
			{
				mainWindow.RequestClose();
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("导入失败：" + ex.Message, "导入配置", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
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
