using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using AppHub.Models;
using AppHub.Services;
using AppHub.Views.Dialogs;
using DrawingColor = System.Drawing.Color;
using DrawingFontFamily = System.Drawing.FontFamily;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingPen = System.Drawing.Pen;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using MediaColor = System.Windows.Media.Color;

namespace AppHub.Infrastructure;

public sealed class TrayIconService : IDisposable
{
	private const string UngroupedName = "\u672a\u5206\u7ec4";

	private const uint FileAttributeDirectory = 16u;

	private const uint ShgfiIcon = 0x100u;

	private const uint ShgfiLargeIcon = 0u;

	private const uint ShgfiSmallIcon = 1u;

	private const uint ShgfiUseFileAttributes = 0x10u;

	private const int DwmWindowCornerPreference = 33;

	private const int DwmBorderColor = 34;

	private const int DwmCornerRoundSmall = 3;

	private const int BaseIconSize = 18;

	private const int BaseItemCornerRadius = 6;

	private const int BaseItemHorizontalInset = 4;

	private const int BaseItemVerticalInset = 2;

	private const int MaxNotifyTextLength = 127;

	private static readonly Font MenuFont = new Font("Microsoft YaHei UI", 9.5f, DrawingFontStyle.Regular, GraphicsUnit.Point);

	private static readonly Font MenuBoldFont = new Font("Microsoft YaHei UI", 9.5f, DrawingFontStyle.Bold, GraphicsUnit.Point);

	private static readonly Font SectionHeaderFont = new Font("Microsoft YaHei UI", 8.5f, DrawingFontStyle.Regular, GraphicsUnit.Point);

	private static readonly string GlyphFontFamilyName = ResolveGlyphFontFamilyName();

	private readonly Dictionary<string, Image> _appIconCache = new Dictionary<string, Image>();
	private readonly HashSet<string> _pendingIconLoads = new HashSet<string>();
	private HashSet<string> _desiredIconSignatures = new HashSet<string>();
	private readonly CancellationTokenSource _iconLoadCancellation = new CancellationTokenSource();
	private readonly Dictionary<ToolStripMenuItem, ApplicationItem> _appMenuItems = new();
	private readonly Dictionary<ToolStripMenuItem, ApplicationGroupMenu> _groupMenuItems = new();
	private readonly Dictionary<ToolStripMenuItem, ApplicationGroupMenu> _groupLaunchItems = new();
	private readonly Dictionary<Guid, ToolStripMenuItem> _runningMenuItems = new();
	private readonly Dictionary<Guid, ToolStripMenuItem> _frequentMenuItems = new();
	private ToolStripMenuItem? _runningHeader;
	private ToolStripMenuItem? _closeAllItem;
	private ToolStripSeparator? _runningSeparator;
	private ToolStripMenuItem? _frequentHeader;
	private ToolStripSeparator? _frequentSeparator;
	private readonly Icon _baseTrayIcon;
	private Icon? _statusTrayIcon;
	private int _statusTrayIconSize;
	private bool _statusTrayIconRunning;
	private bool _batchOperationPending;

	private readonly List<Image> _menuScopedImages = new List<Image>();

	private readonly HashSet<Guid> _pendingAppOperations = new HashSet<Guid>();

	private IReadOnlyDictionary<Guid, ProcessStatus> _runningStatuses = new Dictionary<Guid, ProcessStatus>();

	private readonly Window _window;

	private readonly NotifyIcon _notifyIcon;

	private readonly ContextMenuStrip _menu;

	private Guid? _launchingAppId;

	private string? _launchingAppName;

	private int _launchingToken;

	private bool _isBackgroundMode;

	private bool _isWindowInForeground = true;

	private bool _disposed;

	public event EventHandler<bool>? BackgroundModeChanged;

	public TrayIconService(Window window)
	{
		_window = window;
		_menu = BuildMenu();
		_baseTrayIcon = GetAppIcon();
		_notifyIcon = new NotifyIcon
		{
			Text = "AppHub",
			Icon = _baseTrayIcon,
			Visible = false,
			ContextMenuStrip = _menu
		};
		_notifyIcon.MouseClick += OnNotifyIconMouseClick;
		AppServices.ProcessService.ProcessStatusesChanged += OnProcessStatusesChanged;
		AppServices.Catalog.Changed += OnCatalogChanged;
		AppServices.TraySettingsChanged += OnTraySettingsChanged;
		_window.StateChanged += OnWindowStateChanged;
		_window.Activated += OnWindowActivated;
		_window.Deactivated += OnWindowDeactivated;
		_window.Closed += delegate
		{
			Dispose();
		};
		UpdateNotifyIconText();
		QueueIconWarmup();
	}

