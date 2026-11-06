using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using AppHub.Infrastructure;
using AppHub.Models;
using AppHub.Services;
using CommunityToolkit.Mvvm.Input;

namespace AppHub.ViewModels;

public sealed class AppItemViewModel : ViewModelBase
{
	private readonly ApplicationItem _model;

	private readonly LaunchService _launchService;

	private readonly ProcessControlService _processService;

	private readonly IconService _iconService;

	private readonly Action<AppItemViewModel>? _editRequested;

	private readonly Action<AppItemViewModel>? _removeRequested;

	private readonly Action<AppItemViewModel>? _togglePinRequested;

	private readonly Action<AppItemViewModel>? _inspectRequested;

	private ImageSource? _icon;

	private bool _isRunning;

	private bool _isLaunching;

	private string _displayName;

	private string _groupName;

	private bool _isPinned;

	private bool _canTogglePin = true;

	private DateTime? _lastLaunchTime;

	private bool _isActionRunning;

	private string _activeGroupScope = AppGroupScope.AllGroups;

	public Guid Id => _model.Id;

	public ApplicationItem Model => _model;

	public string DisplayName
	{
		get
		{
			return _displayName;
		}
		set
		{
			if (SetProperty(ref _displayName, value, "DisplayName"))
			{
				_model.DisplayName = value;
			}
		}
	}

	public ImageSource? Icon
	{
		get
		{
			return _icon;
		}
		private set
		{
			SetProperty(ref _icon, value, "Icon");
		}
	}

	public string GroupName => _groupName;

	public string GroupDisplayName => string.IsNullOrWhiteSpace(_groupName) ? "未分组" : _groupName;

