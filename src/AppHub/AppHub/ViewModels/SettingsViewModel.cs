using System;
using System.Windows;
using AppHub.Infrastructure;
using AppHub.Models;

namespace AppHub.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
	private const int MinRefreshIntervalMs = 1000;

	private const int MaxRefreshIntervalMs = 60000;

	private const int DefaultRefreshIntervalMs = 3000;

	private const int MinCloseGracePeriodMs = 200;

	private const int MaxCloseGracePeriodMs = 120000;

	private const int DefaultCloseGracePeriodMs = 5000;

	private const int DefaultBackgroundStrength = 40;

	private const int DefaultLightPanelOpacity = 92;

	private const int DefaultDarkPanelOpacity = 88;

	private readonly AppSettings _settings;

	private bool _autoStart;

	private double _refreshIntervalMs;

	private double _closeGracePeriodMs;

	private bool _allowForceKill;

	private bool _alwaysOnTop;

	private string _logDirectory = string.Empty;

	private double _logMaxSizeMb;

	private LaunchBehavior _launchBehavior;

	private string _lightBackgroundImagePath = string.Empty;

	private BackgroundStyle _lightBackgroundStyle;

	private double _lightBackgroundEffectStrength;

	private string _darkBackgroundImagePath = string.Empty;

	private BackgroundStyle _darkBackgroundStyle;

	private double _darkBackgroundEffectStrength;

	private double _lightSidebarOpacity;

	private double _lightProgramOpacity;

	private double _darkSidebarOpacity;

	private double _darkProgramOpacity;

	private double _panelOpacity;

	public SettingsViewModel()
	{
		_settings = AppServices.Config.Settings;
		_autoStart = _settings.AutoStart;
		_refreshIntervalMs = _settings.RefreshIntervalMs;
		_closeGracePeriodMs = _settings.CloseGracePeriodMs;
		_allowForceKill = _settings.AllowForceKill;
		_alwaysOnTop = _settings.AlwaysOnTop;
		_logDirectory = _settings.LogDirectory;
		_logMaxSizeMb = _settings.LogMaxSizeMb;
		_launchBehavior = _settings.LaunchBehavior;
		_lightBackgroundImagePath = _settings.LightBackgroundImagePath ?? string.Empty;
		_lightBackgroundStyle = NormalizeStyle(_settings.LightBackgroundStyle);
		_lightBackgroundEffectStrength = NormalizeStrength(_settings.LightBackgroundEffectStrength);
		_darkBackgroundImagePath = _settings.DarkBackgroundImagePath ?? string.Empty;
		_darkBackgroundStyle = NormalizeStyle(_settings.DarkBackgroundStyle);
		_darkBackgroundEffectStrength = NormalizeStrength(_settings.DarkBackgroundEffectStrength);
		_lightSidebarOpacity = NormalizeOpacity(_settings.LightSidebarOpacity, DefaultLightPanelOpacity);
		_lightProgramOpacity = NormalizeOpacity(_settings.LightProgramOpacity, DefaultLightPanelOpacity);
		_darkSidebarOpacity = NormalizeOpacity(_settings.DarkSidebarOpacity, DefaultDarkPanelOpacity);
		_darkProgramOpacity = NormalizeOpacity(_settings.DarkProgramOpacity, DefaultDarkPanelOpacity);
		_panelOpacity = ResolvePanelOpacity(
			_lightSidebarOpacity,
			_lightProgramOpacity,
			_darkSidebarOpacity,
			_darkProgramOpacity,
			_settings.IsDarkMode);
		int normalizedPanelOpacity = (int)_panelOpacity;
		bool requiresNormalization =
			_lightSidebarOpacity != normalizedPanelOpacity ||
			_lightProgramOpacity != normalizedPanelOpacity ||
			_darkSidebarOpacity != normalizedPanelOpacity ||
			_darkProgramOpacity != normalizedPanelOpacity;
		ApplyUnifiedPanelOpacity(normalizedPanelOpacity, persistChanges: requiresNormalization);
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
				int normalized = NormalizeInterval(value, MinRefreshIntervalMs, MaxRefreshIntervalMs, DefaultRefreshIntervalMs);
				_settings.RefreshIntervalMs = normalized;
				if (!double.Equals(_refreshIntervalMs, normalized))
				{
					_refreshIntervalMs = normalized;
					OnPropertyChanged("RefreshIntervalMs");
				}
				AppServices.Storage.ScheduleSave(AppServices.Config);
				AppServices.StatusScheduler.RequestImmediateRefresh();
			}
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
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
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
				ApplyBackgroundEffect();
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

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
				ApplyBackgroundEffect();
				AppServices.Storage.ScheduleSave(AppServices.Config);
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
				ApplyBackgroundEffect();
				AppServices.Storage.ScheduleSave(AppServices.Config);
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
				ApplyBackgroundEffect();
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

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
				ApplyBackgroundEffect();
				AppServices.Storage.ScheduleSave(AppServices.Config);
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
				ApplyBackgroundEffect();
				AppServices.Storage.ScheduleSave(AppServices.Config);
			}
		}
	}

	public bool IsLightEffectStrengthEnabled => LightBackgroundStyle != BackgroundStyle.ImageOnly;

	public bool IsDarkEffectStrengthEnabled => DarkBackgroundStyle != BackgroundStyle.ImageOnly;

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
				int normalized = NormalizeOpacity((int)value, fallback);
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
		_lightSidebarOpacity = normalizedOpacity;
		_lightProgramOpacity = normalizedOpacity;
		_darkSidebarOpacity = normalizedOpacity;
		_darkProgramOpacity = normalizedOpacity;
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

	private void ApplyBackgroundEffect()
	{
		Window? window = Application.Current?.MainWindow;
		BackgroundEffectService.Apply(window, _settings);
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
		if (normalizedLightSidebarOpacity == normalizedLightProgramOpacity &&
			normalizedLightSidebarOpacity == normalizedDarkSidebarOpacity &&
			normalizedLightSidebarOpacity == normalizedDarkProgramOpacity)
		{
			return normalizedLightSidebarOpacity;
		}
		return preferDark ? normalizedDarkSidebarOpacity : normalizedLightSidebarOpacity;
	}

	private static BackgroundStyle NormalizeStyle(BackgroundStyle style)
	{
		return Enum.IsDefined(typeof(BackgroundStyle), style) ? style : BackgroundStyle.None;
	}
}
