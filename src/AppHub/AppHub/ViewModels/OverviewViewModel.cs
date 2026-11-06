using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using AppHub.Infrastructure;
using AppHub.Models;
using AppHub.Services;
using CommunityToolkit.Mvvm.Input;

namespace AppHub.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
{
	public const string AllGroupsOption = "全部分组";

	public const string SortManualOption = "手动";

	public const string SortByNameOption = "字母";

	public const string SortByRunningOption = "运行";

	public const string SortByGroupOption = "分组";

	public const string SortAscendingOption = "升序";

	public const string SortDescendingOption = "降序";

	private readonly AppCatalogService _catalog;

	private readonly ProcessControlService _processService;

	private readonly LaunchService _launchService;

	private readonly IconService _iconService;

	private readonly AppSettings _settings;

	private readonly ObservableCollection<AppItemViewModel> _displayFilteredApps = new ObservableCollection<AppItemViewModel>();

	private readonly Dictionary<Guid, AppItemViewModel> _appLookup = new Dictionary<Guid, AppItemViewModel>();

	private string _searchQuery = string.Empty;

	private string _selectedGroup = AllGroupsOption;

	private string _selectedSort = SortManualOption;

	private string _selectedSortDirection = SortAscendingOption;

	private bool _isRunningFilterActive;

	private bool _isDarkMode;

	private bool _isCompactMode;

	private bool _isGridView;

	private bool _isInspectorOpen;

	private int _deferRefreshDepth;

	private bool _refreshPending;

	private bool _appsCollectionChangedDuringDefer;

	private AppItemViewModel? _selectedApp;

	private bool _selectionDismissed;

	public ObservableCollection<AppItemViewModel> Apps { get; } = new ObservableCollection<AppItemViewModel>();

	public ObservableCollection<string> GroupOptions { get; } = new ObservableCollection<string>();

	public ObservableCollection<string> SortOptions { get; } = new ObservableCollection<string>();

	public ObservableCollection<string> SortDirectionOptions { get; } = new ObservableCollection<string>();

	public IAsyncRelayCommand RefreshCommand { get; }

	public IRelayCommand ToggleThemeCommand { get; }

	public IRelayCommand ClearSelectionCommand { get; }

	public IRelayCommand ClearSearchCommand { get; }

	public IRelayCommand ClearFiltersCommand { get; }

	public IRelayCommand ToggleRunningFilterCommand { get; }

	public IRelayCommand ShowAllAppsCommand { get; }

	public IRelayCommand ToggleSortDirectionCommand { get; }

	public IRelayCommand ToggleInspectorCommand { get; }

	public IRelayCommand CloseInspectorCommand { get; }

	public IRelayCommand ToggleCompactModeCommand { get; }

	public IRelayCommand ToggleViewModeCommand { get; }

	public string SearchQuery
	{
		get
		{
			return _searchQuery;
		}
		set
		{
			if (SetProperty(ref _searchQuery, value ?? string.Empty))
			{
				RequestDisplayRefresh();
				OnPropertyChanged("CanReorder");
				OnPropertyChanged("HasSearchQuery");
				OnPropertyChanged("HasActiveFilter");
			}
		}
	}

	public bool HasSearchQuery => !string.IsNullOrWhiteSpace(_searchQuery);

	public bool IsRunningFilterActive
	{
		get
		{
			return _isRunningFilterActive;
		}
		set
		{
			if (SetProperty(ref _isRunningFilterActive, value, "IsRunningFilterActive"))
			{
				RequestDisplayRefresh();
				OnPropertyChanged("CanReorder");
				OnPropertyChanged("HasActiveFilter");
			}
		}
	}

	public bool HasActiveFilter => HasSearchQuery || !IsAllGroupsSelected || IsRunningFilterActive;

