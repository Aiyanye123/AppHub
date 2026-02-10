using System;
using System.IO;
using System.Windows;
using AppHub.Models;
using Microsoft.Win32;

namespace AppHub.Views.Dialogs;

public partial class EditAppDialog : Window
{
	public ApplicationItem EditedItem { get; }

	public EditAppDialog(ApplicationItem model)
	{
		InitializeComponent();
		EditedItem = model.Clone();
		base.DataContext = EditedItem;
		UpdateIconModeText();
	}

	private void OnSave(object sender, RoutedEventArgs e)
	{
		NormalizeEditedItem();
		if (string.IsNullOrWhiteSpace(EditedItem.DisplayName))
		{
			MessageBox.Show("\u8bf7\u8f93\u5165\u5e94\u7528\u540d\u79f0\u3002", "\u7f3a\u5c11\u5fc5\u586b\u9879", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		if (string.IsNullOrWhiteSpace(EditedItem.TargetPath))
		{
			MessageBox.Show("\u8bf7\u8f93\u5165\u76ee\u6807\u8def\u5f84\u3002", "\u7f3a\u5c11\u5fc5\u586b\u9879", MessageBoxButton.OK, MessageBoxImage.Warning);
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

	private void OnBrowseCustomIcon(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "Image Files (*.png;*.ico;*.jpg;*.jpeg;*.bmp)|*.png;*.ico;*.jpg;*.jpeg;*.bmp",
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
			CustomIconPathBox.Text = dialog.FileName;
			UpdateIconModeText();
		}
	}

	private void OnResetIcon(object sender, RoutedEventArgs e)
	{
		EditedItem.IconSource = IconSource.Auto;
		EditedItem.CustomIconPath = null;
		CustomIconPathBox.Text = string.Empty;
		UpdateIconModeText();
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

	private void UpdateIconModeText()
	{
		bool hasCustomIcon = EditedItem.IconSource == IconSource.Custom && !string.IsNullOrWhiteSpace(EditedItem.CustomIconPath);
		IconModeText.Text = hasCustomIcon ? "\u5f53\u524d\uff1a\u4f7f\u7528\u81ea\u5b9a\u4e49\u56fe\u6807" : "\u5f53\u524d\uff1a\u81ea\u52a8\u63d0\u53d6\u5e94\u7528\u56fe\u6807";
	}
}
