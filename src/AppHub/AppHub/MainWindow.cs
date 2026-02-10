using System;
using System.ComponentModel;
using System.Windows;
using AppHub.Infrastructure;

namespace AppHub;

public partial class MainWindow : Window
{
	private bool _allowClose;

	public MainWindow()
	{
		InitializeComponent();
		base.Closing += OnClosing;
		BackgroundEffectService.Apply(this, AppServices.Config.Settings);
	}

	public void RequestClose()
	{
		_allowClose = true;
		Close();
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (!_allowClose)
		{
			e.Cancel = true;
			App.TrayIconService?.HideToTray();
		}
	}
}