	public bool IsDarkMode
	{
		get
		{
			return _isDarkMode;
		}
		set
		{
			if (SetProperty(ref _isDarkMode, value))
			{
				_settings.IsDarkMode = value;
				ThemeService.ApplyTheme(value);
				BackgroundEffectService.Apply(Application.Current?.MainWindow, _settings);
				if (Application.Current?.MainWindow is MainWindow mainWin)
				{
					mainWin.SyncThemeButtonVisual();
				}
				AppServices.Storage.ScheduleSave(AppServices.Config);
				OnPropertyChanged("ThemeToggleText");
			}
		}
	}

	public bool IsCompactMode
	{
		get
		{
			return _isCompactMode;
		}
		set
		{
			if (SetProperty(ref _isCompactMode, value, "IsCompactMode"))
			{
				_settings.IsCompactList = value;
				AppServices.Storage.ScheduleSave(AppServices.Config);
				OnPropertyChanged("CompactModeTooltip");
			}
		}
	}

	public string CompactModeTooltip => IsCompactMode ? "当前：紧凑列表（点击切换舒适模式）" : "当前：舒适列表（点击切换紧凑模式）";

	public bool IsGridView
	{
		get
		{
			return _isGridView;
		}
		set
		{
			if (SetProperty(ref _isGridView, value, "IsGridView"))
			{
				_settings.IsGridView = value;
				AppServices.Storage.ScheduleSave(AppServices.Config);
				OnPropertyChanged("IsListView");
				OnPropertyChanged("ViewModeGlyph");
				OnPropertyChanged("ViewModeTooltip");
			}
		}
	}

	public bool IsListView => !IsGridView;

	public string ViewModeGlyph => IsGridView ? "\uE8FD" : "\uF0E2";

	public string ViewModeTooltip => IsGridView ? "切换为列表视图" : "切换为磁贴网格视图";

	public string SelectedGroup
	{
		get
		{
			return _selectedGroup;
		}
		set
		{
			string normalized = string.IsNullOrWhiteSpace(value) ? AllGroupsOption : value;
			if (SetProperty(ref _selectedGroup, normalized, "SelectedGroup"))
			{
				ApplyActiveGroupScopeToApps();
				RequestDisplayRefresh();
				OnPropertyChanged("CanReorder");
				OnPropertyChanged("HasActiveFilter");
			}
		}
	}

	public string SelectedSort
	{
		get
		{
			return _selectedSort;
		}
		set
		{
			string normalized = NormalizeSortOption(value);
			if (SetProperty(ref _selectedSort, normalized, "SelectedSort"))
			{
				ApplyPinAvailabilityToApps();
				RequestDisplayRefresh();
				OnPropertyChanged("CanReorder");
				OnPropertyChanged("IsSortDirectionVisible");
				OnPropertyChanged("IsPinFeatureEnabled");
			}
		}
	}

	public string SelectedSortDirection
	{
		get
		{
			return _selectedSortDirection;
		}
		set
		{
			string normalized = NormalizeSortDirection(value);
			if (SetProperty(ref _selectedSortDirection, normalized, "SelectedSortDirection"))
			{
				RequestDisplayRefresh();
				OnPropertyChanged("IsSortDescending");
				OnPropertyChanged("SortDirectionGlyph");
				OnPropertyChanged("SortDirectionTooltip");
			}
		}
	}

	public bool IsSortDescending => string.Equals(SelectedSortDirection, SortDescendingOption, StringComparison.Ordinal);

	public string SortDirectionGlyph => IsSortDescending ? "\uE74B" : "\uE74A";

	public string SortDirectionTooltip => IsSortDescending ? "当前：降序（点击切换升序）" : "当前：升序（点击切换降序）";

	public bool CanReorder => string.IsNullOrWhiteSpace(SearchQuery) && IsAllGroupsSelected && IsManualSortSelected && !IsRunningFilterActive;

	public bool IsSortDirectionVisible => !IsManualSortSelected;

	public bool IsPinFeatureEnabled => IsManualSortSelected;

