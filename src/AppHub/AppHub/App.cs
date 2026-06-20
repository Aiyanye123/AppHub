using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AppHub.Infrastructure;

namespace AppHub;

public partial class App : Application
{
	private const string SingleInstanceMutexName = @"Local\AppHub.SingleInstance";

	private const string ShowWindowEventName = @"Local\AppHub.ShowWindow";

	private Mutex? _singleInstanceMutex;

	private EventWaitHandle? _showWindowEvent;

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

	private void OnStartup(object sender, StartupEventArgs e)
	{
		_singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
		if (!createdNew)
		{
			SignalExistingInstance();
			_singleInstanceMutex.Dispose();
			_singleInstanceMutex = null;
			Shutdown();
			return;
		}
		_ownsSingleInstanceMutex = true;
		_showWindowEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowWindowEventName);

		AppServices.Initialize();
		_servicesInitialized = true;
		CommandLineOptions options = CommandLineOptions.Parse(Environment.GetCommandLineArgs());
		AppServices.ApplyCommandLine(options);
		MainWindow window = (MainWindow)(base.MainWindow = new MainWindow());
		window.Topmost = AppServices.Config.Settings.AlwaysOnTop;
		ThemeService.ApplyTheme(AppServices.Config.Settings.IsDarkMode);
		BackgroundEffectService.Apply(window, AppServices.Config.Settings);
		_trayIconService = new TrayIconService(window);
		_trayIconService.BackgroundModeChanged += OnBackgroundModeChanged;
		TrayIconService = _trayIconService;
		StartShowWindowListener();
		if (options.StartInBackground)
		{
			_trayIconService.HideToTray();
		}
		else
		{
			window.Show();
		}
		AppServices.StatusScheduler.Start();
	}

	private void OnExit(object sender, ExitEventArgs e)
	{
		_isExiting = true;
		_showWindowEvent?.Set();
		if (_servicesInitialized)
		{
			AppServices.StatusScheduler.Dispose();
			AppServices.Storage.Dispose();
		}
		if (_trayIconService != null)
		{
			_trayIconService.BackgroundModeChanged -= OnBackgroundModeChanged;
		}
		if (_ownsSingleInstanceMutex)
		{
			_singleInstanceMutex?.ReleaseMutex();
		}
		_singleInstanceMutex?.Dispose();
		_showWindowEvent?.Dispose();
	}

	private static void SignalExistingInstance()
	{
		using EventWaitHandle showWindowEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowWindowEventName);
		showWindowEvent.Set();
	}

	private void StartShowWindowListener()
	{
		Thread thread = new Thread(delegate()
		{
			while (!_isExiting)
			{
				_showWindowEvent?.WaitOne();
				if (!_isExiting)
				{
					Dispatcher.Invoke(RestoreMainWindow);
				}
			}
		})
		{
			IsBackground = true
		};
		thread.Start();
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