	public string SourceTypeText
	{
		get
		{
			if (_model.SourceType == SourceType.Folder)
			{
				return "文件夹";
			}
			if (_model.SourceType == SourceType.Shortcut)
			{
				return "LNK";
			}
			string ext = Path.GetExtension(_model.TargetPath ?? string.Empty);
			if (string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
			{
				return "BAT";
			}
			return "EXE";
		}
	}

	public string SourceBadgeGlyph
	{
		get
		{
			if (_model.SourceType == SourceType.Folder)
			{
				return "\uE8B7";
			}
			if (_model.SourceType == SourceType.Shortcut)
			{
				return "\uE71B";
			}
			string ext = Path.GetExtension(_model.TargetPath ?? string.Empty);
			if (string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
			{
				return "\uE756";
			}
			return string.Empty;
		}
	}

	public bool ShowSourceBadge => !string.IsNullOrEmpty(SourceBadgeGlyph);

	public string TargetPath => _model.TargetPath;

	public string WorkingDirectoryDisplay => string.IsNullOrWhiteSpace(_model.WorkingDirectory) ? "—" : _model.WorkingDirectory;

	public string ArgumentsDisplay => string.IsNullOrWhiteSpace(_model.Arguments) ? "—" : _model.Arguments;

	public string TrackProcessText => _model.TrackProcess ? "已启用" : "不跟踪";

	public bool IsPinned
	{
		get
		{
			return _isPinned;
		}
		set
		{
			if (SetProperty(ref _isPinned, value, "IsPinned"))
			{
				OnPropertyChanged("PinMenuText");
				OnPropertyChanged("ShowPinBadge");
			}
		}
	}

	public bool CanTogglePin
	{
		get
		{
			return _canTogglePin;
		}
		set
		{
			if (SetProperty(ref _canTogglePin, value, "CanTogglePin"))
			{
				OnPropertyChanged("ShowPinBadge");
				TogglePinCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public DateTime? LastLaunchTime => _lastLaunchTime;

	public long LastLaunchTicks => _lastLaunchTime?.Ticks ?? long.MinValue;

	public string LastLaunchDisplay => _lastLaunchTime.HasValue ? _lastLaunchTime.Value.ToString("yyyy/MM/dd HH:mm") : "—";

	public string LastLaunchRelativeDisplay
	{
		get
		{
			if (!_lastLaunchTime.HasValue)
			{
				return "—";
			}
			TimeSpan span = DateTime.Now - _lastLaunchTime.Value;
			if (span.TotalSeconds < 60)
			{
				return "刚刚";
			}
			if (span.TotalMinutes < 60)
			{
				return $"{(int)span.TotalMinutes} 分钟前";
			}
			if (span.TotalHours < 24)
			{
				return $"{(int)span.TotalHours} 小时前";
			}
			if (span.TotalDays < 30)
			{
				return $"{(int)span.TotalDays} 天前";
			}
			if (span.TotalDays < 365)
			{
				return $"{Math.Max(1, (int)(span.TotalDays / 30))} 个月前";
			}
			return _lastLaunchTime.Value.ToString("yyyy/MM/dd");
		}
	}

	public string LastLaunchTooltip => _lastLaunchTime.HasValue
		? $"最近启动时间：{_lastLaunchTime.Value:yyyy/MM/dd HH:mm:ss}"
		: "尚无启动记录";

	public int SortIndex => _model.SortIndex;

	public string PinMenuText => IsPinned ? "取消置顶" : "置顶";

	public bool ShowPinBadge => IsPinned && CanTogglePin;

	public bool IsRunning
	{
		get
		{
			return _isRunning;
		}
		private set
		{
			if (SetProperty(ref _isRunning, value, "IsRunning"))
			{
				if (value)
				{
					IsLaunching = false;
				}
				OnPropertyChanged("StatusText");
				OnPropertyChanged("ActiveStatusText");
				OnPropertyChanged("ShowStatusBadge");
			}
		}
	}

	public bool IsLaunching
	{
		get
		{
			return _isLaunching;
		}
		private set
		{
			if (SetProperty(ref _isLaunching, value, "IsLaunching"))
			{
				OnPropertyChanged("StatusText");
				OnPropertyChanged("ActiveStatusText");
				OnPropertyChanged("ShowStatusBadge");
			}
		}
	}

	public bool ShowStatusBadge => IsRunning || IsLaunching;

	public string StatusText => IsRunning ? "运行中" : (IsLaunching ? "启动中…" : "未运行");

	public string ActiveStatusText => IsRunning ? "运行中" : (IsLaunching ? "启动中…" : string.Empty);

	public bool IsActionRunning
	{
		get
		{
			return _isActionRunning;
		}
		private set
		{
			if (SetProperty(ref _isActionRunning, value, "IsActionRunning"))
			{
				NotifyCommandStateChanged();
			}
		}
	}

	public IRelayCommand LaunchCommand { get; }

	public IAsyncRelayCommand CloseCommand { get; }

	public IAsyncRelayCommand ForceCloseCommand { get; }

	public IRelayCommand OpenLocationCommand { get; }

	public IRelayCommand EditCommand { get; }

	public IRelayCommand RemoveCommand { get; }

	public IRelayCommand TogglePinCommand { get; }

	public IRelayCommand InspectCommand { get; }

	public AppItemViewModel(
		ApplicationItem model,
		LaunchService launchService,
		ProcessControlService processService,
		IconService iconService,
		Action<AppItemViewModel>? editRequested,
		Action<AppItemViewModel>? removeRequested,
		Action<AppItemViewModel>? togglePinRequested,
		Action<AppItemViewModel>? inspectRequested = null)
	{
		_model = model;
		_launchService = launchService;
		_processService = processService;
		_iconService = iconService;
		_editRequested = editRequested;
		_removeRequested = removeRequested;
		_togglePinRequested = togglePinRequested;
		_inspectRequested = inspectRequested;
		_displayName = _model.DisplayName;
		_groupName = NormalizeGroupName(_model.GroupName);
		_isPinned = IsPinnedInActiveGroup();
		_lastLaunchTime = _model.LastLaunchTime;

		LaunchCommand = new RelayCommand(OnLaunchRequested, CanLaunch);
		CloseCommand = new AsyncRelayCommand(() => ExecuteCloseAsync(force: false), CanClose);
		ForceCloseCommand = new AsyncRelayCommand(() => ExecuteCloseAsync(force: true), CanClose);
		OpenLocationCommand = new RelayCommand(delegate
		{
			_launchService.OpenFileLocation(_model.Id);
		}, CanInteract);
		EditCommand = new RelayCommand(delegate
		{
			_editRequested?.Invoke(this);
		}, CanInteract);
		RemoveCommand = new RelayCommand(delegate
		{
			_removeRequested?.Invoke(this);
		}, CanInteract);
		TogglePinCommand = new RelayCommand(delegate
		{
			_togglePinRequested?.Invoke(this);
		}, CanTogglePinCommand);
		InspectCommand = new RelayCommand(delegate
		{
			_inspectRequested?.Invoke(this);
		});

		RefreshIcon();
		NotifyCommandStateChanged();
	}

	public void SetActiveGroupScope(string groupScope)
	{
		string normalized = AppGroupScope.Normalize(groupScope);
		if (!string.Equals(_activeGroupScope, normalized, StringComparison.Ordinal))
		{
			_activeGroupScope = normalized;
			IsPinned = IsPinnedInActiveGroup();
		}
	}

	public void UpdateStatus(ProcessStatus status)
	{
		if (_lastLaunchTime != _model.LastLaunchTime)
		{
			_lastLaunchTime = _model.LastLaunchTime;
			OnPropertyChanged("LastLaunchTime");
			OnPropertyChanged("LastLaunchTicks");
			OnPropertyChanged("LastLaunchDisplay");
			OnPropertyChanged("LastLaunchRelativeDisplay");
			OnPropertyChanged("LastLaunchTooltip");
		}
		if (status.IsRunning)
		{
			IsLaunching = false;
		}
		if (IsRunning == status.IsRunning)
		{
			return;
		}
		IsRunning = status.IsRunning;
		CloseCommand.NotifyCanExecuteChanged();
		ForceCloseCommand.NotifyCanExecuteChanged();
	}

	public void RefreshIcon()
	{
		Icon = _iconService.GetIcon(_model);
	}

	public void RefreshRelativeTime()
	{
		OnPropertyChanged("LastLaunchRelativeDisplay");
	}

	public void SyncFromModel()
	{
		_displayName = _model.DisplayName;
		_groupName = NormalizeGroupName(_model.GroupName);
		_isPinned = IsPinnedInActiveGroup();
		_lastLaunchTime = _model.LastLaunchTime;
		OnPropertyChanged("DisplayName");
		OnPropertyChanged("GroupName");
		OnPropertyChanged("GroupDisplayName");
		OnPropertyChanged("SourceTypeText");
		OnPropertyChanged("SourceBadgeGlyph");
		OnPropertyChanged("ShowSourceBadge");
		OnPropertyChanged("TargetPath");
		OnPropertyChanged("WorkingDirectoryDisplay");
		OnPropertyChanged("ArgumentsDisplay");
		OnPropertyChanged("TrackProcessText");
		OnPropertyChanged("IsPinned");
		OnPropertyChanged("LastLaunchTime");
		OnPropertyChanged("LastLaunchTicks");
		OnPropertyChanged("LastLaunchDisplay");
		OnPropertyChanged("LastLaunchRelativeDisplay");
		OnPropertyChanged("LastLaunchTooltip");
		OnPropertyChanged("SortIndex");
		OnPropertyChanged("PinMenuText");
		OnPropertyChanged("ShowPinBadge");
		RefreshIcon();
		NotifyCommandStateChanged();
	}

	private async void OnLaunchRequested()
	{
		if (IsActionRunning)
		{
			return;
		}
		IsActionRunning = true;
		try
		{
			LaunchResult result = _launchService.Launch(_model.Id);
			if (result.Success)
			{
				_lastLaunchTime = _model.LastLaunchTime;
				OnPropertyChanged("LastLaunchTime");
				OnPropertyChanged("LastLaunchTicks");
				OnPropertyChanged("LastLaunchDisplay");
				OnPropertyChanged("LastLaunchRelativeDisplay");
				OnPropertyChanged("LastLaunchTooltip");
				if (_model.TrackProcess && !IsRunning && _model.SourceType != SourceType.Folder)
				{
					IsLaunching = true;
					AppServices.StatusScheduler.RequestImmediateRefresh();
					_ = ClearLaunchingAfterDelayAsync();
				}
				return;
			}
			ShowActionError("启动失败", result.ErrorMessage);
		}
		finally
		{
			IsActionRunning = false;
		}
	}

	private async Task ClearLaunchingAfterDelayAsync()
	{
		await Task.Delay(3500);
		if (IsLaunching)
		{
			IsLaunching = false;
		}
	}

	private async Task ExecuteCloseAsync(bool force)
	{
		if (IsActionRunning)
		{
			return;
		}
		IsActionRunning = true;
		try
		{
			CloseResult result = await _processService.CloseAppAsync(_model.Id, force);
			if (!result.Success)
			{
				ShowActionError(force ? "强制结束失败" : "关闭失败", result.ErrorMessage);
			}
		}
		finally
		{
			IsActionRunning = false;
		}
	}

	private bool CanLaunch()
	{
		return CanInteract();
	}

	private bool CanClose()
	{
		return CanInteract() && IsRunning;
	}

	private bool CanInteract()
	{
		return !IsActionRunning;
	}

	private bool CanTogglePinCommand()
	{
		return CanInteract() && CanTogglePin;
	}

	private void NotifyCommandStateChanged()
	{
		LaunchCommand.NotifyCanExecuteChanged();
		CloseCommand.NotifyCanExecuteChanged();
		ForceCloseCommand.NotifyCanExecuteChanged();
		OpenLocationCommand.NotifyCanExecuteChanged();
		EditCommand.NotifyCanExecuteChanged();
		RemoveCommand.NotifyCanExecuteChanged();
		TogglePinCommand.NotifyCanExecuteChanged();
	}

	private static void ShowActionError(string title, string? message)
	{
		string text = string.IsNullOrWhiteSpace(message) ? "操作失败，请稍后重试。" : message;
		ToastService.ShowError($"{title}：{text}");
	}

	private static string NormalizeGroupName(string? groupName)
	{
		return string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName.Trim();
	}

	private bool IsPinnedInActiveGroup()
	{
		if (_model.GetPinned(_activeGroupScope))
		{
			return true;
		}
		return _model.IsPinned && string.Equals(_activeGroupScope, AppGroupScope.AllGroups, StringComparison.OrdinalIgnoreCase);
	}
}
