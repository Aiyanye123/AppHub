using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AppHub.Infrastructure;
using AppHub.ViewModels;

namespace AppHub;

public partial class MainWindow : Window
{
	private const double ExpandedSidebarWidth = 208;

	private const double CollapsedSidebarWidth = 76;

	private bool _allowClose;

	private bool _isSidebarCollapsed;

	private Action? _currentToastAction;

	private DispatcherTimer? _toastTimer;

	private readonly GlobalHotkeyService _globalHotkeyService;

	private readonly Queue<ToastMessage> _pendingToasts = new Queue<ToastMessage>();

	private FrameworkElement[] SidebarTextElements => new FrameworkElement[]
	{
		BrandCopy,
		OverviewNavText,
		SettingsNavText,
		ThemeToggleText,
		SidebarToggleText
	};

	public MainWindow()
	{
		InitializeComponent();
		base.Closing += OnClosing;
		base.SourceInitialized += OnSourceInitialized;
		base.Closed += OnClosed;
		ToastService.ToastRequested += OnToastRequested;
		BackgroundEffectService.Apply(this, AppServices.Config.Settings);
		SyncThemeButtonVisual();
		_globalHotkeyService = new GlobalHotkeyService(this, ShowLauncher);
		ApplyGlobalHotkey();
	}

	public void RequestClose()
	{
		_allowClose = true;
		Close();
	}

	public void NavigateToSettings()
	{
		if (SettingsPageView?.DataContext is SettingsViewModel settingsVm)
		{
			settingsVm.SyncFromGlobalSettings();
		}
		MainTabControl.SelectedIndex = 1;
	}

	public void ShowLauncher()
	{
		App.TrayIconService?.Restore();
		MainTabControl.SelectedIndex = 0;
		Dispatcher.BeginInvoke(new Action(() => OverviewPageView.FocusSearch()), DispatcherPriority.Input);
	}

	public void ApplyGlobalHotkey()
	{
		_globalHotkeyService.Update(AppServices.Config.Settings.GlobalHotkey);
		if (SettingsPageView?.DataContext is SettingsViewModel settingsVm)
		{
			settingsVm.SetGlobalHotkeyError(_globalHotkeyService.Error);
		}
	}

	public void SyncThemeButtonVisual()
	{
		bool isDark = AppServices.Config.Settings.IsDarkMode;
		ThemeToggleIcon.Text = isDark ? "\uE706" : "\uE708";
		ThemeToggleText.Text = isDark ? "切换浅色" : "切换深色";
		ThemeToggleButton.ToolTip = isDark ? "当前：深色模式（点击切换浅色）" : "当前：浅色模式（点击切换深色）";
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		ThemeService.ApplyWindowChromeTheme(this, AppServices.Config.Settings.IsDarkMode);
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		_globalHotkeyService.Dispose();
		ToastService.ToastRequested -= OnToastRequested;
		_toastTimer?.Stop();
		_currentToastAction = null;
		_pendingToasts.Clear();
		Application.Current?.Shutdown();
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (!_allowClose && AppServices.Config.Settings.CloseToTray)
		{
			e.Cancel = true;
			App.TrayIconService?.HideToTray(showHint: true);
			return;
		}
		_globalHotkeyService.Dispose();
	}

	private void OnToggleThemeClick(object sender, RoutedEventArgs e)
	{
		if (OverviewPageView?.DataContext is OverviewViewModel overviewVm)
		{
			overviewVm.IsDarkMode = !overviewVm.IsDarkMode;
		}
		else
		{
			bool nextDark = !AppServices.Config.Settings.IsDarkMode;
			AppServices.Config.Settings.IsDarkMode = nextDark;
			ThemeService.ApplyTheme(nextDark);
			BackgroundEffectService.Apply(this, AppServices.Config.Settings);
			AppServices.Storage.ScheduleSave(AppServices.Config);
		}
		if (SettingsPageView?.DataContext is SettingsViewModel settingsVm)
		{
			settingsVm.SyncFromGlobalSettings();
		}
		SyncThemeButtonVisual();
	}

