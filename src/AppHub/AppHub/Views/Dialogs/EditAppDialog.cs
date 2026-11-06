using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AppHub.Infrastructure;
using AppHub.Models;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace AppHub.Views.Dialogs;

public partial class EditAppDialog : Window
{
	public ApplicationItem EditedItem { get; }

	public IReadOnlyList<string> AvailableGroupNames { get; }

	public EditAppDialog(ApplicationItem model, IReadOnlyList<string>? availableGroupNames = null, bool isAddingNew = false)
	{
		AvailableGroupNames = (availableGroupNames ?? Array.Empty<string>())
			.Where((string groupName) => !string.IsNullOrWhiteSpace(groupName))
			.Select((string groupName) => groupName.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy((string groupName) => groupName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		InitializeComponent();
		base.SourceInitialized += OnSourceInitialized;
		EditedItem = model.Clone();
		base.DataContext = EditedItem;

		if (isAddingNew)
		{
			Title = "添加应用";
			DialogHeaderIcon.Text = "\uE710";
			DialogHeaderTitle.Text = "添加应用配置";
			SaveButton.Content = "完成添加";
		}

		if (!string.IsNullOrWhiteSpace(EditedItem.Arguments))
		{
			AdvancedExpander.IsExpanded = true;
		}

		UpdateIconPreview();
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		ThemeService.ApplyWindowChromeTheme(this, AppServices.Config.Settings.IsDarkMode);
	}

	private void OnDisplayNameTextChanged(object sender, TextChangedEventArgs e)
	{
		if (DisplayNameErrorText != null && !string.IsNullOrWhiteSpace(DisplayNameBox.Text))
		{
			DisplayNameErrorText.Visibility = Visibility.Collapsed;
		}
	}

	private void OnTargetPathTextChanged(object sender, TextChangedEventArgs e)
	{
		if (TargetPathErrorText != null && !string.IsNullOrWhiteSpace(TargetPathBox.Text))
		{
			TargetPathErrorText.Visibility = Visibility.Collapsed;
		}
	}

	private void OnSave(object sender, RoutedEventArgs e)
	{
		NormalizeEditedItem();
		bool hasError = false;
		if (string.IsNullOrWhiteSpace(EditedItem.DisplayName))
		{
			DisplayNameErrorText.Visibility = Visibility.Visible;
			DisplayNameBox.Focus();
			hasError = true;
		}
		if (string.IsNullOrWhiteSpace(EditedItem.TargetPath))
		{
			TargetPathErrorText.Visibility = Visibility.Visible;
			if (!hasError)
			{
				TargetPathBox.Focus();
			}
			hasError = true;
		}
		if (hasError)
		{
			return;
		}
		base.DialogResult = true;
		Close();
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}

	private void OnBrowseTargetFile(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "选择目标程序",
			Filter = "应用程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|所有文件 (*.*)|*.*",
			Multiselect = false
		};
		if (!string.IsNullOrWhiteSpace(EditedItem.TargetPath))
		{
			string? dir = Path.GetDirectoryName(EditedItem.TargetPath);
			if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
			{
				dialog.InitialDirectory = dir;
			}
		}
		if (dialog.ShowDialog(this) == true)
		{
			EditedItem.TargetPath = dialog.FileName;
			TargetPathBox.Text = dialog.FileName;
			if (string.IsNullOrWhiteSpace(EditedItem.DisplayName))
			{
				string name = Path.GetFileNameWithoutExtension(dialog.FileName);
				EditedItem.DisplayName = name;
				DisplayNameBox.Text = name;
			}
			if (string.IsNullOrWhiteSpace(EditedItem.WorkingDirectory))
			{
				string workDir = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
				EditedItem.WorkingDirectory = workDir;
				WorkingDirectoryBox.Text = workDir;
			}
			UpdateIconPreview();
		}
	}

	private void OnBrowseTargetFolder(object sender, RoutedEventArgs e)
	{
		using WinForms.FolderBrowserDialog dialog = new WinForms.FolderBrowserDialog
		{
			Description = "选择目标文件夹",
			UseDescriptionForTitle = true
		};
		if (!string.IsNullOrWhiteSpace(EditedItem.TargetPath) && Directory.Exists(EditedItem.TargetPath))
		{
			dialog.InitialDirectory = EditedItem.TargetPath;
		}
		if (dialog.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
		{
			EditedItem.TargetPath = dialog.SelectedPath;
			EditedItem.SourceType = SourceType.Folder;
			EditedItem.TrackProcess = false;
			TargetPathBox.Text = dialog.SelectedPath;
			if (string.IsNullOrWhiteSpace(EditedItem.DisplayName))
			{
				string name = new DirectoryInfo(dialog.SelectedPath).Name;
				EditedItem.DisplayName = name;
				DisplayNameBox.Text = name;
			}
			UpdateIconPreview();
		}
	}

	private void OnBrowseWorkingDirectory(object sender, RoutedEventArgs e)
	{
		using WinForms.FolderBrowserDialog dialog = new WinForms.FolderBrowserDialog
		{
			Description = "选择工作目录",
			UseDescriptionForTitle = true
		};
		if (!string.IsNullOrWhiteSpace(EditedItem.WorkingDirectory) && Directory.Exists(EditedItem.WorkingDirectory))
		{
			dialog.InitialDirectory = EditedItem.WorkingDirectory;
		}
		if (dialog.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
		{
			EditedItem.WorkingDirectory = dialog.SelectedPath;
			WorkingDirectoryBox.Text = dialog.SelectedPath;
		}
	}

	private void OnBrowseCustomIcon(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "选择自定义图标",
			Filter = "图标文件 (*.png;*.ico;*.jpg;*.jpeg;*.bmp)|*.png;*.ico;*.jpg;*.jpeg;*.bmp",
			Multiselect = false
		};
		if (!string.IsNullOrWhiteSpace(EditedItem.CustomIconPath))
		{
			string? directory = Path.GetDirectoryName(EditedItem.CustomIconPath);
			if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
			{
				dialog.InitialDirectory = directory;
			}
		}
		if (dialog.ShowDialog(this) == true)
		{
			EditedItem.IconSource = IconSource.Custom;
			EditedItem.CustomIconPath = dialog.FileName;
			UpdateIconPreview();
		}
	}

	private void OnResetIcon(object sender, RoutedEventArgs e)
	{
		EditedItem.IconSource = IconSource.Auto;
		EditedItem.CustomIconPath = null;
		UpdateIconPreview();
	}

	private void NormalizeEditedItem()
	{
		EditedItem.DisplayName = EditedItem.DisplayName?.Trim() ?? string.Empty;
		EditedItem.TargetPath = EditedItem.TargetPath?.Trim() ?? string.Empty;
		EditedItem.Arguments = EditedItem.Arguments?.Trim() ?? string.Empty;
		EditedItem.WorkingDirectory = EditedItem.WorkingDirectory?.Trim() ?? string.Empty;
		EditedItem.GroupName = EditedItem.GroupName?.Trim() ?? string.Empty;
		if (EditedItem.IconSource == IconSource.Custom && string.IsNullOrWhiteSpace(EditedItem.CustomIconPath))
		{
			EditedItem.IconSource = IconSource.Auto;
			EditedItem.CustomIconPath = null;
		}
	}

	private void UpdateIconPreview()
	{
		bool hasCustomIcon = EditedItem.IconSource == IconSource.Custom && !string.IsNullOrWhiteSpace(EditedItem.CustomIconPath);
		IconModeText.Text = hasCustomIcon ? "当前：使用自定义图标" : "当前：自动提取应用图标";
		IconPathHintText.Text = hasCustomIcon
			? EditedItem.CustomIconPath
			: "支持 PNG / ICO / JPG 自定义图标";
		ResetIconButton.Visibility = hasCustomIcon ? Visibility.Visible : Visibility.Collapsed;
		try
		{
			IconPreviewImage.Source = AppServices.IconService.GetIcon(EditedItem);
		}
		catch
		{
			IconPreviewImage.Source = null;
		}
	}
}
