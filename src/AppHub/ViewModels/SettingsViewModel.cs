using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppHub.Infrastructure;
using AppHub.Models;
using CommunityToolkit.Mvvm.Input;

namespace AppHub.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
	private const int MinRefreshIntervalMs = 1000;

	private const int MaxRefreshIntervalMs = 60000;

	private const int MinCloseGracePeriodMs = 200;

	private const int MaxCloseGracePeriodMs = 120000;

	private const int DefaultCloseGracePeriodMs = 5000;

	private const int DefaultBackgroundStrength = 40;

	private const int DefaultLightPanelOpacity = 92;

	private const int DefaultDarkPanelOpacity = 88;

	private readonly AppSettings _settings;

	private bool _isDarkMode;

	private bool _isCompactList;

	private bool _isEditingDarkBackground;

	private bool _autoStart;

	private double _refreshIntervalMs;

	private double _closeGracePeriodMs;

	private bool _allowForceKill;

	private bool _alwaysOnTop;

	private string _logDirectory = string.Empty;

	private double _logMaxSizeMb;

	private bool _isLoggingEnabled;

	private LaunchBehavior _launchBehavior;

	private string _lightBackgroundImagePath = string.Empty;

	private BackgroundStyle _lightBackgroundStyle;

	private double _lightBackgroundEffectStrength;

	private string _darkBackgroundImagePath = string.Empty;

	private BackgroundStyle _darkBackgroundStyle;

	private double _darkBackgroundEffectStrength;

	private double _panelOpacity;

	private double _trayFrequentCount;

	private bool _closeToTray;

	private TrayLeftClickAction _trayLeftClickAction;

	private string _globalHotkey = string.Empty;

	private string _globalHotkeyError = string.Empty;

	public IRelayCommand SelectLightBgTabCommand { get; }

	public IRelayCommand SelectDarkBgTabCommand { get; }

	public SettingsViewModel()
	{
		_settings = AppServices.Config.Settings;
		_isDarkMode = _settings.IsDarkMode;
		_isCompactList = _settings.IsCompactList;
		_isEditingDarkBackground = _settings.IsDarkMode;
		_autoStart = _settings.AutoStart;
		_refreshIntervalMs = _settings.RefreshIntervalMs;
		_closeGracePeriodMs = _settings.CloseGracePeriodMs;
		_allowForceKill = _settings.AllowForceKill;
		_alwaysOnTop = _settings.AlwaysOnTop;
		_trayFrequentCount = Math.Clamp(_settings.TrayFrequentCount, 0, 15);
		_closeToTray = _settings.CloseToTray;
		_trayLeftClickAction = _settings.TrayLeftClickAction;
		_globalHotkey = _settings.GlobalHotkey ?? string.Empty;
		_logDirectory = _settings.LogDirectory;
		_logMaxSizeMb = _settings.LogMaxSizeMb;
		_isLoggingEnabled = _settings.IsLoggingEnabled;
		_launchBehavior = _settings.LaunchBehavior;
		_lightBackgroundImagePath = _settings.LightBackgroundImagePath ?? string.Empty;
		_lightBackgroundStyle = NormalizeStyle(_settings.LightBackgroundStyle);
		_lightBackgroundEffectStrength = NormalizeStrength(_settings.LightBackgroundEffectStrength);
		_darkBackgroundImagePath = _settings.DarkBackgroundImagePath ?? string.Empty;
		_darkBackgroundStyle = NormalizeStyle(_settings.DarkBackgroundStyle);
		_darkBackgroundEffectStrength = NormalizeStrength(_settings.DarkBackgroundEffectStrength);
		int lightSidebarOpacity = NormalizeOpacity(_settings.LightSidebarOpacity, DefaultLightPanelOpacity);
		int lightProgramOpacity = NormalizeOpacity(_settings.LightProgramOpacity, DefaultLightPanelOpacity);
		int darkSidebarOpacity = NormalizeOpacity(_settings.DarkSidebarOpacity, DefaultDarkPanelOpacity);
		int darkProgramOpacity = NormalizeOpacity(_settings.DarkProgramOpacity, DefaultDarkPanelOpacity);
		_panelOpacity = ResolvePanelOpacity(
			lightSidebarOpacity,
			lightProgramOpacity,
			darkSidebarOpacity,
			darkProgramOpacity,
			_settings.IsDarkMode);
		int normalizedPanelOpacity = (int)_panelOpacity;
		bool requiresNormalization =
			lightSidebarOpacity != normalizedPanelOpacity ||
			lightProgramOpacity != normalizedPanelOpacity ||
			darkSidebarOpacity != normalizedPanelOpacity ||
			darkProgramOpacity != normalizedPanelOpacity;
		ApplyUnifiedPanelOpacity(normalizedPanelOpacity, persistChanges: requiresNormalization);

		SelectLightBgTabCommand = new RelayCommand(delegate
		{
			IsEditingDarkBackground = false;
		});
		SelectDarkBgTabCommand = new RelayCommand(delegate
		{
			IsEditingDarkBackground = true;
		});
	}

	public void SyncFromGlobalSettings()
	{
		if (_isDarkMode != _settings.IsDarkMode)
		{
			_isDarkMode = _settings.IsDarkMode;
			OnPropertyChanged("IsDarkMode");
		}
		if (_isCompactList != _settings.IsCompactList)
		{
			_isCompactList = _settings.IsCompactList;
			OnPropertyChanged("IsCompactList");
		}
		int clampedFrequent = Math.Clamp(_settings.TrayFrequentCount, 0, 15);
		if ((int)_trayFrequentCount != clampedFrequent)
		{
			_trayFrequentCount = clampedFrequent;
			OnPropertyChanged("TrayFrequentCount");
			OnPropertyChanged("TrayFrequentCountText");
		}
	}

	public bool IsDarkMode
	{
		get
		{
			return _isDarkMode;
		}
		set
		{
			if (SetProperty(ref _isDarkMode, value, "IsDarkMode"))
			{
				_settings.IsDarkMode = value;
				IsEditingDarkBackground = value;
				ThemeService.ApplyTheme(value);
				BackgroundEffectService.Apply(Application.Current?.MainWindow, _settings);
				if (Application.Current?.MainWindow is MainWindow mainWin)
				{
					mainWin.SyncThemeButtonVisual();
				}
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public bool IsCompactList
	{
		get
		{
			return _isCompactList;
		}
		set
		{
			if (SetProperty(ref _isCompactList, value, "IsCompactList"))
			{
				_settings.IsCompactList = value;
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public bool IsEditingDarkBackground
	{
		get
		{
			return _isEditingDarkBackground;
		}
		set
		{
			if (SetProperty(ref _isEditingDarkBackground, value, "IsEditingDarkBackground"))
			{
				OnPropertyChanged("IsEditingLightBackground");
			}
		}
	}

	public bool IsEditingLightBackground
	{
		get
		{
			return !_isEditingDarkBackground;
		}
		set
		{
			IsEditingDarkBackground = !value;
		}
	}

	public bool AutoStart
	{
		get
		{
			return _autoStart;
		}
		set
		{
			if (SetProperty(ref _autoStart, value, "AutoStart"))
			{
				_settings.AutoStart = value;
				if (value)
				{
					AppServices.AutoStartService.Enable();
				}
				else
				{
					AppServices.AutoStartService.Disable();
				}
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public double RefreshIntervalMs
	{
		get
		{
			return _refreshIntervalMs;
		}
		set
		{
			if (SetProperty(ref _refreshIntervalMs, value, "RefreshIntervalMs"))
			{
				int normalized = NormalizeInterval(value, MinRefreshIntervalMs, MaxRefreshIntervalMs, AppSettings.DefaultRefreshIntervalMs);
				_settings.RefreshIntervalMs = normalized;
				if (!double.Equals(_refreshIntervalMs, normalized))
				{
					_refreshIntervalMs = normalized;
					OnPropertyChanged("RefreshIntervalMs");
				}
				OnPropertyChanged("RefreshIntervalSeconds");
				AppServices.Storage.ScheduleSave(AppServices.Config);
				AppServices.StatusScheduler.RequestImmediateRefresh();
			}
		}
	}

	public double RefreshIntervalSeconds
	{
		get
		{
			return Math.Round(_refreshIntervalMs / 1000.0, 1);
		}
		set
		{
			RefreshIntervalMs = Math.Round(Math.Clamp(value, 1, 60) * 1000.0);
		}
	}

	public double CloseGracePeriodMs
	{
		get
		{
			return _closeGracePeriodMs;
		}
		set
		{
			if (SetProperty(ref _closeGracePeriodMs, value, "CloseGracePeriodMs"))
			{
				int normalized = NormalizeInterval(value, MinCloseGracePeriodMs, MaxCloseGracePeriodMs, DefaultCloseGracePeriodMs);
				_settings.CloseGracePeriodMs = normalized;
				if (!double.Equals(_closeGracePeriodMs, normalized))
				{
					_closeGracePeriodMs = normalized;
					OnPropertyChanged("CloseGracePeriodMs");
				}
				OnPropertyChanged("CloseGracePeriodSeconds");
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public double CloseGracePeriodSeconds
	{
		get
		{
			return Math.Round(_closeGracePeriodMs / 1000.0, 1);
		}
		set
		{
			CloseGracePeriodMs = Math.Round(Math.Clamp(value, 0.5, 120) * 1000.0);
		}
	}

	public bool AllowForceKill
	{
		get
		{
			return _allowForceKill;
		}
		set
		{
			if (SetProperty(ref _allowForceKill, value, "AllowForceKill"))
			{
				_settings.AllowForceKill = value;
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public bool AlwaysOnTop
	{
		get
		{
			return _alwaysOnTop;
		}
		set
		{
			if (SetProperty(ref _alwaysOnTop, value, "AlwaysOnTop"))
			{
				_settings.AlwaysOnTop = value;
				Window? window = Application.Current?.MainWindow;
				if (window != null)
				{
					window.Topmost = value;
				}
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public double TrayFrequentCount
	{
		get
		{
			return _trayFrequentCount;
		}
		set
		{
			int normalized = Math.Clamp((int)Math.Round(value), 0, 15);
			if (SetProperty(ref _trayFrequentCount, normalized, "TrayFrequentCount"))
			{
				_settings.TrayFrequentCount = normalized;
				OnPropertyChanged("TrayFrequentCountText");
				AppServices.Storage.ScheduleSave(AppServices.Config);
				AppServices.NotifyTraySettingsChanged();
			}
		}
	}

	public string TrayFrequentCountText => (int)_trayFrequentCount <= 0 ? "已隐藏" : $"{(int)_trayFrequentCount} 个";

	public bool CloseToTray
	{
		get => _closeToTray;
		set
		{
			if (SetProperty(ref _closeToTray, value, nameof(CloseToTray)))
			{
				_settings.CloseToTray = value;
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public TrayLeftClickAction TrayLeftClickAction
	{
		get => _trayLeftClickAction;
		set
		{
			if (SetProperty(ref _trayLeftClickAction, value, nameof(TrayLeftClickAction)))
			{
				_settings.TrayLeftClickAction = value;
				AppServices.Storage.ScheduleSave(AppServices.Config);
				AppServices.NotifyTraySettingsChanged();
			}
		}
	}

	public string GlobalHotkey
	{
		get => _globalHotkey;
		set
		{
			string normalized = value ?? string.Empty;
			if (SetProperty(ref _globalHotkey, normalized, nameof(GlobalHotkey)))
			{
				_settings.GlobalHotkey = normalized;
				if (Application.Current?.MainWindow is MainWindow mainWindow)
				{
					mainWindow.ApplyGlobalHotkey();
				}
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public string GlobalHotkeyError => _globalHotkeyError;

	public bool HasGlobalHotkeyError => !string.IsNullOrEmpty(_globalHotkeyError);

	public void SetGlobalHotkeyError(string error)
	{
		if (SetProperty(ref _globalHotkeyError, error, nameof(GlobalHotkeyError)))
		{
			OnPropertyChanged(nameof(HasGlobalHotkeyError));
		}
	}

	public string LogDirectory
	{
		get
		{
			return _logDirectory;
		}
		set
		{
			string normalized = value ?? string.Empty;
			if (SetProperty(ref _logDirectory, normalized, "LogDirectory"))
			{
				_settings.LogDirectory = normalized;
				AppServices.Logger.SetLogDirectory(normalized);
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public double LogMaxSizeMb
	{
		get
		{
			return _logMaxSizeMb;
		}
		set
		{
			if (SetProperty(ref _logMaxSizeMb, value, "LogMaxSizeMb"))
			{
				int normalized = value < 0.0 ? 0 : (int)value;
				_settings.LogMaxSizeMb = normalized;
				AppServices.Logger.SetLogMaxSizeBytes((long)normalized * 1024L * 1024);
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public bool IsLoggingEnabled
	{
		get
		{
			return _isLoggingEnabled;
		}
		set
		{
			if (SetProperty(ref _isLoggingEnabled, value, "IsLoggingEnabled"))
			{
				_settings.IsLoggingEnabled = value;
				AppServices.Logger.SetEnabled(value);
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public LaunchBehavior LaunchBehavior
	{
		get
		{
			return _launchBehavior;
		}
		set
		{
			if (SetProperty(ref _launchBehavior, value, "LaunchBehavior"))
			{
				_settings.LaunchBehavior = value;
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public string LightBackgroundImagePath
	{
		get
		{
			return _lightBackgroundImagePath;
		}
		set
		{
			string normalized = value ?? string.Empty;
			if (SetProperty(ref _lightBackgroundImagePath, normalized, "LightBackgroundImagePath"))
			{
				_settings.LightBackgroundImagePath = normalized;
				OnPropertyChanged("LightBackgroundPreview");
				OnPropertyChanged("HasLightBackgroundPreview");
				ApplyAndSaveBackgroundEffect();
			}
		}
	}

	public ImageSource? LightBackgroundPreview => TryLoadPreview(_lightBackgroundImagePath);

	public bool HasLightBackgroundPreview => !string.IsNullOrWhiteSpace(_lightBackgroundImagePath) && File.Exists(_lightBackgroundImagePath);

	public BackgroundStyle LightBackgroundStyle
	{
		get
		{
			return _lightBackgroundStyle;
		}
		set
		{
			BackgroundStyle normalized = NormalizeStyle(value);
			if (SetProperty(ref _lightBackgroundStyle, normalized, "LightBackgroundStyle"))
			{
				_settings.LightBackgroundStyle = normalized;
				OnPropertyChanged("IsLightEffectStrengthEnabled");
				ApplyAndSaveBackgroundEffect();
			}
		}
	}

	public double LightBackgroundEffectStrength
	{
		get
		{
			return _lightBackgroundEffectStrength;
		}
		set
		{
			if (SetProperty(ref _lightBackgroundEffectStrength, value, "LightBackgroundEffectStrength"))
			{
				int normalized = NormalizeStrength((int)value);
				_settings.LightBackgroundEffectStrength = normalized;
				if (!double.Equals(_lightBackgroundEffectStrength, normalized))
				{
					_lightBackgroundEffectStrength = normalized;
					OnPropertyChanged("LightBackgroundEffectStrength");
				}
				ApplyAndSaveBackgroundEffect();
			}
		}
	}

	public string DarkBackgroundImagePath
	{
		get
		{
			return _darkBackgroundImagePath;
		}
		set
		{
			string normalized = value ?? string.Empty;
			if (SetProperty(ref _darkBackgroundImagePath, normalized, "DarkBackgroundImagePath"))
			{
				_settings.DarkBackgroundImagePath = normalized;
				OnPropertyChanged("DarkBackgroundPreview");
				OnPropertyChanged("HasDarkBackgroundPreview");
				ApplyAndSaveBackgroundEffect();
			}
		}
	}

	public ImageSource? DarkBackgroundPreview => TryLoadPreview(_darkBackgroundImagePath);

	public bool HasDarkBackgroundPreview => !string.IsNullOrWhiteSpace(_darkBackgroundImagePath) && File.Exists(_darkBackgroundImagePath);

	public BackgroundStyle DarkBackgroundStyle
	{
		get
		{
			return _darkBackgroundStyle;
		}
		set
		{
			BackgroundStyle normalized = NormalizeStyle(value);
			if (SetProperty(ref _darkBackgroundStyle, normalized, "DarkBackgroundStyle"))
			{
				_settings.DarkBackgroundStyle = normalized;
				OnPropertyChanged("IsDarkEffectStrengthEnabled");
				ApplyAndSaveBackgroundEffect();
			}
		}
	}

	public double DarkBackgroundEffectStrength
	{
		get
		{
			return _darkBackgroundEffectStrength;
		}
		set
		{
			if (SetProperty(ref _darkBackgroundEffectStrength, value, "DarkBackgroundEffectStrength"))
			{
				int normalized = NormalizeStrength((int)value);
				_settings.DarkBackgroundEffectStrength = normalized;
				if (!double.Equals(_darkBackgroundEffectStrength, normalized))
				{
					_darkBackgroundEffectStrength = normalized;
					OnPropertyChanged("DarkBackgroundEffectStrength");
				}
				ApplyAndSaveBackgroundEffect();
			}
		}
	}

	public bool IsLightEffectStrengthEnabled => LightBackgroundStyle == BackgroundStyle.FrostedGlass || LightBackgroundStyle == BackgroundStyle.Dimmed;

	public bool IsDarkEffectStrengthEnabled => DarkBackgroundStyle == BackgroundStyle.FrostedGlass || DarkBackgroundStyle == BackgroundStyle.Dimmed;

	public double PanelOpacity
	{
		get
		{
			return _panelOpacity;
		}
		set
		{
			if (SetProperty(ref _panelOpacity, value, "PanelOpacity"))
			{
				int fallback = _settings.IsDarkMode ? DefaultDarkPanelOpacity : DefaultLightPanelOpacity;
				int normalized = Math.Clamp(NormalizeOpacity((int)value, fallback), BackgroundEffectService.MinReadablePanelOpacity, 100);
				if (!double.Equals(_panelOpacity, normalized))
				{
					_panelOpacity = normalized;
					OnPropertyChanged("PanelOpacity");
				}
				ApplyUnifiedPanelOpacity(normalized, persistChanges: true);
			}
		}
	}

	public void ResetLightBackground()
	{
		LightBackgroundImagePath = string.Empty;
		LightBackgroundStyle = BackgroundStyle.None;
		LightBackgroundEffectStrength = DefaultBackgroundStrength;
	}

	public void ResetLightBackgroundStrength()
	{
		LightBackgroundEffectStrength = DefaultBackgroundStrength;
	}

	public void ResetDarkBackground()
	{
		DarkBackgroundImagePath = string.Empty;
		DarkBackgroundStyle = BackgroundStyle.None;
		DarkBackgroundEffectStrength = DefaultBackgroundStrength;
	}

	public void ResetDarkBackgroundStrength()
	{
		DarkBackgroundEffectStrength = DefaultBackgroundStrength;
	}

	public void ResetPanelOpacity()
	{
		PanelOpacity = _settings.IsDarkMode ? DefaultDarkPanelOpacity : DefaultLightPanelOpacity;
	}

	private void ApplyUnifiedPanelOpacity(int normalizedOpacity, bool persistChanges)
	{
		_settings.LightSidebarOpacity = normalizedOpacity;
		_settings.LightProgramOpacity = normalizedOpacity;
		_settings.DarkSidebarOpacity = normalizedOpacity;
		_settings.DarkProgramOpacity = normalizedOpacity;
		ApplyBackgroundEffect();
		if (persistChanges)
		{
			AppServices.Storage.ScheduleSave(AppServices.Config);
		}
	}

	private void ApplyAndSaveBackgroundEffect()
	{
		ApplyBackgroundEffect();
		AppServices.Storage.ScheduleSave(AppServices.Config);
	}

	private void ApplyBackgroundEffect()
	{
		Window? window = Application.Current?.MainWindow;
		BackgroundEffectService.Apply(window, _settings);
	}

	private static ImageSource? TryLoadPreview(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		try
		{
			BitmapImage bitmap = new BitmapImage();
			bitmap.BeginInit();
			bitmap.UriSource = new Uri(path, UriKind.Absolute);
			bitmap.DecodePixelWidth = 240;
			bitmap.CacheOption = BitmapCacheOption.OnLoad;
			bitmap.EndInit();
			bitmap.Freeze();
			return bitmap;
		}
		catch
		{
			return null;
		}
	}

	private static int NormalizeInterval(double value, int min, int max, int fallback)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return fallback;
		}
		int parsed = (int)value;
		if (parsed <= 0)
		{
			return fallback;
		}
		return Math.Clamp(parsed, min, max);
	}

	private static int NormalizeStrength(int strength)
	{
		if (strength < 0 || strength > 100)
		{
			return DefaultBackgroundStrength;
		}
		return strength;
	}

	private static int NormalizeOpacity(int opacity, int fallback)
	{
		if (opacity < 0 || opacity > 100)
		{
			return fallback;
		}
		return opacity;
	}

	private static int ResolvePanelOpacity(
		double lightSidebarOpacity,
		double lightProgramOpacity,
		double darkSidebarOpacity,
		double darkProgramOpacity,
		bool preferDark)
	{
		int normalizedLightSidebarOpacity = NormalizeOpacity((int)lightSidebarOpacity, DefaultLightPanelOpacity);
		int normalizedLightProgramOpacity = NormalizeOpacity((int)lightProgramOpacity, DefaultLightPanelOpacity);
		int normalizedDarkSidebarOpacity = NormalizeOpacity((int)darkSidebarOpacity, DefaultDarkPanelOpacity);
		int normalizedDarkProgramOpacity = NormalizeOpacity((int)darkProgramOpacity, DefaultDarkPanelOpacity);
		int resolved = (normalizedLightSidebarOpacity == normalizedLightProgramOpacity &&
		                normalizedLightSidebarOpacity == normalizedDarkSidebarOpacity &&
		                normalizedLightSidebarOpacity == normalizedDarkProgramOpacity)
			? normalizedLightSidebarOpacity
			: (preferDark ? normalizedDarkSidebarOpacity : normalizedLightSidebarOpacity);
		return Math.Clamp(resolved, BackgroundEffectService.MinReadablePanelOpacity, 100);
	}

	private static BackgroundStyle NormalizeStyle(BackgroundStyle style)
	{
		return Enum.IsDefined(typeof(BackgroundStyle), style) ? style : BackgroundStyle.None;
	}
}
