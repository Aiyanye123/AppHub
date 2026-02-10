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
	private const string AllGroupsOption = "\u5168\u90e8\u5206\u7ec4";

	private const string SortManualOption = "\u624b\u52a8";

	private const string SortByNameOption = "\u5b57\u6bcd";

	private const string SortByRunningOption = "\u8fd0\u884c";

	private const string SortByGroupOption = "\u5206\u7ec4";

	private const string SortAscendingOption = "\u5347\u5e8f";

	private const string SortDescendingOption = "\u964d\u5e8f";

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

	private bool _isDarkMode;

	private int _deferRefreshDepth;

	private bool _refreshPending;

	public ObservableCollection<AppItemViewModel> Apps { get; } = new ObservableCollection<AppItemViewModel>();

	public ObservableCollection<string> GroupOptions { get; } = new ObservableCollection<string>();

	public ObservableCollection<string> SortOptions { get; } = new ObservableCollection<string>();

	public ObservableCollection<string> SortDirectionOptions { get; } = new ObservableCollection<string>();

	public IAsyncRelayCommand RefreshCommand { get; }

	public IRelayCommand ToggleThemeCommand { get; }

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
			}
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
			if (SetProperty(ref _isDarkMode, value))
			{
				_settings.IsDarkMode = value;
				ThemeService.ApplyTheme(value);
				BackgroundEffectService.Apply(Application.Current?.MainWindow, _settings);
				AppServices.Storage.ScheduleSave(AppServices.Config);
				OnPropertyChanged("ThemeToggleText");
			}
		}
	}

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
				RequestDisplayRefresh();
				OnPropertyChanged("CanReorder");
				OnPropertyChanged("IsSortDirectionVisible");
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
			}
		}
	}

	public bool CanReorder => string.IsNullOrWhiteSpace(SearchQuery) && IsAllGroupsSelected && IsManualSortSelected;

	public bool IsSortDirectionVisible => !IsManualSortSelected;

	public IEnumerable<AppItemViewModel> CurrentView => _displayFilteredApps;

	public bool IsEmpty => !_displayFilteredApps.Any();

	public string ThemeToggleText => IsDarkMode ? "\u6d45\u8272\u6a21\u5f0f" : "\u6df1\u8272\u6a21\u5f0f";

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

	public void LoadApps()
	{
		foreach (AppItemViewModel app in Apps)
		{
			app.PropertyChanged -= OnItemPropertyChanged;
		}
		using (DeferRefresh())
		{
			Apps.Clear();
			_appLookup.Clear();
			foreach (ApplicationItem app in _catalog.GetAllApps())
			{
				AppItemViewModel vm = CreateViewModel(app);
				Apps.Add(vm);
				_appLookup[vm.Id] = vm;
			}
			ApplyActiveGroupScopeToApps();
		}
	}

	public void AddApp(ApplicationItem item)
	{
		using (DeferRefresh())
		{
			AppItemViewModel vm = CreateViewModel(item);
			vm.SetActiveGroupScope(GetActiveGroupScope());
			Apps.Add(vm);
			_appLookup[vm.Id] = vm;
		}
	}

	public void CommitReorder()
	{
		if (CanReorder)
		{
			_catalog.ReorderApps(Apps.Select(app => app.Id).ToList());
			RequestDisplayRefresh();
		}
	}

	private void OnAppsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		RebuildAppLookup();
		RefreshGroupOptions();
		ApplyActiveGroupScopeToApps();
		RequestDisplayRefresh();
	}

	private AppItemViewModel CreateViewModel(ApplicationItem item)
	{
		AppItemViewModel vm = new AppItemViewModel(item, _launchService, _processService, _iconService, OnEditRequested, OnRemoveRequested, OnTogglePinRequested);
		vm.SetActiveGroupScope(GetActiveGroupScope());
		vm.PropertyChanged += OnItemPropertyChanged;
		return vm;
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
		IEnumerable<AppItemViewModel> filterBase = IsAllGroupsSelected ? Apps : Apps.Where((AppItemViewModel app) => string.Equals(NormalizeGroupName(app.GroupName), NormalizeGroupName(SelectedGroup), StringComparison.OrdinalIgnoreCase));
		IEnumerable<AppItemViewModel> searched = string.IsNullOrWhiteSpace(SearchQuery) ? filterBase : filterBase.Where(MatchesSearch);
		IEnumerable<AppItemViewModel> ordered = OrderApps(searched);
		RebuildCollection(_displayFilteredApps, ordered);
		OnPropertyChanged("CurrentView");
		OnPropertyChanged("IsEmpty");
	}

	private IEnumerable<AppItemViewModel> OrderApps(IEnumerable<AppItemViewModel> source)
	{
		if (string.Equals(SelectedSort, SortByGroupOption, StringComparison.Ordinal))
		{
			return OrderWithoutPinPriority(source);
		}
		IEnumerable<AppItemViewModel> pinned = source.Where((AppItemViewModel app) => app.IsPinned);
		IEnumerable<AppItemViewModel> normal = source.Where((AppItemViewModel app) => !app.IsPinned);
		IEnumerable<AppItemViewModel> orderedPinned = OrderWithoutPinPriority(pinned);
		IEnumerable<AppItemViewModel> orderedNormal = OrderWithoutPinPriority(normal);
		return orderedPinned.Concat(orderedNormal);
	}

	private IEnumerable<AppItemViewModel> OrderWithoutPinPriority(IEnumerable<AppItemViewModel> source)
	{
		if (IsManualSortSelected)
		{
			return source;
		}
		bool desc = string.Equals(SelectedSortDirection, SortDescendingOption, StringComparison.Ordinal);
		if (string.Equals(SelectedSort, SortByNameOption, StringComparison.Ordinal))
		{
			return desc ? source.OrderByDescending((AppItemViewModel app) => app.DisplayName, StringComparer.OrdinalIgnoreCase).ThenByDescending((AppItemViewModel app) => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase) : source.OrderBy((AppItemViewModel app) => app.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy((AppItemViewModel app) => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase);
		}
		if (string.Equals(SelectedSort, SortByRunningOption, StringComparison.Ordinal))
		{
			bool runningFirst = !desc;
			return runningFirst ? source.OrderByDescending((AppItemViewModel app) => app.IsRunning).ThenBy((AppItemViewModel app) => app.DisplayName, StringComparer.OrdinalIgnoreCase) : source.OrderBy((AppItemViewModel app) => app.IsRunning).ThenBy((AppItemViewModel app) => app.DisplayName, StringComparer.OrdinalIgnoreCase);
		}
		if (string.Equals(SelectedSort, SortByGroupOption, StringComparison.Ordinal))
		{
			return desc ? source.OrderByDescending((AppItemViewModel app) => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase).ThenByDescending((AppItemViewModel app) => app.DisplayName, StringComparer.OrdinalIgnoreCase) : source.OrderBy((AppItemViewModel app) => NormalizeGroupName(app.GroupName), StringComparer.OrdinalIgnoreCase).ThenBy((AppItemViewModel app) => app.DisplayName, StringComparer.OrdinalIgnoreCase);
		}
		return source;
	}

	private static void RebuildCollection(ObservableCollection<AppItemViewModel> target, IEnumerable<AppItemViewModel> source)
	{
		target.Clear();
		foreach (AppItemViewModel item in source)
		{
			target.Add(item);
		}
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
		_deferRefreshDepth--;
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
		string scope = GetActiveGroupScope();
		bool isPinned = !vm.IsPinned;
		_catalog.SetPinned(vm.Id, scope, isPinned);
		vm.SetPinnedForActiveGroup(isPinned);
		RequestDisplayRefresh();
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
