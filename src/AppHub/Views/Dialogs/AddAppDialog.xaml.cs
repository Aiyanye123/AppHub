using System;
using System.Windows;
using AppHub.Infrastructure;
using Microsoft.Win32;

namespace AppHub.Views.Dialogs;

public partial class AddAppDialog : Window
{
	public string SelectedPath { get; set; } = string.Empty;

	public AddAppDialog()
	{
		InitializeComponent();
		base.DataContext = this;
		base.SourceInitialized += OnSourceInitialized;
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		ThemeService.ApplyWindowChromeTheme(this, AppServices.Config.Settings.IsDarkMode);
	}

	private void OnBrowse(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "应用程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat"
		};
		if (dialog.ShowDialog() == true)
		{
			SelectedPath = dialog.FileName;
		}
	}

	private void OnAdd(object sender, RoutedEventArgs e)
	{
		base.DialogResult = true;
		Close();
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}
}
