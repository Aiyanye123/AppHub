using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AppHub.Infrastructure;

namespace AppHub;

public partial class App : Application
{
	private TrayIconService? _trayIconService;

	public static TrayIconService? TrayIconService { get; private set; }

	public App()
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		base.Startup += OnStartup;
		base.Exit += OnExit;
		base.DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(OnDispatcherUnhandledException);
		AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
	}

	private void OnStartup(object sender, StartupEventArgs e)
	{
		AppServices.Initialize();
		AppServices.ApplyCommandLine(CommandLineOptions.Parse(Environment.GetCommandLineArgs()));
		MainWindow window = (MainWindow)(base.MainWindow = new MainWindow());
		window.Topmost = AppServices.Config.Settings.AlwaysOnTop;
		ThemeService.ApplyTheme(AppServices.Config.Settings.IsDarkMode);
		BackgroundEffectService.Apply(window, AppServices.Config.Settings);
		window.Show();
		_trayIconService = new TrayIconService(window);
		_trayIconService.BackgroundModeChanged += OnBackgroundModeChanged;
		TrayIconService = _trayIconService;
		AppServices.StatusScheduler.Start();
	}

	private void OnExit(object sender, ExitEventArgs e)
	{
		AppServices.StatusScheduler.Dispose();
		AppServices.Storage.Dispose();
		if (_trayIconService != null)
		{
			_trayIconService.BackgroundModeChanged -= OnBackgroundModeChanged;
		}
	}

	private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		AppServices.Logger.Error("Unhandled UI exception", e.Exception);
		e.Handled = false;
	}

	private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
		{
			AppServices.Logger.Fatal("Unhandled domain exception", ex);
		}
	}

	private void OnBackgroundModeChanged(object? sender, bool isBackground)
	{
		AppServices.StatusScheduler.SetIsBackground(isBackground);
	}
}
