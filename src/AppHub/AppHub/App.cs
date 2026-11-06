using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AppHub.Infrastructure;

namespace AppHub;

public partial class App : Application
{
	private const string SingleInstanceMutexName = @"Local\AppHub.SingleInstance";

	private Mutex? _singleInstanceMutex;

	private SingleInstanceService? _singleInstanceService;

	private JumpListService? _jumpListService;

	private bool _ownsSingleInstanceMutex;

	private bool _servicesInitialized;

	private volatile bool _isExiting;

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

	private async void OnStartup(object sender, StartupEventArgs e)
	{
		CommandLineOptions options = CommandLineOptions.Parse(e.Args);
		_singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
		if (!createdNew)
		{
			try
			{
				await SingleInstanceService.SendAsync(options.LaunchAppId);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"无法联系已运行的 AppHub。\n{ex.Message}", "AppHub", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
			_singleInstanceMutex.Dispose();
			_singleInstanceMutex = null;
			Shutdown();
			return;
		}
		_ownsSingleInstanceMutex = true;
		ShutdownMode = ShutdownMode.OnExplicitShutdown;

		AppServices.Initialize();
		_servicesInitialized = true;
		AppServices.ApplyCommandLine(options);
		_singleInstanceService = new SingleInstanceService(QueueInstanceCommand, OnSingleInstanceError);
		MainWindow window = (MainWindow)(base.MainWindow = new MainWindow());
		window.Topmost = AppServices.Config.Settings.AlwaysOnTop;
		ThemeService.ApplyTheme(AppServices.Config.Settings.IsDarkMode);
		BackgroundEffectService.Apply(window, AppServices.Config.Settings);
		_trayIconService = new TrayIconService(window);
		_trayIconService.BackgroundModeChanged += OnBackgroundModeChanged;
		TrayIconService = _trayIconService;
		_jumpListService = new JumpListService(_trayIconService);
		if (options.StartInBackground || options.LaunchAppId.HasValue)
		{
			_trayIconService.HideToTray(showHint: false);
		}
		else
		{
			window.Show();
		}
		AppServices.StatusScheduler.Start();
		if (options.LaunchAppId.HasValue)
		{
			await LaunchAppAsync(options.LaunchAppId.Value);
		}
	}

	private void OnExit(object sender, ExitEventArgs e)
	{
		_isExiting = true;
		_singleInstanceService?.Dispose();
		_jumpListService?.Dispose();
		if (_trayIconService != null)
		{
			_trayIconService.BackgroundModeChanged -= OnBackgroundModeChanged;
			_trayIconService.Dispose();
			TrayIconService = null;
		}
		if (_servicesInitialized)
		{
			AppServices.StatusScheduler.Dispose();
			AppServices.Storage.Dispose();
		}
		if (_ownsSingleInstanceMutex)
		{
			_singleInstanceMutex?.ReleaseMutex();
		}
		_singleInstanceMutex?.Dispose();
	}

	private void QueueInstanceCommand(Guid? appId)
	{
		if (_isExiting || Dispatcher.HasShutdownStarted)
		{
			return;
		}
		Dispatcher.BeginInvoke((Action)(() => HandleInstanceCommand(appId)));
	}

	private async void HandleInstanceCommand(Guid? appId)
	{
		if (_isExiting)
		{
			return;
		}
		if (appId.HasValue)
		{
			await LaunchAppAsync(appId.Value);
		}
		else
		{
			RestoreMainWindow();
		}
	}

	private async Task LaunchAppAsync(Guid appId)
	{
		try
		{
			if (_trayIconService != null)
			{
				await _trayIconService.LaunchAppAsync(appId);
			}
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error($"Jump List launch failed: {appId}", ex);
		}
	}

	private void OnSingleInstanceError(Exception exception)
	{
		AppServices.Logger.Error("Single-instance communication failed", exception);
	}

	private void RestoreMainWindow()
	{
		_trayIconService?.Restore();
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