	public IEnumerable<AppItemViewModel> CurrentView => _displayFilteredApps;

	public bool IsEmpty => !_displayFilteredApps.Any();

	public bool HasNoApps => Apps.Count == 0;

	public bool IsFilterEmpty => Apps.Count > 0 && !_displayFilteredApps.Any();

	public int TotalCount => Apps.Count;

	public int RunningCount => Apps.Count((AppItemViewModel app) => app.IsRunning);

	public int PinnedCount => _displayFilteredApps.Count((AppItemViewModel app) => app.IsPinned);

	public string RefreshIntervalText
	{
		get
		{
			double seconds = Math.Clamp(_settings.RefreshIntervalMs, 1000, 60000) / 1000.0;
			return seconds % 1 == 0 ? $"{seconds:0} s" : $"{seconds:0.#} s";
		}
	}

	public string VisibleCountText
	{
		get
		{
			if (HasActiveFilter)
			{
				return $"显示 {_displayFilteredApps.Count} / {Apps.Count} 个应用";
			}
			if (CanReorder)
			{
				return $"共 {_displayFilteredApps.Count} 个应用 · 可拖拽调整顺序";
			}
			return $"共 {_displayFilteredApps.Count} 个应用";
		}
	}

	public AppItemViewModel? SelectedApp
	{
		get
		{
			return _selectedApp;
		}
		set
		{
			if (SetProperty(ref _selectedApp, value, "SelectedApp"))
			{
				if (value != null)
				{
					_selectionDismissed = false;
				}
				else
				{
					IsInspectorOpen = false;
				}
				OnPropertyChanged("HasSelectedApp");
				OnPropertyChanged("ShowInspector");
			}
		}
	}

	public bool HasSelectedApp => SelectedApp != null;

	public bool IsInspectorOpen
	{
		get
		{
			return _isInspectorOpen;
		}
		set
		{
			if (SetProperty(ref _isInspectorOpen, value, "IsInspectorOpen"))
			{
				OnPropertyChanged("ShowInspector");
				OnPropertyChanged("InspectorToggleText");
			}
		}
	}

	public bool ShowInspector => IsInspectorOpen && SelectedApp != null;

	public string InspectorToggleText => ShowInspector ? "收起详情" : "详情面板";

	public string ThemeToggleText => IsDarkMode ? "浅色模式" : "深色模式";

	public event Action<AppItemViewModel>? EditRequested;

	public event Action<AppItemViewModel>? RemoveRequested;