	private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
	{
		_isSidebarCollapsed = !_isSidebarCollapsed;
		double targetWidth = _isSidebarCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth;
		DoubleAnimation widthAnimation = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(180))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		SidebarRail.BeginAnimation(WidthProperty, widthAnimation);
		AnimateSidebarText(!_isSidebarCollapsed);
		BrandContainer.Margin = _isSidebarCollapsed ? new Thickness(12, 22, 12, 24) : new Thickness(18, 22, 18, 24);
		BrandRow.HorizontalAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
		MainTabControl.Margin = _isSidebarCollapsed ? new Thickness(10, 0, 10, 16) : new Thickness(10, 0, 10, 16);
		SidebarFooter.Margin = _isSidebarCollapsed ? new Thickness(10, 0, 10, 18) : new Thickness(14, 0, 14, 18);
		ThemeToggleButton.HorizontalContentAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
		ThemeToggleButton.Padding = _isSidebarCollapsed ? new Thickness(0) : new Thickness(12, 0, 12, 0);
		SidebarToggleButton.HorizontalContentAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
		SidebarToggleButton.Padding = _isSidebarCollapsed ? new Thickness(0) : new Thickness(12, 0, 12, 0);
		SidebarToggleIcon.Text = _isSidebarCollapsed ? "\uE76C" : "\uE76B";
		SidebarToggleButton.ToolTip = _isSidebarCollapsed ? "展开侧边栏" : "收起侧边栏";
	}

	private void AnimateSidebarText(bool show)
	{
		foreach (FrameworkElement element in SidebarTextElements)
		{
			if (show)
			{
				element.Visibility = Visibility.Visible;
			}
			DoubleAnimation opacityAnimation = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(120));
			if (!show)
			{
				opacityAnimation.Completed += delegate
				{
					element.Visibility = Visibility.Collapsed;
				};
			}
			element.BeginAnimation(OpacityProperty, opacityAnimation);
		}
	}

	private void OnToastRequested(ToastMessage toast)
	{
		if (_toastTimer?.IsEnabled == true)
		{
			_pendingToasts.Enqueue(toast);
			return;
		}
		ShowToast(toast);
	}

	private void ShowToast(ToastMessage toast)
	{
		_currentToastAction = toast.ActionCallback;
		ToastText.Text = toast.Message;

		switch (toast.Severity)
		{
		case ToastSeverity.Success:
			ToastIcon.Text = "\uE73E";
			ToastIcon.Foreground = TryFindResource("SuccessBrush") as Brush ?? Brushes.SeaGreen;
			break;
		case ToastSeverity.Warning:
			ToastIcon.Text = "\uE7BA";
			ToastIcon.Foreground = TryFindResource("WarningBrush") as Brush ?? Brushes.Goldenrod;
			break;
		case ToastSeverity.Error:
			ToastIcon.Text = "\uEA39";
			ToastIcon.Foreground = TryFindResource("DangerBrush") as Brush ?? Brushes.IndianRed;
			break;
		default:
			ToastIcon.Text = "\uE946";
			ToastIcon.Foreground = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
			break;
		}

		if (!string.IsNullOrWhiteSpace(toast.ActionText) && toast.ActionCallback != null)
		{
			ToastActionButton.Content = toast.ActionText;
			ToastActionButton.Visibility = Visibility.Visible;
		}
		else
		{
			ToastActionButton.Visibility = Visibility.Collapsed;
		}

		ToastContainer.Visibility = Visibility.Visible;
		ToastContainer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
		ToastTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
		});

		_toastTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(Math.Clamp(toast.DurationMs, 1500, 10000))
		};
		_toastTimer.Tick += delegate
		{
			DismissCurrentToast();
		};
		_toastTimer.Start();
	}

	private void OnToastActionClick(object sender, RoutedEventArgs e)
	{
		Action? callback = _currentToastAction;
		DismissCurrentToast();
		callback?.Invoke();
	}

	private void OnDismissToastClick(object sender, RoutedEventArgs e)
	{
		DismissCurrentToast();
	}

	private void DismissCurrentToast()
	{
		_currentToastAction = null;
		_toastTimer?.Stop();
		if (_pendingToasts.Count > 0)
		{
			ShowToast(_pendingToasts.Dequeue());
			return;
		}
		HideToast();
	}

	private void HideToast()
	{
		DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
		fadeOut.Completed += delegate
		{
			if (ToastContainer.Opacity <= 0.05)
			{
				ToastContainer.Visibility = Visibility.Collapsed;
			}
		};
		ToastContainer.BeginAnimation(OpacityProperty, fadeOut);
		ToastTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(150)));
	}
}
