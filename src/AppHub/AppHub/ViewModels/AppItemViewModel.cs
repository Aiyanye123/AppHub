using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
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

	private ImageSource? _icon;

	private bool _isRunning;

	private string _displayName;

	private string _groupName;

	private bool _isPinned;

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

	public string GroupDisplayName => string.IsNullOrWhiteSpace(_groupName) ? "\u672a\u5206\u7ec4" : _groupName;

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
			}
		}
	}

	public DateTime? LastLaunchTime => _lastLaunchTime;

	public long LastLaunchTicks => _lastLaunchTime?.Ticks ?? long.MinValue;

	public int SortIndex => _model.SortIndex;

	public string PinMenuText => IsPinned ? "\u53d6\u6d88\u7f6e\u9876" : "\u7f6e\u9876";

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
				OnPropertyChanged("StatusText");
			}
		}
	}

	public string StatusText => IsRunning ? "\u8fd0\u884c\u4e2d" : "\u672a\u8fd0\u884c";

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

	public AppItemViewModel(ApplicationItem model, LaunchService launchService, ProcessControlService processService, IconService iconService, Action<AppItemViewModel>? editRequested, Action<AppItemViewModel>? removeRequested, Action<AppItemViewModel>? togglePinRequested)
	{
		_model = model;
		_launchService = launchService;
		_processService = processService;
		_iconService = iconService;
		_editRequested = editRequested;
		_removeRequested = removeRequested;
		_togglePinRequested = togglePinRequested;
		_displayName = _model.DisplayName;
		_groupName = NormalizeGroupName(_model.GroupName);
		_isPinned = ResolvePinnedForScope(_activeGroupScope);
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
		}, CanInteract);

		RefreshIcon();
		NotifyCommandStateChanged();
	}

	public void SetActiveGroupScope(string groupScope)
	{
		string normalized = AppGroupScope.Normalize(groupScope);
		if (!string.Equals(_activeGroupScope, normalized, StringComparison.Ordinal))
		{
			_activeGroupScope = normalized;
			IsPinned = ResolvePinnedForScope(_activeGroupScope);
		}
	}

	public void SetPinnedForActiveGroup(bool isPinned)
	{
		_model.SetPinned(_activeGroupScope, isPinned);
		if (string.Equals(_activeGroupScope, AppGroupScope.AllGroups, StringComparison.Ordinal))
		{
			_model.IsPinned = isPinned;
		}
		IsPinned = isPinned;
	}

	public void UpdateStatus(ProcessStatus status)
	{
		IsRunning = status.IsRunning;
		NotifyCommandStateChanged();
	}

	public void RefreshIcon()
	{
		Icon = _iconService.GetIcon(_model);
	}

	public void SyncFromModel()
	{
		_displayName = _model.DisplayName;
		_groupName = NormalizeGroupName(_model.GroupName);
		_isPinned = ResolvePinnedForScope(_activeGroupScope);
		_lastLaunchTime = _model.LastLaunchTime;
		OnPropertyChanged("DisplayName");
		OnPropertyChanged("GroupName");
		OnPropertyChanged("GroupDisplayName");
		OnPropertyChanged("IsPinned");
		OnPropertyChanged("LastLaunchTime");
		OnPropertyChanged("LastLaunchTicks");
		OnPropertyChanged("SortIndex");
		OnPropertyChanged("PinMenuText");
		RefreshIcon();
		NotifyCommandStateChanged();
	}

	private void OnLaunchRequested()
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
				return;
			}
			ShowActionError("\u542f\u52a8\u5931\u8d25", result.ErrorMessage);
		}
		finally
		{
			IsActionRunning = false;
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
				ShowActionError(force ? "\u5f3a\u5236\u7ed3\u675f\u5931\u8d25" : "\u5173\u95ed\u5931\u8d25", result.ErrorMessage);
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
		string text = string.IsNullOrWhiteSpace(message) ? "\u64cd\u4f5c\u5931\u8d25\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u3002" : message;
		MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
	}

	private static string NormalizeGroupName(string? groupName)
	{
		return string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName.Trim();
	}

	private bool ResolvePinnedForScope(string groupScope)
	{
		bool scopedPinned = _model.GetPinned(groupScope);
		if (scopedPinned)
		{
			return true;
		}
		if (string.Equals(AppGroupScope.Normalize(groupScope), AppGroupScope.AllGroups, StringComparison.Ordinal) && _model.IsPinned)
		{
			_model.SetPinned(AppGroupScope.AllGroups, isPinned: true);
			return true;
		}
		return false;
	}
}