	public OverviewViewModel()
	{
		_catalog = AppServices.Catalog;
		_processService = AppServices.ProcessService;
		_launchService = AppServices.LaunchService;
		_iconService = AppServices.IconService;
		_settings = AppServices.Config.Settings;
		_isDarkMode = _settings.IsDarkMode;
		_isCompactMode = _settings.IsCompactList;
		_isGridView = _settings.IsGridView;
		SortOptions.Add(SortManualOption);
		SortOptions.Add(SortByNameOption);
		SortOptions.Add(SortByRunningOption);
		SortOptions.Add(SortByGroupOption);
		SortDirectionOptions.Add(SortAscendingOption);
		SortDirectionOptions.Add(SortDescendingOption);
		ToggleThemeCommand = new RelayCommand(delegate
		{
			IsDarkMode = !IsDarkMode;
		});
		ClearSelectionCommand = new RelayCommand(delegate
		{
			_selectionDismissed = true;
			IsInspectorOpen = false;
			SelectedApp = null;
		});
		ClearSearchCommand = new RelayCommand(delegate
		{
			SearchQuery = string.Empty;
		});
		ClearFiltersCommand = new RelayCommand(delegate
		{
			SearchQuery = string.Empty;
			SelectedGroup = AllGroupsOption;
			IsRunningFilterActive = false;
		});
		ToggleRunningFilterCommand = new RelayCommand(delegate
		{
			IsRunningFilterActive = !IsRunningFilterActive;
		});
		ShowAllAppsCommand = new RelayCommand(delegate
		{
			IsRunningFilterActive = false;
			SelectedGroup = AllGroupsOption;
		});
		ToggleSortDirectionCommand = new RelayCommand(delegate
		{
			SelectedSortDirection = IsSortDescending ? SortAscendingOption : SortDescendingOption;
		});
		ToggleInspectorCommand = new RelayCommand(delegate
		{
			if (!IsInspectorOpen && SelectedApp == null)
			{
				SelectedApp = _displayFilteredApps.FirstOrDefault();
			}
			if (SelectedApp != null)
			{
				IsInspectorOpen = !IsInspectorOpen;
			}
		});
		CloseInspectorCommand = new RelayCommand(delegate
		{
			IsInspectorOpen = false;
		});
		ToggleCompactModeCommand = new RelayCommand(delegate
		{
			IsCompactMode = !IsCompactMode;
		});
		ToggleViewModeCommand = new RelayCommand(delegate
		{
			IsGridView = !IsGridView;
		});
		RefreshCommand = new AsyncRelayCommand(async delegate
		{
			AppServices.StatusScheduler.RequestImmediateRefresh();
			await _processService.RefreshAllStatusAsync();
		});
		_processService.ProcessStatusesChanged += OnProcessStatusesChanged;
		Apps.CollectionChanged += OnAppsCollectionChanged;
		RefreshGroupOptions();
		LoadApps();
	}

	public void SyncPreferencesFromSettings()
	{
		IsDarkMode = _settings.IsDarkMode;
		IsCompactMode = _settings.IsCompactList;
		IsGridView = _settings.IsGridView;
	}

	public void LoadApps()
	{
		foreach (AppItemViewModel app in Apps)
		{
			app.PropertyChanged -= OnItemPropertyChanged;
		}
		using (DeferRefresh())
		{
			Apps.Clear();
			foreach (ApplicationItem app in _catalog.GetAllApps())
			{
				AppItemViewModel vm = CreateViewModel(app);
				Apps.Add(vm);
			}
		}
	}

	public void AddApp(ApplicationItem item)
	{
		AddApps(new[] { item });
	}

	public void AddApps(IEnumerable<ApplicationItem> items)
	{
		List<AppItemViewModel> addedApps = items.Select(CreateViewModel).ToList();
		if (addedApps.Count == 0)
		{
			return;
		}
		using (DeferRefresh())
		{
			foreach (AppItemViewModel app in addedApps)
			{
				Apps.Add(app);
			}
		}
		SelectedApp = addedApps.LastOrDefault();
	}

	public void RemoveApp(Guid id)
	{
		if (_appLookup.TryGetValue(id, out AppItemViewModel? app))
		{
			using (DeferRefresh())
			{
				app.PropertyChanged -= OnItemPropertyChanged;
				Apps.Remove(app);
			}
		}
	}

	public void RestoreApp(ApplicationItem item, int index)
	{
		if (_appLookup.ContainsKey(item.Id))
		{
			return;
		}
		AppItemViewModel vm = CreateViewModel(item);
		using (DeferRefresh())
		{
			int insertIndex = Math.Clamp(index, 0, Apps.Count);
			Apps.Insert(insertIndex, vm);
		}
		SelectedApp = vm;
	}

	public void RefreshSummary()
	{
		foreach (AppItemViewModel app in Apps)
		{
			app.RefreshRelativeTime();
		}
		NotifySummaryChanged();
	}

