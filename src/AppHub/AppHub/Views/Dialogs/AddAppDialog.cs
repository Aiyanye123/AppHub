using System;
using System.Windows;
using Microsoft.Win32;

namespace AppHub.Views.Dialogs;

public partial class AddAppDialog : Window
{
	public string SelectedPath { get; set; } = string.Empty;

	public AddAppDialog()
	{
		InitializeComponent();
		base.DataContext = this;
	}

	private void OnBrowse(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk",
			Multiselect = false
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