	private ContextMenuStrip BuildMenu()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			ShowImageMargin = true,
			ShowCheckMargin = false,
			DropShadowEnabled = true,
			Padding = new Padding(4),
			Font = MenuFont
		};
		contextMenuStrip.Opening += OnMenuOpening;
		contextMenuStrip.Opened += OnMenuOpened;
		return contextMenuStrip;
	}

	private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
	{
		try
		{
			RefreshMenu(_menu);
			QueueIconWarmup();
			e.Cancel = _menu.Items.Count == 0;
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Tray menu refresh failed", ex);
		}
	}

	private void OnMenuOpened(object? sender, EventArgs e)
	{
		MenuPalette palette = MenuPalette.FromTheme(AppServices.Config.Settings.IsDarkMode);
		ApplyNativeDropDownWindowStyle(_menu, palette);
		AppServices.StatusScheduler.RequestImmediateRefresh();
	}

	private void OnProcessStatusesChanged(object? sender, ProcessStatusesChangedEventArgs e)
	{
		if (_disposed)
		{
			return;
		}
		bool refreshMenu = _menu.Visible && HaveRunningStatusesChanged(e.Statuses);
		_runningStatuses = e.Statuses;
		if (_launchingAppId.HasValue && IsAppRunning(_runningStatuses, _launchingAppId.Value))
		{
			_launchingAppId = null;
			_launchingAppName = null;
		}
		UpdateNotifyIconText();
		if (refreshMenu)
		{
			try
			{
				UpdateVisibleMenu();
			}
			catch (Exception ex)
			{
				AppServices.Logger.Error("Tray menu status refresh failed", ex);
			}
		}
	}

	private bool HaveRunningStatusesChanged(IReadOnlyDictionary<Guid, ProcessStatus> statuses)
	{
		if (statuses.Count != _runningStatuses.Count)
		{
			return true;
		}
		foreach (KeyValuePair<Guid, ProcessStatus> pair in statuses)
		{
			if (!_runningStatuses.TryGetValue(pair.Key, out ProcessStatus? previousStatus)
				|| pair.Value.IsRunning != previousStatus.IsRunning)
			{
				return true;
			}
		}
		return false;
	}

	private void OnCatalogChanged(object? sender, EventArgs e)
	{
		if (_disposed)
		{
			return;
		}
		QueueIconWarmup();
		if (_menu.Visible)
		{
			UpdateVisibleMenu();
		}
	}

	private void OnTraySettingsChanged(object? sender, EventArgs e)
	{
		if (!_disposed && _menu.Visible)
		{
			UpdateVisibleMenu();
		}
	}

	private void UpdateVisibleMenu()
	{
		if (_disposed || !_menu.Visible)
		{
			return;
		}
		MenuPalette palette = MenuPalette.FromTheme(AppServices.Config.Settings.IsDarkMode);
		float dpiScale = GetDpiScale(_menu);
		int iconSize = ScaleByDpi(BaseIconSize, dpiScale);
		int bottom = _menu.Bottom;
		IReadOnlyList<ApplicationItem> apps = AppServices.Catalog.GetAllApps();
		List<ApplicationItem> runningApps = OrderAppsByPinAndSortIndex(
			apps.Where(app => IsAppRunning(_runningStatuses, app.Id)), AppGroupScope.AllGroups);
		_menu.SuspendLayout();
		try
		{
			UpdateRunningSection(runningApps, palette, iconSize, dpiScale);
			UpdateFrequentSection(GetFrequentApps(), palette, iconSize, dpiScale);
			Dictionary<Guid, ApplicationItem> currentApps = apps.ToDictionary(app => app.Id);
			foreach (KeyValuePair<ToolStripMenuItem, ApplicationItem> pair in _appMenuItems.ToArray())
			{
				if (pair.Key.IsDisposed || !currentApps.TryGetValue(pair.Value.Id, out ApplicationItem? app))
				{
					continue;
				}
				ToolStripMenuItem item = pair.Key;
				_appMenuItems[item] = app;
				item.Text = ResolveTrayAppText(app);
				item.ToolTipText = app.TargetPath;
				bool running = IsAppRunning(_runningStatuses, app.Id);
				ReplaceAppMenuIcon(item, app, running, palette, iconSize, dpiScale);
				UpdateAppActions(item, app, running, palette, iconSize, dpiScale);
			}
			foreach (KeyValuePair<ToolStripMenuItem, ApplicationGroupMenu> pair in _groupMenuItems)
			{
				int runningCount = pair.Value.Apps.Count(app => IsAppRunning(_runningStatuses, app.Id));
				pair.Key.ShortcutKeyDisplayString = runningCount > 0 ? $"● {runningCount}" : string.Empty;
				pair.Key.Tag = runningCount > 0 ? TrayItemRole.GroupWithRunning : null;
			}
			foreach (KeyValuePair<ToolStripMenuItem, ApplicationGroupMenu> pair in _groupLaunchItems)
			{
				UpdateGroupLaunchText(pair.Key, pair.Value, _runningStatuses);
			}
			ApplyMenuAppearance(_menu, palette, dpiScale);
		}
		finally
		{
			_menu.ResumeLayout(performLayout: true);
		}
		_menu.Location = new DrawingPoint(_menu.Left, bottom - _menu.Height);
		_menu.Invalidate();
	}

	private void UpdateRunningSection(IReadOnlyList<ApplicationItem> apps, MenuPalette palette, int iconSize, float dpiScale)
	{
		HashSet<Guid> wantedIds = apps.Select(app => app.Id).ToHashSet();
		foreach (KeyValuePair<Guid, ToolStripMenuItem> pair in _runningMenuItems.ToArray())
		{
			if (!wantedIds.Contains(pair.Key) && !pair.Value.DropDown.Visible)
			{
				RemoveAppMenuItem(pair.Value);
				_runningMenuItems.Remove(pair.Key);
			}
		}
		if (apps.Count > 0 && _runningHeader == null)
		{
			_runningHeader = CreateSectionHeaderItem("运行中", dpiScale);
			_closeAllItem = CreateCloseAllMenuItem(dpiScale);
			_runningSeparator = new ToolStripSeparator();
			_menu.Items.Insert(0, _runningHeader);
			_menu.Items.Insert(1, _closeAllItem);
			_menu.Items.Insert(2, _runningSeparator);
		}
		foreach (ApplicationItem app in apps)
		{
			if (!_runningMenuItems.ContainsKey(app.Id))
			{
				ToolStripMenuItem item = CreateAppMenuItem(app, true, palette, iconSize, dpiScale);
				_menu.Items.Insert(_menu.Items.IndexOf(_closeAllItem!), item);
				_runningMenuItems.Add(app.Id, item);
			}
		}
		ReorderSectionItems(apps, _runningMenuItems, _runningHeader);
		if (_runningMenuItems.Count == 0 && _runningHeader != null)
		{
			_runningHeader.Dispose();
			_closeAllItem?.Dispose();
			_runningSeparator?.Dispose();
			_runningHeader = null;
			_closeAllItem = null;
			_runningSeparator = null;
		}
	}

	private void UpdateFrequentSection(IReadOnlyList<ApplicationItem> apps, MenuPalette palette, int iconSize, float dpiScale)
	{
		HashSet<Guid> wantedIds = apps.Select(app => app.Id).ToHashSet();
		foreach (KeyValuePair<Guid, ToolStripMenuItem> pair in _frequentMenuItems.ToArray())
		{
			if (!wantedIds.Contains(pair.Key) && !pair.Value.DropDown.Visible)
			{
				RemoveAppMenuItem(pair.Value);
				_frequentMenuItems.Remove(pair.Key);
			}
		}
		if (apps.Count > 0 && _frequentHeader == null)
		{
			int index = _runningSeparator == null ? 0 : _menu.Items.IndexOf(_runningSeparator) + 1;
			_frequentHeader = CreateSectionHeaderItem("常用", dpiScale);
			_frequentSeparator = new ToolStripSeparator();
			_menu.Items.Insert(index, _frequentHeader);
			_menu.Items.Insert(index + 1, _frequentSeparator);
		}
		foreach (ApplicationItem app in apps)
		{
			if (!_frequentMenuItems.ContainsKey(app.Id))
			{
				ToolStripMenuItem item = CreateAppMenuItem(app, false, palette, iconSize, dpiScale);
				_menu.Items.Insert(_menu.Items.IndexOf(_frequentSeparator!), item);
				_frequentMenuItems.Add(app.Id, item);
			}
		}
		ReorderSectionItems(apps, _frequentMenuItems, _frequentHeader);
		if (_frequentMenuItems.Count == 0 && _frequentHeader != null)
		{
			_frequentHeader.Dispose();
			_frequentSeparator?.Dispose();
			_frequentHeader = null;
			_frequentSeparator = null;
		}
	}

	private void ReorderSectionItems(IReadOnlyList<ApplicationItem> apps,
		Dictionary<Guid, ToolStripMenuItem> items, ToolStripMenuItem? header)
	{
		if (header == null || items.Values.Any(item => item.DropDown.Visible))
		{
			return;
		}
		int index = _menu.Items.IndexOf(header) + 1;
		foreach (ApplicationItem app in apps)
		{
			ToolStripMenuItem item = items[app.Id];
			if (_menu.Items.IndexOf(item) != index)
			{
				_menu.Items.Remove(item);
				_menu.Items.Insert(index, item);
			}
			index++;
		}
	}

	private void RemoveAppMenuItem(ToolStripMenuItem item)
	{
		_appMenuItems.Remove(item);
		if (item.Image != null)
		{
			_menuScopedImages.Remove(item.Image);
			item.Image.Dispose();
			item.Image = null;
		}
		item.Dispose();
	}

	private void ReplaceAppMenuIcon(ToolStripMenuItem item, ApplicationItem app, bool running,
		MenuPalette palette, int iconSize, float dpiScale)
	{
		Image? oldImage = item.Image;
		Image image = CreateAppStatusIcon(app, running, palette, iconSize, dpiScale);
		item.Image = image;
		_menuScopedImages.Add(image);
		if (oldImage != null)
		{
			_menuScopedImages.Remove(oldImage);
			oldImage.Dispose();
		}
	}

	private void RefreshMenu(ContextMenuStrip menu, bool keepBottomEdge = false)
	{
		bool keepLocation = menu.Visible;
		DrawingPoint currentLocation = menu.Location;
		int currentBottom = menu.Bottom;
		float dpiScale = GetDpiScale(menu);
		MenuPalette palette = MenuPalette.FromTheme(AppServices.Config.Settings.IsDarkMode);

		RebuildMenuItems(menu, palette, dpiScale);
		ApplyMenuAppearance(menu, palette, dpiScale);

		if (keepLocation)
		{
			int y = keepBottomEdge ? currentBottom - menu.Height : currentLocation.Y;
			menu.Location = new DrawingPoint(currentLocation.X, y);
		}
	}

	private void RebuildMenuItems(ContextMenuStrip menu, MenuPalette palette, float dpiScale)
	{
		int iconSize = ScaleByDpi(BaseIconSize, dpiScale);
		menu.SuspendLayout();
		try
		{
			foreach (ToolStripItem oldItem in menu.Items.Cast<ToolStripItem>().ToArray())
			{
				oldItem.Dispose();
			}
			menu.Items.Clear();
			_appMenuItems.Clear();
			_groupMenuItems.Clear();
			_groupLaunchItems.Clear();
			_runningMenuItems.Clear();
			_frequentMenuItems.Clear();
			_runningHeader = null;
			_runningSeparator = null;
			_closeAllItem = null;
			_frequentHeader = null;
			_frequentSeparator = null;
			DisposeMenuScopedImages();

			IReadOnlyList<ApplicationItem> apps = AppServices.Catalog.GetAllApps();
			IReadOnlyDictionary<Guid, ProcessStatus> runningStatuses = _runningStatuses;

			List<ApplicationItem> runningApps = OrderAppsByPinAndSortIndex(
				apps.Where(app => IsAppRunning(runningStatuses, app.Id)),
				AppGroupScope.AllGroups);

			if (runningApps.Count > 0)
			{
				_runningHeader = CreateSectionHeaderItem("运行中", dpiScale);
				menu.Items.Add(_runningHeader);
				foreach (ApplicationItem runningApp in runningApps)
				{
					ToolStripMenuItem runningItem = CreateAppMenuItem(runningApp, isRunning: true, palette, iconSize, dpiScale);
					_runningMenuItems.Add(runningApp.Id, runningItem);
					menu.Items.Add(runningItem);
				}
				_closeAllItem = CreateCloseAllMenuItem(dpiScale);
				menu.Items.Add(_closeAllItem);
				_runningSeparator = new ToolStripSeparator();
				menu.Items.Add(_runningSeparator);
			}

			int frequentLimit = Math.Clamp(AppServices.Config.Settings.TrayFrequentCount, 0, 15);
			if (frequentLimit > 0)
			{
				IReadOnlyList<ApplicationItem> frequentApps = GetFrequentApps();
				if (frequentApps.Count > 0)
				{
					_frequentHeader = CreateSectionHeaderItem("常用", dpiScale);
					menu.Items.Add(_frequentHeader);
					foreach (ApplicationItem frequentApp in frequentApps)
					{
						ToolStripMenuItem frequentItem = CreateAppMenuItem(frequentApp, isRunning: false, palette, iconSize, dpiScale);
						_frequentMenuItems.Add(frequentApp.Id, frequentItem);
						menu.Items.Add(frequentItem);
					}
					_frequentSeparator = new ToolStripSeparator();
					menu.Items.Add(_frequentSeparator);
				}
			}

			AddAllApplicationsSection(menu, apps, runningStatuses, palette, iconSize, dpiScale);

			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add(CreateOpenMenuItem(palette, iconSize, dpiScale));
			menu.Items.Add(CreateSettingsMenuItem(palette, iconSize, dpiScale));
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add(CreateExitMenuItem(palette, iconSize, dpiScale));
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Build tray menu failed", ex);
			menu.Items.Clear();
			ToolStripMenuItem failedItem = CreateMenuItem("菜单加载失败", dpiScale);
			failedItem.Enabled = false;
			menu.Items.Add(failedItem);
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add(CreateOpenMenuItem(palette, iconSize, dpiScale));
		}
		finally
		{
			menu.ResumeLayout(performLayout: false);
		}
	}

	public IReadOnlyList<ApplicationItem> GetFrequentApps()
	{
		IReadOnlyList<ApplicationItem> apps = AppServices.Catalog.GetAllApps();
		List<ApplicationItem> runningApps = apps.Where(app => IsAppRunning(_runningStatuses, app.Id)).ToList();
		int limit = Math.Clamp(AppServices.Config.Settings.TrayFrequentCount, 0, 15);
		return BuildFrequentApps(apps, runningApps, limit);
	}

	private static List<ApplicationItem> BuildFrequentApps(
		IReadOnlyList<ApplicationItem> allApps,
		IReadOnlyList<ApplicationItem> runningApps,
		int limit)
	{
		HashSet<Guid> excludedIds = runningApps.Select(app => app.Id).ToHashSet();
		List<ApplicationItem> result = new List<ApplicationItem>(limit);

		foreach (ApplicationItem pinnedApp in allApps
			.Where(app => !excludedIds.Contains(app.Id) && IsPinnedInScope(app, AppGroupScope.AllGroups))
			.OrderBy(app => app.SortIndex))
		{
			if (result.Count >= limit)
			{
				return result;
			}
			result.Add(pinnedApp);
			excludedIds.Add(pinnedApp.Id);
		}

		foreach (ApplicationItem recentApp in allApps
			.Where(app => !excludedIds.Contains(app.Id) && app.LastLaunchTime.HasValue)
			.OrderByDescending(app => app.LastLaunchTime!.Value)
			.ThenBy(app => app.SortIndex))
		{
			if (result.Count >= limit)
			{
				break;
			}
			result.Add(recentApp);
			excludedIds.Add(recentApp.Id);
		}

		return result;
	}

	private void AddAllApplicationsSection(
		ContextMenuStrip menu,
		IReadOnlyList<ApplicationItem> apps,
		IReadOnlyDictionary<Guid, ProcessStatus> runningStatuses,
		MenuPalette palette,
		int iconSize,
		float dpiScale)
	{
		if (apps.Count == 0)
		{
			ToolStripMenuItem emptyItem = CreateMenuItem("暂无应用", dpiScale);
			emptyItem.Enabled = false;
			menu.Items.Add(emptyItem);
			return;
		}

		ToolStripMenuItem allAppsItem = CreateMenuItem("全部应用", dpiScale);
		allAppsItem.Image = CreateGlyphIcon("\uE8A9", iconSize, palette.Text);
		AttachDropDownHighlightRefresh(allAppsItem, palette);

		List<ApplicationGroupMenu> groups = BuildGroupMenus(apps);
		if (groups.Count == 1)
		{
			AddGroupLaunchItem(allAppsItem, groups[0], runningStatuses, dpiScale);
			foreach (ApplicationItem app in groups[0].Apps)
			{
				allAppsItem.DropDownItems.Add(
					CreateAppMenuItem(app, IsAppRunning(runningStatuses, app.Id), palette, iconSize, dpiScale));
			}
		}
		else
		{
			foreach (ApplicationGroupMenu group in groups)
			{
				allAppsItem.DropDownItems.Add(
					CreateGroupMenuItem(group, runningStatuses, palette, iconSize, dpiScale));
			}
		}

		menu.Items.Add(allAppsItem);
	}

	private ToolStripMenuItem CreateGroupMenuItem(
		ApplicationGroupMenu group,
		IReadOnlyDictionary<Guid, ProcessStatus> runningStatuses,
		MenuPalette palette,
		int iconSize,
		float dpiScale)
	{
		int runningCount = group.Apps.Count(app => IsAppRunning(runningStatuses, app.Id));
		ToolStripMenuItem groupItem = CreateMenuItem(group.Name, dpiScale);
		_groupMenuItems[groupItem] = group;
		if (runningCount > 0)
		{
			groupItem.ShortcutKeyDisplayString = $"\u25CF {runningCount}";
			groupItem.Tag = TrayItemRole.GroupWithRunning;
		}
		AttachDropDownHighlightRefresh(groupItem, palette);
		AddGroupLaunchItem(groupItem, group, runningStatuses, dpiScale);
		foreach (ApplicationItem app in group.Apps)
		{
			groupItem.DropDownItems.Add(
				CreateAppMenuItem(app, IsAppRunning(runningStatuses, app.Id), palette, iconSize, dpiScale));
		}
		return groupItem;
	}

	private void AddGroupLaunchItem(ToolStripMenuItem parent, ApplicationGroupMenu group,
		IReadOnlyDictionary<Guid, ProcessStatus> statuses, float dpiScale)
	{
		ToolStripMenuItem launchItem = CreateMenuItem(string.Empty, dpiScale);
		_groupLaunchItems[launchItem] = group;
		UpdateGroupLaunchText(launchItem, group, statuses);
		launchItem.Click += delegate
		{
			_ = ExecuteOnUiThreadAsync(() => ExecuteBatchAsync(group.Apps, close: false));
		};
		parent.DropDownItems.Add(launchItem);
		parent.DropDownItems.Add(new ToolStripSeparator());
	}

	private static void UpdateGroupLaunchText(ToolStripMenuItem item, ApplicationGroupMenu group,
		IReadOnlyDictionary<Guid, ProcessStatus> statuses)
	{
		int stoppedCount = group.Apps.Count(app => !IsAppRunning(statuses, app.Id));
		item.Text = $"启动整组（{stoppedCount}）";
		item.Enabled = stoppedCount > 0;
	}

	private ToolStripMenuItem CreateCloseAllMenuItem(float dpiScale)
	{
		ToolStripMenuItem item = CreateMenuItem("全部关闭", dpiScale);
		item.Tag = TrayItemRole.Danger;
		item.Click += delegate
		{
			_ = ExecuteOnUiThreadAsync(async () =>
			{
				if (_batchOperationPending)
				{
					return;
				}
				MessageBoxResult answer = System.Windows.MessageBox.Show(
					"关闭所有运行中的应用？", "全部关闭", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
				if (answer == MessageBoxResult.Yes)
				{
					await ExecuteBatchAsync(AppServices.Catalog.GetAllApps(), close: true);
				}
			});
		};
		return item;
	}

	private async Task ExecuteBatchAsync(IReadOnlyList<ApplicationItem> apps, bool close)
	{
		if (_disposed || _batchOperationPending)
		{
			return;
		}
		_batchOperationPending = true;
		int succeeded = 0;
		int skipped = 0;
		List<string> failures = new List<string>();
		try
		{
			foreach (ApplicationItem app in apps)
			{
				if (_disposed)
				{
					return;
				}
				if (!_pendingAppOperations.Add(app.Id))
				{
					skipped++;
					continue;
				}
				try
				{
					ProcessStatus status = await Task.Run(() => AppServices.ProcessService.GetRunningStatus(app.Id));
					if (_disposed)
					{
						return;
					}
					if (status.IsRunning != close)
					{
						skipped++;
						continue;
					}
					bool success;
					string? error;
					if (close)
					{
						CloseResult result = await AppServices.ProcessService.CloseAppAsync(app.Id, force: false);
						success = result.Success;
						error = result.ErrorMessage;
					}
					else
					{
						LaunchResult result = AppServices.LaunchService.Launch(app.Id);
						success = result.Success;
						error = result.ErrorMessage;
					}
					if (success)
					{
						succeeded++;
					}
					else
					{
						failures.Add($"{ResolveTrayAppText(app)}：{error}");
					}
				}
				catch (Exception ex)
				{
					AppServices.Logger.Error("Tray batch operation failed", ex);
					failures.Add($"{ResolveTrayAppText(app)}：{ex.Message}");
				}
				finally
				{
					_pendingAppOperations.Remove(app.Id);
				}
			}
			string message = $"成功 {succeeded}，跳过 {skipped}，失败 {failures.Count}";
			if (failures.Count > 0)
			{
				message += $"。{string.Join("；", failures)}";
			}
			ShowMenuOperationResult(close ? "全部关闭完成" : "整组启动完成", message, null,
				failures.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
			AppServices.StatusScheduler.RequestImmediateRefresh();
		}
		finally
		{
			_batchOperationPending = false;
		}
	}

	private ToolStripMenuItem CreateAppMenuItem(
		ApplicationItem app,
		bool isRunning,
		MenuPalette palette,
		int iconSize,
		float dpiScale)
	{
		ToolStripMenuItem item = CreateMenuItem(ResolveTrayAppText(app), dpiScale);
		_appMenuItems[item] = app;
		Image statusIcon = CreateAppStatusIcon(app, isRunning, palette, iconSize, dpiScale);
		item.Image = statusIcon;
		_menuScopedImages.Add(statusIcon);
		item.ToolTipText = app.TargetPath;

		AttachDropDownHighlightRefresh(item, palette);
		UpdateAppActions(item, app, isRunning, palette, iconSize, dpiScale);
		item.Click += delegate
		{
			bool running = IsAppRunning(_runningStatuses, app.Id);
			bool withArgs = !running && (Control.ModifierKeys & Keys.Shift) != 0;
			TrayAppAction action = running ? TrayAppAction.Activate : TrayAppAction.Launch;
			if (withArgs)
			{
				action = TrayAppAction.LaunchWithArgs;
			}
			_menu.Close(ToolStripDropDownCloseReason.ItemClicked);
			_ = ExecuteOnUiThreadAsync(() => ExecuteAppFromTrayAsync(app, action));
		};
		item.DropDownClosed += delegate
		{
			if (!_disposed && _menu.Visible)
			{
				_window.Dispatcher.BeginInvoke(new Action(UpdateVisibleMenu));
			}
		};

		return item;
	}

	private void UpdateAppActions(ToolStripMenuItem item, ApplicationItem app, bool isRunning,
		MenuPalette palette, int iconSize, float dpiScale)
	{
		item.ShortcutKeyDisplayString = isRunning ? "运行中" : string.Empty;
		if (!isRunning)
		{
			if (!item.DropDown.Visible)
			{
				foreach (ToolStripItem action in item.DropDownItems.Cast<ToolStripItem>().ToArray())
				{
					action.Dispose();
				}
				item.DropDownItems.Clear();
			}
			return;
		}
		if (item.HasDropDownItems)
		{
			return;
		}
		ToolStripMenuItem activateItem = CreateMenuItem("切换到窗口", dpiScale);
		activateItem.Image = CreateGlyphIcon("\uE8A7", iconSize, palette.Text);
		activateItem.Click += delegate { _ = ExecuteOnUiThreadAsync(() => ExecuteAppFromTrayAsync(app, TrayAppAction.Activate)); };
		ToolStripMenuItem closeItem = CreateMenuItem("关闭", dpiScale);
		closeItem.Image = CreateGlyphIcon("\uE711", iconSize, palette.Text);
		closeItem.Click += delegate { _ = ExecuteOnUiThreadAsync(() => ExecuteAppFromTrayAsync(app, TrayAppAction.Close)); };
		ToolStripMenuItem locationItem = CreateMenuItem("打开所在位置", dpiScale);
		locationItem.Image = CreateGlyphIcon("\uE8B7", iconSize, palette.Text);
		locationItem.Click += delegate { _ = ExecuteOnUiThreadAsync(() => ExecuteAppFromTrayAsync(app, TrayAppAction.OpenLocation)); };
		bool allowForceKill = AppServices.Config.Settings.AllowForceKill;
		ToolStripMenuItem forceCloseItem = CreateMenuItem("强制结束", dpiScale);
		forceCloseItem.Tag = TrayItemRole.Danger;
		forceCloseItem.Enabled = allowForceKill;
		forceCloseItem.Image = CreateGlyphIcon("\uEA39", iconSize, allowForceKill ? palette.Danger : palette.SubtleText);
		forceCloseItem.Click += delegate { _ = ExecuteOnUiThreadAsync(() => ExecuteAppFromTrayAsync(app, TrayAppAction.ForceClose)); };
		item.DropDownItems.AddRange(new ToolStripItem[] { activateItem, closeItem, locationItem, new ToolStripSeparator(), forceCloseItem });
	}

	private static ToolStripMenuItem CreateSectionHeaderItem(string title, float dpiScale)
	{
		return new ToolStripMenuItem(title)
		{
			Enabled = false,
			Font = SectionHeaderFont,
			Tag = TrayItemRole.SectionHeader,
			Padding = new Padding(
				ScaleByDpi(8, dpiScale),
				ScaleByDpi(4, dpiScale),
				ScaleByDpi(8, dpiScale),
				ScaleByDpi(2, dpiScale)),
			ImageScaling = ToolStripItemImageScaling.None
		};
	}

	private ToolStripMenuItem CreateOpenMenuItem(MenuPalette palette, int iconSize, float dpiScale)
	{
		ToolStripMenuItem item = CreateMenuItem("打开 AppHub", dpiScale);
		item.Font = MenuBoldFont;
		item.Image = CreateGlyphIcon("\uE80F", iconSize, palette.Text);
		item.Click += delegate
		{
			Restore();
		};
		return item;
	}

	private ToolStripMenuItem CreateSettingsMenuItem(MenuPalette palette, int iconSize, float dpiScale)
	{
		ToolStripMenuItem item = CreateMenuItem("设置", dpiScale);
		item.Image = CreateGlyphIcon("\uE713", iconSize, palette.Text);
		item.Click += delegate
		{
			Restore();
			if (_window is MainWindow mainWindow)
			{
				mainWindow.NavigateToSettings();
			}
		};
		return item;
	}

	private ToolStripMenuItem CreateExitMenuItem(MenuPalette palette, int iconSize, float dpiScale)
	{
		ToolStripMenuItem item = CreateMenuItem("退出", dpiScale);
		item.Image = CreateGlyphIcon("\uE7E8", iconSize, palette.Text);
		item.Click += delegate
		{
			_notifyIcon.Visible = false;
			if (_window is MainWindow mainWindow)
			{
				mainWindow.RequestClose();
			}
			else
			{
				_window.Close();
			}
		};
		return item;
	}

	private static bool IsAppRunning(IReadOnlyDictionary<Guid, ProcessStatus> statuses, Guid appId)
	{
		return statuses.TryGetValue(appId, out ProcessStatus? status) && status.IsRunning;
	}

	private void DisposeMenuScopedImages()
	{
		foreach (Image menuScopedImage in _menuScopedImages)
		{
			menuScopedImage.Dispose();
		}
		_menuScopedImages.Clear();
	}

	private Image CreateAppStatusIcon(ApplicationItem app, bool isRunning, MenuPalette palette, int iconSize, float dpiScale)
	{
		Image? appIcon = GetCachedAppIcon(app, iconSize);
		Bitmap composite = new Bitmap(iconSize, iconSize);
		using Graphics graphics = Graphics.FromImage(composite);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		graphics.Clear(DrawingColor.Transparent);
		if (appIcon != null)
		{
			graphics.DrawImage(appIcon, 0, 0, iconSize, iconSize);
		}
		else
		{
			graphics.DrawIcon(SystemIcons.Application, new Rectangle(0, 0, iconSize, iconSize));
		}

		if (isRunning)
		{
			int dotDiameter = Math.Max(6, ScaleByDpi(7, dpiScale));
			float ringWidth = Math.Max(1.2f, 1.5f * dpiScale);
			float x = iconSize - dotDiameter - 0.5f;
			float y = iconSize - dotDiameter - 0.5f;
			using SolidBrush ringBrush = new SolidBrush(palette.Background);
			graphics.FillEllipse(
				ringBrush,
				x - ringWidth,
				y - ringWidth,
				dotDiameter + ringWidth * 2f,
				dotDiameter + ringWidth * 2f);
			using SolidBrush dotBrush = new SolidBrush(palette.Success);
			graphics.FillEllipse(dotBrush, x, y, dotDiameter, dotDiameter);
		}

		return composite;
	}

	private Image CreateGlyphIcon(string glyph, int size, DrawingColor color)
	{
		Bitmap bitmap = new Bitmap(size, size);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		graphics.Clear(DrawingColor.Transparent);

		float fontPx = Math.Max(10f, size * 0.78f);
		using Font font = new Font(GlyphFontFamilyName, fontPx, DrawingFontStyle.Regular, GraphicsUnit.Pixel);
		using StringFormat format = new StringFormat(StringFormat.GenericTypographic)
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			FormatFlags = StringFormatFlags.NoWrap
		};
		using SolidBrush brush = new SolidBrush(color);
		graphics.DrawString(glyph, font, brush, new RectangleF(0f, 0.5f, size, size), format);

		_menuScopedImages.Add(bitmap);
		return bitmap;
	}

	private static string ResolveGlyphFontFamilyName()
	{
		try
		{
			using InstalledFontCollection fonts = new InstalledFontCollection();
			if (fonts.Families.Any(f => string.Equals(f.Name, "Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase)))
			{
				return "Segoe Fluent Icons";
			}
		}
		catch
		{
		}
		return "Segoe MDL2 Assets";
	}

	private Image? GetCachedAppIcon(ApplicationItem app, int iconSize)
	{
		string signature = BuildIconSignature(app, iconSize);
		return _appIconCache.TryGetValue(signature, out Image? cached) ? cached : null;
	}

	private void QueueIconWarmup()
	{
		if (_disposed)
		{
			return;
		}
		int iconSize = ScaleByDpi(BaseIconSize, GetDpiScale(_menu));
		IReadOnlyList<ApplicationItem> apps = AppServices.Catalog.GetAllApps();
		_desiredIconSignatures = apps.Select(app => BuildIconSignature(app, iconSize)).ToHashSet();
		foreach (string signature in _appIconCache.Keys.Where(signature => !_desiredIconSignatures.Contains(signature)).ToArray())
		{
			_appIconCache[signature].Dispose();
			_appIconCache.Remove(signature);
		}
		foreach (ApplicationItem app in apps)
		{
			string signature = BuildIconSignature(app, iconSize);
			if (!_appIconCache.ContainsKey(signature) && _pendingIconLoads.Add(signature))
			{
				ApplicationItem snapshot = new ApplicationItem
				{
					Id = app.Id,
					IconSource = app.IconSource,
					CustomIconPath = app.CustomIconPath,
					TargetPath = app.TargetPath,
					SourceType = app.SourceType
				};
				_ = LoadAppIconAsync(snapshot, signature, iconSize);
			}
		}
	}

	private async Task LoadAppIconAsync(ApplicationItem app, string signature, int iconSize)
	{
		Image? loadedImage = null;
		CancellationToken cancellation = _iconLoadCancellation.Token;
		try
		{
			loadedImage = await Task.Run(() => LoadAppIcon(app, iconSize), cancellation).ConfigureAwait(false);
			cancellation.ThrowIfCancellationRequested();
			await _window.Dispatcher.InvokeAsync(() =>
			{
				if (_disposed)
				{
					return;
				}
				_pendingIconLoads.Remove(signature);
				if (!_desiredIconSignatures.Contains(signature))
				{
					return;
				}
				_appIconCache[signature] = loadedImage!;
				// Ownership moves to the UI thread; workers never read cached GDI images.
				loadedImage = null;
				MenuPalette palette = MenuPalette.FromTheme(AppServices.Config.Settings.IsDarkMode);
				float dpiScale = GetDpiScale(_menu);
				int currentSize = ScaleByDpi(BaseIconSize, dpiScale);
				foreach (KeyValuePair<ToolStripMenuItem, ApplicationItem> pair in _appMenuItems)
				{
					if (!pair.Key.IsDisposed && BuildIconSignature(pair.Value, currentSize) == signature)
					{
						ReplaceAppMenuIcon(pair.Key, pair.Value, IsAppRunning(_runningStatuses, pair.Value.Id), palette, currentSize, dpiScale);
					}
				}
			}, System.Windows.Threading.DispatcherPriority.Background, cancellation);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (cancellation.IsCancellationRequested)
			{
				return;
			}
			AppServices.Logger.Error("Tray icon preload failed", ex);
			if (!_window.Dispatcher.HasShutdownStarted)
			{
				_window.Dispatcher.BeginInvoke(new Action(() =>
				{
					if (!_disposed)
					{
						_pendingIconLoads.Remove(signature);
					}
				}));
			}
		}
		finally
		{
			loadedImage?.Dispose();
		}
	}

	private static string BuildIconSignature(ApplicationItem app, int iconSize)
	{
		return $"{app.IconSource}|{app.CustomIconPath}|{app.TargetPath}|{app.SourceType}|{iconSize}";
	}

	private static Image LoadAppIcon(ApplicationItem app, int size)
	{
		if (app.IconSource == IconSource.Custom
			&& !string.IsNullOrWhiteSpace(app.CustomIconPath)
			&& File.Exists(app.CustomIconPath))
		{
			try
			{
				return LoadScaledImageFromFile(app.CustomIconPath, size);
			}
			catch
			{
			}
		}
		if (!string.IsNullOrWhiteSpace(app.TargetPath))
		{
			string path = app.TargetPath;
			if (app.SourceType == SourceType.Folder || Directory.Exists(path))
			{
				return LoadFolderIcon(size);
			}
			if (File.Exists(path))
			{
				try
				{
					using Icon? icon = Icon.ExtractAssociatedIcon(path);
					if (icon != null)
					{
						using Bitmap iconBitmap = icon.ToBitmap();
						return ScaleToSquare(iconBitmap, size);
					}
				}
				catch
				{
				}
			}
		}
		using Bitmap fallback = SystemIcons.Application.ToBitmap();
		return ScaleToSquare(fallback, size);
	}

	private static Image LoadFolderIcon(int size)
	{
		if (TryGetShellFolderIcon(size, out Bitmap? folderBitmap) && folderBitmap != null)
		{
			using (folderBitmap)
			{
				return ScaleToSquare(folderBitmap, size);
			}
		}
		using Bitmap fallback = SystemIcons.Application.ToBitmap();
		return ScaleToSquare(fallback, size);
	}

	private static Image LoadScaledImageFromFile(string path, int size)
	{
		try
		{
			using Image source = Image.FromFile(path);
			return ScaleToSquare(source, size);
		}
		catch
		{
			using Bitmap fallback = SystemIcons.Application.ToBitmap();
			return ScaleToSquare(fallback, size);
		}
	}

	private static Bitmap ScaleToSquare(Image source, int size)
	{
		Bitmap result = new Bitmap(size, size);
		using Graphics graphics = Graphics.FromImage(result);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		graphics.Clear(DrawingColor.Transparent);
		float ratio = Math.Min((float)size / source.Width, (float)size / source.Height);
		int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
		int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
		int x = (size - width) / 2;
		int y = (size - height) / 2;
		graphics.DrawImage(source, x, y, width, height);
		return result;
	}

	private static List<ApplicationGroupMenu> BuildGroupMenus(IReadOnlyList<ApplicationItem> apps)
	{
		Dictionary<string, List<ApplicationItem>> map = new Dictionary<string, List<ApplicationItem>>(StringComparer.OrdinalIgnoreCase);
		foreach (ApplicationItem app in apps)
		{
			string groupName = NormalizeGroupName(app.GroupName);
			if (!map.TryGetValue(groupName, out List<ApplicationItem>? list))
			{
				list = new List<ApplicationItem>();
				map[groupName] = list;
			}
			list.Add(app);
		}

		// Match main window Chip bar order: named groups sorted alphabetically, with "未分组" placed last.
		IEnumerable<string> orderedGroupNames = map.Keys
			.OrderBy(name => string.Equals(name, UngroupedName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
			.ThenBy(name => name, StringComparer.OrdinalIgnoreCase);

		bool singleGroup = map.Count == 1;
		List<ApplicationGroupMenu> result = new List<ApplicationGroupMenu>(map.Count);
		foreach (string groupName in orderedGroupNames)
		{
			string groupScope = singleGroup || string.Equals(groupName, UngroupedName, StringComparison.OrdinalIgnoreCase)
				? AppGroupScope.AllGroups
				: groupName;
			List<ApplicationItem> orderedApps = OrderAppsByPinAndSortIndex(map[groupName], groupScope);
			result.Add(new ApplicationGroupMenu(groupName, orderedApps));
		}
		return result;
	}

	private static List<ApplicationItem> OrderAppsByPinAndSortIndex(IEnumerable<ApplicationItem> apps, string scope)
	{
		return apps
			.OrderByDescending(app => IsPinnedInScope(app, scope))
			.ThenBy(app => app.SortIndex)
			.ToList();
	}

	private static bool IsPinnedInScope(ApplicationItem app, string scope)
	{
		if (app.GetPinned(scope))
		{
			return true;
		}
		return app.IsPinned && string.Equals(scope, AppGroupScope.AllGroups, StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeGroupName(string? groupName)
	{
		return string.IsNullOrWhiteSpace(groupName) ? UngroupedName : groupName.Trim();
	}

	public static string ResolveTrayAppText(ApplicationItem app)
	{
		if (!string.IsNullOrWhiteSpace(app.DisplayName))
		{
			return app.DisplayName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(app.TargetPath))
		{
			try
			{
				if (app.SourceType == SourceType.Folder)
				{
					string directoryName = new DirectoryInfo(app.TargetPath).Name;
					if (!string.IsNullOrWhiteSpace(directoryName))
					{
						return directoryName;
					}
				}
				else
				{
					string fileName = Path.GetFileNameWithoutExtension(app.TargetPath);
					if (!string.IsNullOrWhiteSpace(fileName))
					{
						return fileName;
					}
				}
			}
			catch
			{
			}
		}
		return "\u672a\u547d\u540d\u5e94\u7528";
	}

	public Task LaunchAppAsync(Guid appId)
	{
		return ExecuteOnUiThreadAsync(() =>
		{
			ApplicationItem? app = AppServices.Catalog.GetAppById(appId);
			if (app == null)
			{
				ShowMenuOperationResult("启动失败", "应用不存在", null, ToolTipIcon.Warning);
				return Task.CompletedTask;
			}
			return ExecuteAppFromTrayAsync(app, TrayAppAction.Launch);
		});
	}

	private async Task ExecuteAppFromTrayAsync(ApplicationItem app, TrayAppAction action)
	{
		if (_disposed)
		{
			return;
		}
		if (!_pendingAppOperations.Add(app.Id))
		{
			return;
		}
		string appName = ResolveTrayAppText(app);
		try
		{
			switch (action)
			{
			case TrayAppAction.Activate:
				await ActivateAppFromTrayAsync(app, appName);
				break;
			case TrayAppAction.Close:
				await CloseAppFromTrayAsync(app, appName, force: false);
				break;
			case TrayAppAction.ForceClose:
				await CloseAppFromTrayAsync(app, appName, force: true);
				break;
			case TrayAppAction.OpenLocation:
				AppServices.LaunchService.OpenFileLocation(app.Id);
				break;
			case TrayAppAction.LaunchWithArgs:
				await LaunchAppIfStoppedAsync(app, appName, withArgs: true);
				break;
			default:
				await LaunchAppIfStoppedAsync(app, appName);
				break;
			}
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Tray app operation failed", ex);
			string failureTitle = action switch
			{
				TrayAppAction.Activate => "切换窗口失败",
				TrayAppAction.Close => "关闭失败",
				TrayAppAction.ForceClose => "强制结束失败",
				TrayAppAction.OpenLocation => "打开所在位置失败",
				_ => "启动失败"
			};
			ShowMenuOperationResult(failureTitle, appName, ex.Message, ToolTipIcon.Error);
		}
		finally
		{
			_pendingAppOperations.Remove(app.Id);
		}
	}

	private async Task ActivateAppFromTrayAsync(ApplicationItem app, string appName)
	{
		LaunchResult result = await Task.Run(() => AppServices.LaunchService.ActivateRunningWindow(app.Id));
		if (_disposed)
		{
			return;
		}
		if (!result.Success)
		{
			AppServices.StatusScheduler.RequestImmediateRefresh();
			ShowMenuOperationResult("无法切换窗口", appName, result.ErrorMessage, ToolTipIcon.Warning);
		}
	}

	private async Task LaunchAppIfStoppedAsync(ApplicationItem app, string appName, bool withArgs = false)
	{
		ProcessStatus status = await Task.Run(() => AppServices.ProcessService.GetRunningStatus(app.Id));
		if (_disposed)
		{
			return;
		}
		if (status.IsRunning)
		{
			AppServices.StatusScheduler.RequestImmediateRefresh();
			await ActivateAppFromTrayAsync(app, appName);
			return;
		}
		string? arguments = null;
		if (withArgs)
		{
			LaunchArgumentsDialog dialog = new LaunchArgumentsDialog(appName, app.Arguments);
			if (_window.IsVisible)
			{
				dialog.Owner = _window;
			}
			if (dialog.ShowDialog() != true)
			{
				return;
			}
			arguments = dialog.Arguments;
			status = await Task.Run(() => AppServices.ProcessService.GetRunningStatus(app.Id));
			if (_disposed)
			{
				return;
			}
			if (status.IsRunning)
			{
				await ActivateAppFromTrayAsync(app, appName);
				return;
			}
		}
		LaunchAppFromTray(app, appName, arguments);
	}

	private void LaunchAppFromTray(ApplicationItem app, string appName, string? arguments = null)
	{
		try
		{
			LaunchResult result = arguments == null
				? AppServices.LaunchService.Launch(app.Id)
				: AppServices.LaunchService.LaunchWithArgs(app.Id, arguments);
			if (!result.Success)
			{
				ShowMenuOperationResult("启动失败", appName, result.ErrorMessage, ToolTipIcon.Warning);
				return;
			}
			if (app.TrackProcess && app.SourceType != SourceType.Folder)
			{
				BeginLaunchingFeedback(app.Id, appName);
			}
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Launch from tray failed", ex);
			ShowMenuOperationResult("启动失败", appName, ex.Message, ToolTipIcon.Error);
		}
	}

	private void BeginLaunchingFeedback(Guid appId, string appName)
	{
		_launchingAppId = appId;
		_launchingAppName = appName;
		int token = ++_launchingToken;
		UpdateNotifyIconText();
		_ = ClearLaunchingFeedbackAfterTimeoutAsync(token);
	}

	private async Task ClearLaunchingFeedbackAfterTimeoutAsync(int token)
	{
		await Task.Delay(3500);
		if (_disposed || token != _launchingToken)
		{
			return;
		}
		_launchingAppId = null;
		_launchingAppName = null;
		UpdateNotifyIconText();
	}

	private async Task CloseAppFromTrayAsync(ApplicationItem app, string appName, bool force)
	{
		try
		{
			CloseResult result = await AppServices.ProcessService.CloseAppAsync(app.Id, force);
			if (!result.Success)
			{
				ShowMenuOperationResult(force ? "强制结束失败" : "关闭失败", appName, result.ErrorMessage, ToolTipIcon.Warning);
			}
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Close from tray failed", ex);
			ShowMenuOperationResult(force ? "强制结束失败" : "关闭失败", appName, ex.Message, ToolTipIcon.Error);
		}
	}

	private void ShowMenuOperationResult(string title, string appName, string? detail, ToolTipIcon icon)
	{
		if (_disposed)
		{
			return;
		}
		string message = string.IsNullOrWhiteSpace(detail) ? appName : $"{appName}\uff1a{detail}";
		if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
		{
			string toastText = $"{title}（{message}）";
			switch (icon)
			{
			case ToolTipIcon.Error:
				ToastService.ShowError(toastText);
				break;
			case ToolTipIcon.Warning:
				ToastService.ShowWarning(toastText);
				break;
			default:
				ToastService.Show(toastText);
				break;
			}
			return;
		}
		_notifyIcon.ShowBalloonTip(1200, title, message, icon);
	}

	private void UpdateNotifyIconText()
	{
		if (_disposed)
		{
			return;
		}
		string text;
		if (!string.IsNullOrWhiteSpace(_launchingAppName))
		{
			text = $"AppHub \u00b7 正在启动 {_launchingAppName}\u2026";
		}
		else
		{
			int runningCount = _runningStatuses.Values.Count(status => status.IsRunning);
			text = runningCount > 0 ? $"AppHub \u00b7 {runningCount} 个运行中" : "AppHub";
		}
		if (text.Length > MaxNotifyTextLength)
		{
			text = text.Substring(0, MaxNotifyTextLength - 1) + "\u2026";
		}
		try
		{
			_notifyIcon.Text = text;
			UpdateTrayStatusIcon();
		}
		catch
		{
		}
	}

	private Task ExecuteOnUiThreadAsync(Func<Task> action)
	{
		if (_window.Dispatcher.CheckAccess())
		{
			return action();
		}
		return _window.Dispatcher.InvokeAsync(action).Task.Unwrap();
	}

	private void UpdateTrayStatusIcon()
	{
		bool running = _runningStatuses.Values.Any(status => status.IsRunning);
		float dpiScale = GetDpiScale(_menu);
		int size = ScaleByDpi(16, dpiScale);
		if (_statusTrayIconRunning == running && _statusTrayIconSize == size)
		{
			return;
		}
		Icon? previousIcon = _statusTrayIcon;
		_statusTrayIcon = null;
		if (running)
		{
			using Bitmap bitmap = new Bitmap(size, size);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.Clear(DrawingColor.Transparent);
				graphics.DrawIcon(_baseTrayIcon, new Rectangle(0, 0, size, size));
				int diameter = ScaleByDpi(6, dpiScale);
				MenuPalette palette = MenuPalette.FromTheme(AppServices.Config.Settings.IsDarkMode);
				using SolidBrush brush = new SolidBrush(palette.Success);
				graphics.FillEllipse(brush, size - diameter - 0.5f, size - diameter - 0.5f, diameter, diameter);
			}
			nint hIcon = bitmap.GetHicon();
			try
			{
				using Icon borrowedIcon = Icon.FromHandle(hIcon);
				_statusTrayIcon = (Icon)borrowedIcon.Clone();
			}
			finally
			{
				DestroyIcon(hIcon);
			}
		}
		_notifyIcon.Icon = _statusTrayIcon ?? _baseTrayIcon;
		_statusTrayIconRunning = running;
		_statusTrayIconSize = size;
		previousIcon?.Dispose();
	}

	private static ToolStripMenuItem CreateMenuItem(string text, float dpiScale)
	{
		return new ToolStripMenuItem(text)
		{
			Padding = new Padding(
				ScaleByDpi(8, dpiScale),
				ScaleByDpi(5, dpiScale),
				ScaleByDpi(8, dpiScale),
				ScaleByDpi(5, dpiScale)),
			ImageScaling = ToolStripItemImageScaling.None
		};
	}

	private void ApplyMenuAppearance(ContextMenuStrip menu, MenuPalette palette, float dpiScale)
	{
		int iconSize = ScaleByDpi(BaseIconSize, dpiScale);
		int menuPadding = ScaleByDpi(4, dpiScale);
		TrayMenuRenderer renderer = new TrayMenuRenderer(new TrayMenuColorTable(palette), palette, dpiScale);

		menu.AutoSize = true;
		menu.RenderMode = ToolStripRenderMode.Professional;
		menu.Renderer = renderer;
		menu.ShowCheckMargin = false;
		menu.ShowImageMargin = true;
		menu.DropShadowEnabled = true;
		menu.ImageScalingSize = new DrawingSize(iconSize, iconSize);
		menu.Padding = new Padding(menuPadding);
		menu.BackColor = palette.Background;
		menu.ForeColor = palette.Text;
		menu.Font = MenuFont;

		ApplyMenuItemAppearance(menu.Items, palette, renderer, iconSize, menuPadding);
		EnsureDropDownWidthForArrows(menu);
		ApplyNativeDropDownWindowStyle(menu, palette);
	}

	private static void ApplyMenuItemAppearance(
		ToolStripItemCollection items,
		MenuPalette palette,
		TrayMenuRenderer renderer,
		int iconSize,
		int menuPadding)
	{
		foreach (ToolStripItem item in items)
		{
			if (item is ToolStripMenuItem menuItem)
			{
				menuItem.BackColor = palette.Background;
				menuItem.ForeColor = ResolveItemForeColor(menuItem, palette);
				if (menuItem.HasDropDownItems)
				{
					menuItem.DropDown.BackColor = palette.Background;
					menuItem.DropDown.ForeColor = palette.Text;
					menuItem.DropDown.Font = MenuFont;
					menuItem.DropDown.Renderer = renderer;
					if (menuItem.DropDown is ToolStripDropDownMenu dropDownMenu)
					{
						dropDownMenu.ShowImageMargin = true;
						dropDownMenu.ShowCheckMargin = false;
						dropDownMenu.DropShadowEnabled = true;
						dropDownMenu.ImageScalingSize = new DrawingSize(iconSize, iconSize);
						dropDownMenu.Padding = new Padding(menuPadding);
						dropDownMenu.Margin = Padding.Empty;
					}
					ApplyMenuItemAppearance(menuItem.DropDownItems, palette, renderer, iconSize, menuPadding);
					EnsureDropDownWidthForArrows(menuItem.DropDown);
				}
			}
		}
	}

	private static DrawingColor ResolveItemForeColor(ToolStripMenuItem menuItem, MenuPalette palette)
	{
		if (!menuItem.Enabled)
		{
			return palette.SubtleText;
		}
		if (menuItem.Tag is TrayItemRole.Danger)
		{
			return palette.Danger;
		}
		return palette.Text;
	}

	private static void EnsureDropDownWidthForArrows(ToolStripDropDown dropDown)
	{
		dropDown.AutoSize = true;
		dropDown.PerformLayout();
		int dropDownItemRight = dropDown.Items.OfType<ToolStripMenuItem>()
			.Where(item => item.HasDropDownItems)
			.Select(item => item.Bounds.Right)
			.DefaultIfEmpty(0)
			.Max();
		if (dropDownItemRight > dropDown.Width)
		{
			dropDown.AutoSize = false;
			dropDown.Width = dropDownItemRight;
		}
	}

	private static void AttachDropDownHighlightRefresh(ToolStripMenuItem menuItem, MenuPalette palette)
	{
		menuItem.DropDownOpened += delegate
		{
			ApplyNativeDropDownWindowStyle(menuItem.DropDown, palette);
			EnsureDropDownWidthForArrows(menuItem.DropDown);
			menuItem.Owner?.Invalidate(menuItem.Bounds);
		};
		menuItem.DropDownClosed += delegate
		{
			menuItem.Owner?.Invalidate(menuItem.Bounds);
		};
	}

	private static bool ApplyNativeDropDownWindowStyle(ToolStripDropDown dropDown, MenuPalette palette)
	{
		if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !dropDown.IsHandleCreated)
		{
			return false;
		}
		try
		{
			nint hwnd = dropDown.Handle;
			int cornerPreference = DwmCornerRoundSmall;
			int hr = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));
			int borderColorRef = ColorTranslator.ToWin32(palette.Border);
			DwmSetWindowAttribute(hwnd, DwmBorderColor, ref borderColorRef, sizeof(int));
			return hr == 0;
		}
		catch
		{
			return false;
		}
	}

	private float GetDpiScale(ToolStrip menu)
	{
		float scale = menu.DeviceDpi > 0 ? menu.DeviceDpi / 96f : 1f;
		if (Math.Abs(scale - 1f) < 0.01f)
		{
			try
			{
				double wpfScale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
				if (wpfScale > 0.1)
				{
					scale = (float)wpfScale;
				}
			}
			catch
			{
			}
		}
		return Math.Clamp(scale, 1f, 4f);
	}

	private static int ScaleByDpi(int basePixels, float dpiScale)
	{
		return Math.Max(1, (int)Math.Round(basePixels * dpiScale));
	}

	private static bool TryGetShellFolderIcon(int targetSize, out Bitmap? bitmap)
	{
		bitmap = null;
		ShFileInfo fileInfo = default(ShFileInfo);
		uint iconSizeFlag = targetSize > 20 ? ShgfiLargeIcon : ShgfiSmallIcon;
		nint result = SHGetFileInfo(
			"folder",
			FileAttributeDirectory,
			ref fileInfo,
			(uint)Marshal.SizeOf<ShFileInfo>(),
			ShgfiIcon | iconSizeFlag | ShgfiUseFileAttributes);
		if (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			using Icon icon = Icon.FromHandle(fileInfo.hIcon);
			bitmap = icon.ToBitmap();
			return true;
		}
		finally
		{
			DestroyIcon(fileInfo.hIcon);
		}
	}

	private void OnWindowActivated(object? sender, EventArgs e)
	{
		_isWindowInForeground = true;
	}

	private void OnWindowDeactivated(object? sender, EventArgs e)
	{
		if (!IsTaskbarOrTrayForeground())
		{
			_isWindowInForeground = false;
		}
	}

	private static bool IsTaskbarOrTrayForeground()
	{
		nint fgHwnd = GetForegroundWindow();
		if (fgHwnd == IntPtr.Zero)
		{
			return false;
		}
		StringBuilder classNameBuilder = new StringBuilder(64);
		if (GetClassName(fgHwnd, classNameBuilder, classNameBuilder.Capacity) <= 0)
		{
			return false;
		}
		string className = classNameBuilder.ToString();
		return string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(className, "NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(className, "TopLevelWindowForOverflowXamlIsland", StringComparison.OrdinalIgnoreCase);
	}

	private void OnWindowStateChanged(object? sender, EventArgs e)
	{
		if (_window.WindowState == WindowState.Minimized)
		{
			_isWindowInForeground = false;
			_notifyIcon.Visible = true;
		}
		else if (_window.IsVisible)
		{
			_notifyIcon.Visible = true;
			SetBackgroundMode(isBackground: false);
		}
	}

	private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		if (AppServices.Config.Settings.TrayLeftClickAction == TrayLeftClickAction.ShowMenu)
		{
			_menu.Show(Control.MousePosition);
			return;
		}
		bool isVisibleAndActive = _window.IsVisible
			&& _window.WindowState != WindowState.Minimized
			&& (_window.IsActive || _isWindowInForeground);

		if (isVisibleAndActive)
		{
			HideToTray();
		}
		else
		{
			Restore();
		}
	}

	public void HideToTray(bool showHint = false)
	{
		_isWindowInForeground = false;
		_window.Hide();
		_notifyIcon.Visible = true;
		SetBackgroundMode(isBackground: true);
		if (showHint && !AppServices.Config.Settings.TrayHintShown)
		{
			_notifyIcon.ShowBalloonTip(1000, "AppHub", "AppHub \u6b63\u5728\u6258\u76d8\u4e2d\u8fd0\u884c\u3002", ToolTipIcon.Info);
			AppServices.Config.Settings.TrayHintShown = true;
			AppServices.Storage.ScheduleSave(AppServices.Config);
		}
	}

	public void Restore()
	{
		_window.Show();
		if (_window.WindowState == WindowState.Minimized)
		{
			_window.WindowState = WindowState.Normal;
		}
		nint handle = new WindowInteropHelper(_window).Handle;
		if (handle != IntPtr.Zero)
		{
			SetForegroundWindow(handle);
		}
		_window.Activate();
		_isWindowInForeground = true;
		_notifyIcon.Visible = true;
		SetBackgroundMode(isBackground: false);
	}

	private static Icon GetAppIcon()
	{
		try
		{
			string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrWhiteSpace(exePath))
			{
				Icon? icon = Icon.ExtractAssociatedIcon(exePath);
				if (icon != null)
				{
					return icon;
				}
			}
		}
		catch
		{
		}
		return (Icon)SystemIcons.Application.Clone();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_iconLoadCancellation.Cancel();
		_launchingToken++;
		SetBackgroundMode(isBackground: false);
		AppServices.ProcessService.ProcessStatusesChanged -= OnProcessStatusesChanged;
		AppServices.Catalog.Changed -= OnCatalogChanged;
		AppServices.TraySettingsChanged -= OnTraySettingsChanged;
		_window.StateChanged -= OnWindowStateChanged;
		_window.Activated -= OnWindowActivated;
		_window.Deactivated -= OnWindowDeactivated;
		_notifyIcon.Visible = false;
		_notifyIcon.MouseClick -= OnNotifyIconMouseClick;
		_menu.Opening -= OnMenuOpening;
		_menu.Opened -= OnMenuOpened;
		DisposeMenuScopedImages();
		foreach (Image icon in _appIconCache.Values)
		{
			icon.Dispose();
		}
		_appIconCache.Clear();
		_notifyIcon.Dispose();
		_statusTrayIcon?.Dispose();
		_baseTrayIcon.Dispose();
		_iconLoadCancellation.Dispose();
		_menu.Dispose();
	}

	private void SetBackgroundMode(bool isBackground)
	{
		if (_isBackgroundMode != isBackground)
		{
			_isBackgroundMode = isBackground;
			this.BackgroundModeChanged?.Invoke(this, isBackground);
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyIcon(nint hIcon);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetForegroundWindow(nint hWnd);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct ShFileInfo
	{
		public nint hIcon;

		public int iIcon;

		public uint dwAttributes;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szDisplayName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string szTypeName;
	}

	private enum TrayAppAction
	{
		Launch,
		LaunchWithArgs,
		Activate,
		Close,
		ForceClose,
		OpenLocation
	}

	private enum TrayItemRole
	{
		SectionHeader,
		GroupWithRunning,
		Danger
	}

	private sealed record ApplicationGroupMenu(string Name, IReadOnlyList<ApplicationItem> Apps);

	private readonly struct MenuPalette
	{
		public DrawingColor Background { get; }

		public DrawingColor Hover { get; }

		public DrawingColor Selected { get; }

		public DrawingColor Pressed { get; }

		public DrawingColor Border { get; }

		public DrawingColor Text { get; }

		public DrawingColor SubtleText { get; }

		public DrawingColor Accent { get; }

		public DrawingColor Success { get; }

		public DrawingColor Danger { get; }

		public MenuPalette(
			DrawingColor background,
			DrawingColor hover,
			DrawingColor selected,
			DrawingColor pressed,
			DrawingColor border,
			DrawingColor text,
			DrawingColor subtleText,
			DrawingColor accent,
			DrawingColor success,
			DrawingColor danger)
		{
			Background = background;
			Hover = hover;
			Selected = selected;
			Pressed = pressed;
			Border = border;
			Text = text;
			SubtleText = subtleText;
			Accent = accent;
			Success = success;
			Danger = danger;
		}

		public static MenuPalette FromTheme(bool isDarkMode)
		{
			ResourceDictionary? resources = System.Windows.Application.Current?.Resources;
			DrawingColor background = ReadResourceColor(
				resources,
				"CardBgColor",
				"CardBgBrush",
				isDarkMode ? DrawingColor.FromArgb(16, 26, 43) : DrawingColor.FromArgb(255, 255, 255));
			DrawingColor text = ReadResourceColor(
				resources,
				"TextColor",
				"TextBrush",
				isDarkMode ? DrawingColor.FromArgb(234, 242, 255) : DrawingColor.FromArgb(15, 23, 42),
				background);
			DrawingColor subtleText = ReadResourceColor(
				resources,
				"SubtleTextColor",
				"SubtleTextBrush",
				isDarkMode ? DrawingColor.FromArgb(148, 168, 198) : DrawingColor.FromArgb(71, 85, 105),
				background);
			DrawingColor border = ReadResourceColor(
				resources,
				"BorderColor",
				"BorderBrush",
				isDarkMode ? DrawingColor.FromArgb(38, 53, 74) : DrawingColor.FromArgb(220, 229, 240),
				background);
			DrawingColor hover = ReadResourceColor(
				resources,
				"HoverBgColor",
				"HoverBgBrush",
				isDarkMode ? DrawingColor.FromArgb(23, 40, 66) : DrawingColor.FromArgb(238, 244, 251),
				background);
			DrawingColor accent = ReadResourceColor(
				resources,
				"AccentColor",
				"AccentBrush",
				isDarkMode ? DrawingColor.FromArgb(56, 189, 248) : DrawingColor.FromArgb(2, 132, 199),
				background);
			DrawingColor success = ReadResourceColor(
				resources,
				"SuccessColor",
				"SuccessBrush",
				isDarkMode ? DrawingColor.FromArgb(48, 209, 111) : DrawingColor.FromArgb(22, 163, 74),
				background);
			DrawingColor danger = ReadResourceColor(
				resources,
				"DangerColor",
				"DangerBrush",
				isDarkMode ? DrawingColor.FromArgb(239, 68, 68) : DrawingColor.FromArgb(220, 38, 38),
				background);
			DrawingColor selected = ReadResourceColor(
				resources,
				"SelectedBgColor",
				"SelectedBgBrush",
				BlendColor(accent, background, isDarkMode ? 0.16f : 0.10f),
				background);
			DrawingColor pressed = BlendColor(accent, hover, isDarkMode ? 0.24f : 0.16f);

			return new MenuPalette(background, hover, selected, pressed, border, text, subtleText, accent, success, danger);
		}

		private static DrawingColor ReadResourceColor(
			ResourceDictionary? resources,
			string colorKey,
			string brushKey,
			DrawingColor fallback,
			DrawingColor? compositeBackground = null)
		{
			if (resources != null)
			{
				if (resources[colorKey] is MediaColor mediaColor)
				{
					return ToDrawingColor(mediaColor, compositeBackground);
				}
				if (resources[brushKey] is SolidColorBrush brush)
				{
					return ToDrawingColor(brush.Color, compositeBackground);
				}
			}
			return fallback;
		}

		private static DrawingColor ToDrawingColor(MediaColor color, DrawingColor? compositeBackground)
		{
			if (color.A < byte.MaxValue && compositeBackground.HasValue)
			{
				float alpha = color.A / 255f;
				DrawingColor bg = compositeBackground.Value;
				int r = (int)Math.Round(color.R * alpha + bg.R * (1f - alpha));
				int g = (int)Math.Round(color.G * alpha + bg.G * (1f - alpha));
				int b = (int)Math.Round(color.B * alpha + bg.B * (1f - alpha));
				return DrawingColor.FromArgb(
					Math.Clamp(r, 0, 255),
					Math.Clamp(g, 0, 255),
					Math.Clamp(b, 0, 255));
			}
			return DrawingColor.FromArgb(color.R, color.G, color.B);
		}

		private static DrawingColor BlendColor(DrawingColor foreground, DrawingColor background, float ratio)
		{
			int r = (int)Math.Round(foreground.R * ratio + background.R * (1f - ratio));
			int g = (int)Math.Round(foreground.G * ratio + background.G * (1f - ratio));
			int b = (int)Math.Round(foreground.B * ratio + background.B * (1f - ratio));
			return DrawingColor.FromArgb(
				Math.Clamp(r, 0, 255),
				Math.Clamp(g, 0, 255),
				Math.Clamp(b, 0, 255));
		}
	}

	private sealed class TrayMenuColorTable : ProfessionalColorTable
	{
		private readonly MenuPalette _palette;

		public override DrawingColor ToolStripDropDownBackground => _palette.Background;

		public override DrawingColor MenuItemSelected => _palette.Hover;

		public override DrawingColor MenuItemSelectedGradientBegin => _palette.Hover;

		public override DrawingColor MenuItemSelectedGradientEnd => _palette.Hover;

		public override DrawingColor MenuItemBorder => _palette.Background;

		public override DrawingColor MenuBorder => _palette.Border;

		public override DrawingColor MenuItemPressedGradientBegin => _palette.Pressed;

		public override DrawingColor MenuItemPressedGradientMiddle => _palette.Pressed;

		public override DrawingColor MenuItemPressedGradientEnd => _palette.Pressed;

		public override DrawingColor SeparatorDark => _palette.Border;

		public override DrawingColor SeparatorLight => _palette.Border;

		public override DrawingColor ImageMarginGradientBegin => _palette.Background;

		public override DrawingColor ImageMarginGradientMiddle => _palette.Background;

		public override DrawingColor ImageMarginGradientEnd => _palette.Background;

		public TrayMenuColorTable(MenuPalette palette)
		{
			_palette = palette;
			UseSystemColors = false;
		}
	}

	private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
	{
		private readonly MenuPalette _palette;

		private readonly float _dpiScale;

		public TrayMenuRenderer(ProfessionalColorTable colorTable, MenuPalette palette, float dpiScale)
			: base(colorTable)
		{
			_palette = palette;
			_dpiScale = dpiScale;
			RoundedEdges = false;
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
		{
			using SolidBrush brush = new SolidBrush(_palette.Background);
			e.Graphics.FillRectangle(brush, e.AffectedBounds);
		}

		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
		{
			if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && e.ToolStrip.IsHandleCreated)
			{
				// Win11 uses native DWM rounded corners and DWM border color.
				return;
			}
			Rectangle borderRect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
			if (borderRect.Width > 0 && borderRect.Height > 0)
			{
				using DrawingPen pen = new DrawingPen(_palette.Border);
				e.Graphics.DrawRectangle(pen, borderRect);
			}
		}

		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
		{
			if (!e.Item.Enabled)
			{
				return;
			}

			bool hasVisibleDropDown = e.Item is ToolStripMenuItem menuItem && menuItem.DropDown.Visible;
			bool isSelected = e.Item.Selected;
			bool isPressed = e.Item.Pressed;
			if (!isPressed && !hasVisibleDropDown && !isSelected)
			{
				return;
			}

			int horizontalInset = ScaleByDpi(BaseItemHorizontalInset, _dpiScale);
			int verticalInset = ScaleByDpi(BaseItemVerticalInset, _dpiScale);
			int cornerRadius = ScaleByDpi(BaseItemCornerRadius, _dpiScale);

			Rectangle bounds = new Rectangle(DrawingPoint.Empty, e.Item.Size);
			Rectangle fillRect = Rectangle.Inflate(bounds, -horizontalInset, -verticalInset);
			if (fillRect.Width <= 0 || fillRect.Height <= 0)
			{
				return;
			}

			DrawingColor fillColor;
			if (isPressed)
			{
				fillColor = _palette.Pressed;
			}
			else if (hasVisibleDropDown)
			{
				fillColor = _palette.Selected;
			}
			else
			{
				fillColor = _palette.Hover;
			}

			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = CreateRoundedRectanglePath(fillRect, cornerRadius);
			using SolidBrush brush = new SolidBrush(fillColor);
			e.Graphics.FillPath(brush, path);
		}

		protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
		{
			int y = e.Item.Bounds.Height / 2;
			int horizontalMargin = ScaleByDpi(10, _dpiScale);
			using DrawingPen pen = new DrawingPen(_palette.Border);
			e.Graphics.DrawLine(pen, horizontalMargin, y, e.Item.Bounds.Width - horizontalMargin, y);
		}

		protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
		{
			e.ArrowColor = e.Item?.Enabled == true ? _palette.SubtleText : _palette.Border;
			base.OnRenderArrow(e);
		}

		protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
		{
			Rectangle imageBounds = e.ImageRectangle;
			imageBounds.X = ScaleByDpi(8, _dpiScale);
			imageBounds.Y = (e.Item.Height - imageBounds.Height) / 2;
			base.OnRenderItemImage(new ToolStripItemImageRenderEventArgs(e.Graphics, e.Item, e.Image, imageBounds));
		}

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			// WinForms shares a text rectangle that excludes each item's extra padding.
			e.TextRectangle = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
			e.TextFormat = (e.TextFormat & ~TextFormatFlags.Bottom) | TextFormatFlags.VerticalCenter;

			if (e.Item.Tag is TrayItemRole.SectionHeader)
			{
				int leftMargin = ScaleByDpi(12, _dpiScale);
				e.TextRectangle = new Rectangle(
					leftMargin,
					e.TextRectangle.Y,
					Math.Max(10, e.Item.Width - leftMargin * 2),
					e.TextRectangle.Height);
				e.TextColor = _palette.SubtleText;
				base.OnRenderItemText(e);
				return;
			}

			if (e.Item is ToolStripMenuItem menuItem
				&& !string.IsNullOrEmpty(menuItem.ShortcutKeyDisplayString)
				&& string.Equals(e.Text, menuItem.ShortcutKeyDisplayString, StringComparison.Ordinal))
			{
				e.TextColor = menuItem.Tag is TrayItemRole.GroupWithRunning
					? _palette.Success
					: _palette.SubtleText;
				base.OnRenderItemText(e);
				return;
			}

			if (!e.Item.Enabled)
			{
				e.TextColor = _palette.SubtleText;
			}
			else if (e.Item.Tag is TrayItemRole.Danger)
			{
				e.TextColor = _palette.Danger;
			}
			else
			{
				e.TextColor = _palette.Text;
			}
			base.OnRenderItemText(e);
		}

		private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
		{
			GraphicsPath path = new GraphicsPath();
			if (rect.Width <= 0 || rect.Height <= 0)
			{
				return path;
			}

			int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
			int diameter = safeRadius * 2;
			Rectangle arc = new Rectangle(rect.Location, new DrawingSize(diameter, diameter));

			path.AddArc(arc, 180, 90);
			arc.X = rect.Right - diameter;
			path.AddArc(arc, 270, 90);
			arc.Y = rect.Bottom - diameter;
			path.AddArc(arc, 0, 90);
			arc.X = rect.Left;
			path.AddArc(arc, 90, 90);
			path.CloseFigure();
			return path;
		}
	}
}