	public bool TryReorderApp(AppItemViewModel app, AppItemViewModel? target, bool insertAfter)
	{
		if (!CanReorder)
		{
			return false;
		}
		if (target == app)
		{
			return true;
		}
		int sourceIndex = Apps.IndexOf(app);
		if (sourceIndex < 0)
		{
			return target == null;
		}
		int targetIndex = Apps.Count - 1;
		if (target != null)
		{
			targetIndex = Apps.IndexOf(target);
			if (targetIndex < 0)
			{
				return false;
			}
			if (insertAfter)
			{
				targetIndex++;
			}
			if (targetIndex > sourceIndex)
			{
				targetIndex--;
			}
			targetIndex = Math.Clamp(targetIndex, 0, Apps.Count - 1);
		}
		if (sourceIndex != targetIndex)
		{
			Apps.Move(sourceIndex, targetIndex);
			_catalog.ReorderApps(Apps.Select(item => item.Id).ToList());
			RequestDisplayRefresh();
		}
		return true;
	}

	private void OnAppsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (_deferRefreshDepth > 0)
		{
			_appsCollectionChangedDuringDefer = true;
			_refreshPending = true;
			return;
		}
		RefreshAppsCollectionState();
	}

	private void RefreshAppsCollectionState()
	{
		RebuildAppLookup();
		RefreshGroupOptions();
		ApplyActiveGroupScopeToApps();
		ApplyPinAvailabilityToApps();
		RequestDisplayRefresh();
	}

	private AppItemViewModel CreateViewModel(ApplicationItem item)
	{
		AppItemViewModel vm = new AppItemViewModel(
			item,
			_launchService,
			_processService,
			_iconService,
			OnEditRequested,
			OnRemoveRequested,
			OnTogglePinRequested,
			OnInspectRequested);
		vm.SetActiveGroupScope(GetActiveGroupScope());
		vm.CanTogglePin = IsPinFeatureEnabled;
		vm.PropertyChanged += OnItemPropertyChanged;
		return vm;
	}

	private void OnInspectRequested(AppItemViewModel vm)
	{
		SelectedApp = vm;
		IsInspectorOpen = true;
	}

	private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "IsRunning" || e.PropertyName == "IsPinned" || e.PropertyName == "DisplayName" || e.PropertyName == "SortIndex" || e.PropertyName == "GroupName")
		{
			if (e.PropertyName == "GroupName")
			{
				RefreshGroupOptions();
			}
			RequestDisplayRefresh();
		}
	}

	private void RequestDisplayRefresh()
	{
		if (_deferRefreshDepth > 0)
		{
			_refreshPending = true;
			return;
		}
		RefreshDisplay();
	}

	private void RefreshDisplay()
	{
		IEnumerable<AppItemViewModel> filterBase = IsAllGroupsSelected
			? Apps
			: Apps.Where((AppItemViewModel app) => string.Equals(NormalizeGroupName(app.GroupName), NormalizeGroupName(SelectedGroup), StringComparison.OrdinalIgnoreCase));
		if (IsRunningFilterActive)
		{
			filterBase = filterBase.Where((AppItemViewModel app) => app.IsRunning);
		}
		IEnumerable<AppItemViewModel> searched = string.IsNullOrWhiteSpace(SearchQuery) ? filterBase : filterBase.Where(MatchesSearch);
		List<AppItemViewModel> ordered = OrderApps(searched).ToList();
		bool displayChanged = ordered.Count != _displayFilteredApps.Count;
		for (int i = 0; !displayChanged && i < ordered.Count; i++)
		{
			displayChanged = !ReferenceEquals(ordered[i], _displayFilteredApps[i]);
		}
		if (displayChanged)
		{
			UpdateDisplayCollection(ordered);
			OnPropertyChanged("CurrentView");
		}
		OnPropertyChanged("IsEmpty");
		OnPropertyChanged("HasNoApps");
		OnPropertyChanged("IsFilterEmpty");
		OnPropertyChanged("VisibleCountText");
		NotifySummaryChanged();
		SyncSelectionAfterRefresh();
	}

	private void SyncSelectionAfterRefresh()
	{
		if (SelectedApp != null && !_displayFilteredApps.Contains(SelectedApp))
		{
			SelectedApp = null;
		}
		if (SelectedApp == null && !_selectionDismissed)
		{
			SelectedApp = _displayFilteredApps.FirstOrDefault();
		}
	}

	private IEnumerable<AppItemViewModel> OrderApps(IEnumerable<AppItemViewModel> source)
	{
		if (IsManualSortSelected)
		{
			IEnumerable<AppItemViewModel> pinned = source.Where(app => app.IsPinned);
			return pinned.Concat(source.Where(app => !app.IsPinned));
		}
		bool descending = string.Equals(SelectedSortDirection, SortDescendingOption, StringComparison.Ordinal);
		if (string.Equals(SelectedSort, SortByNameOption, StringComparison.Ordinal))
		{
			if (descending)
			{
				return source.OrderByDescending(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
					.ThenByDescending(app => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase);
			}
			return source.OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
				.ThenBy(app => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase);
		}
		if (string.Equals(SelectedSort, SortByRunningOption, StringComparison.Ordinal))
		{
			if (descending)
			{
				return source.OrderBy(app => app.IsRunning)
					.ThenBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase);
			}
			return source.OrderByDescending(app => app.IsRunning)
				.ThenBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase);
		}
		if (string.Equals(SelectedSort, SortByGroupOption, StringComparison.Ordinal))
		{
			if (descending)
			{
				return source.OrderByDescending(app => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase)
					.ThenByDescending(app => app.DisplayName, StringComparer.OrdinalIgnoreCase);
			}
			return source.OrderBy(app => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase)
				.ThenBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase);
		}
		return source;
	}

	private void UpdateDisplayCollection(IReadOnlyList<AppItemViewModel> ordered)
	{
		Dictionary<AppItemViewModel, int> existingPositions = new Dictionary<AppItemViewModel, int>();
		for (int i = 0; i < _displayFilteredApps.Count; i++)
		{
			existingPositions.Add(_displayFilteredApps[i], i);
		}
		if (HasDisplayOrderChanged(ordered, existingPositions))
		{
			_displayFilteredApps.Clear();
			foreach (AppItemViewModel app in ordered)
			{
				_displayFilteredApps.Add(app);
			}
			return;
		}
		HashSet<AppItemViewModel> retainedApps = new HashSet<AppItemViewModel>(ordered);
		for (int i = _displayFilteredApps.Count - 1; i >= 0; i--)
		{
			if (!retainedApps.Contains(_displayFilteredApps[i]))
			{
				_displayFilteredApps.RemoveAt(i);
			}
		}
		for (int i = 0; i < ordered.Count; i++)
		{
			if (!existingPositions.ContainsKey(ordered[i]))
			{
				_displayFilteredApps.Insert(i, ordered[i]);
			}
		}
	}

	private static bool HasDisplayOrderChanged(IReadOnlyList<AppItemViewModel> ordered, IReadOnlyDictionary<AppItemViewModel, int> existingPositions)
	{
		int previousPosition = -1;
		foreach (AppItemViewModel app in ordered)
		{
			if (!existingPositions.TryGetValue(app, out int position))
			{
				continue;
			}
			if (position < previousPosition)
			{
				return true;
			}
			previousPosition = position;
		}
		return false;
	}

	private void OnProcessStatusesChanged(object? sender, ProcessStatusesChangedEventArgs e)
	{
		using (DeferRefresh())
		{
			foreach (KeyValuePair<Guid, ProcessStatus> pair in e.Statuses)
			{
				if (_appLookup.TryGetValue(pair.Key, out AppItemViewModel? vm))
				{
					vm.UpdateStatus(pair.Value);
				}
			}
		}
	}

	private IDisposable DeferRefresh()
	{
		_deferRefreshDepth++;
		return new RefreshScope(this);
	}

	private void EndDeferredRefresh()
	{
		try
		{
			if (_deferRefreshDepth == 1 && _appsCollectionChangedDuringDefer)
			{
				_appsCollectionChangedDuringDefer = false;
				RefreshAppsCollectionState();
			}
		}
		finally
		{
			_deferRefreshDepth--;
		}
		if (_deferRefreshDepth == 0 && _refreshPending)
		{
			_refreshPending = false;
			RefreshDisplay();
		}
	}

	private void RebuildAppLookup()
	{
		_appLookup.Clear();
		foreach (AppItemViewModel app in Apps)
		{
			_appLookup[app.Id] = app;
		}
	}

	private void NotifySummaryChanged()
	{
		OnPropertyChanged("TotalCount");
		OnPropertyChanged("RunningCount");
		OnPropertyChanged("PinnedCount");
		OnPropertyChanged("RefreshIntervalText");
	}

	private void RefreshGroupOptions()
	{
		string previous = SelectedGroup;
		GroupOptions.Clear();
		GroupOptions.Add(AllGroupsOption);
		foreach (string groupName in _catalog.GetGroupNames())
		{
			GroupOptions.Add(groupName);
		}
		if (GroupOptions.Contains(previous))
		{
			SelectedGroup = previous;
		}
		else
		{
			SelectedGroup = AllGroupsOption;
		}
	}

	private bool MatchesSearch(AppItemViewModel app)
	{
		string query = SearchQuery.Trim();
		return app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || app.GroupDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
	}

	private void OnTogglePinRequested(AppItemViewModel vm)
	{
		if (!IsPinFeatureEnabled)
		{
			return;
		}
		string scope = GetActiveGroupScope();
		bool isPinned = !vm.IsPinned;
		_catalog.SetPinned(vm.Id, scope, isPinned);
		vm.IsPinned = isPinned;
	}

	private void OnEditRequested(AppItemViewModel vm)
	{
		this.EditRequested?.Invoke(vm);
	}

	private void OnRemoveRequested(AppItemViewModel vm)
	{
		this.RemoveRequested?.Invoke(vm);
	}

	private void ApplyActiveGroupScopeToApps()
	{
		string scope = GetActiveGroupScope();
		foreach (AppItemViewModel app in Apps)
		{
			app.SetActiveGroupScope(scope);
		}
	}

	private void ApplyPinAvailabilityToApps()
	{
		bool enabled = IsPinFeatureEnabled;
		foreach (AppItemViewModel app in Apps)
		{
			app.CanTogglePin = enabled;
		}
	}

	private string GetActiveGroupScope()
	{
		if (IsAllGroupsSelected)
		{
			return AppGroupScope.AllGroups;
		}
		return NormalizeGroupName(SelectedGroup);
	}

	private bool IsAllGroupsSelected => string.Equals(SelectedGroup, AllGroupsOption, StringComparison.Ordinal);

	private bool IsManualSortSelected => string.Equals(SelectedSort, SortManualOption, StringComparison.Ordinal);

	private static string NormalizeGroupName(string? groupName)
	{
		return string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName.Trim();
	}

	private static string NormalizeSortOption(string? sortOption)
	{
		if (string.Equals(sortOption, SortByNameOption, StringComparison.Ordinal) || string.Equals(sortOption, SortByRunningOption, StringComparison.Ordinal) || string.Equals(sortOption, SortByGroupOption, StringComparison.Ordinal))
		{
			return sortOption!;
		}
		return SortManualOption;
	}

	private static string NormalizeSortDirection(string? sortDirection)
	{
		return string.Equals(sortDirection, SortDescendingOption, StringComparison.Ordinal) ? SortDescendingOption : SortAscendingOption;
	}

	private sealed class RefreshScope : IDisposable
	{
		private OverviewViewModel? _owner;

		public RefreshScope(OverviewViewModel owner)
		{
			_owner = owner;
		}

		public void Dispose()
		{
			if (_owner != null)
			{
				_owner.EndDeferredRefresh();
				_owner = null;
			}
		}
	}
}
