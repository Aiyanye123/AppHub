using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AppHub.Infrastructure;
using AppHub.Models;
using AppHub.ViewModels;
using AppHub.Views.Dialogs;
using Microsoft.Win32;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using DialogResult = System.Windows.Forms.DialogResult;

namespace AppHub.Views;

public partial class OverviewView : UserControl
{
	private const double InspectorExpandedWidth = 288;

	private const double InspectorGapWidth = 14;

	private Point _dragStartPoint;

	private AppItemViewModel? _dragItem;

	private ListBoxItem? _dragSourceItem;

	private ListBoxItem? _dragOverItem;

	private string _dragOverPosition = string.Empty;

	private bool _isLoaded;

	private OverviewViewModel ViewModel => (OverviewViewModel)base.DataContext;

	public OverviewView()
	{
		InitializeComponent();
		if (base.DataContext is OverviewViewModel vm)
		{
			vm.EditRequested += OnEditRequested;
			vm.RemoveRequested += OnRemoveRequested;
			vm.PropertyChanged += OnViewModelPropertyChanged;
		}
	}

	public void FocusSearch()
	{
		SearchBox.Focus();
		Keyboard.Focus(SearchBox);
		SearchBox.SelectAll();
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		ViewModel.SyncPreferencesFromSettings();
		SetInspectorVisible(ViewModel.ShowInspector, animate: false);
		_isLoaded = true;
		AppServices.StatusScheduler.RequestImmediateRefresh();
		ViewModel.RefreshSummary();
		Dispatcher.BeginInvoke(new Action(delegate
		{
			SearchBox.Focus();
			Keyboard.Focus(SearchBox);
		}), DispatcherPriority.Input);
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_isLoaded = false;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(OverviewViewModel.ShowInspector))
		{
			SetInspectorVisible(ViewModel.ShowInspector, _isLoaded);
		}
	}

	private void SetInspectorVisible(bool visible, bool animate)
	{
		if (visible)
		{
			InspectorPanel.Visibility = Visibility.Visible;
			InspectorGap.Visibility = Visibility.Visible;
		}
		AnimateWidth(InspectorPanel, visible ? InspectorExpandedWidth : 0, animate);
		AnimateWidth(InspectorGap, visible ? InspectorGapWidth : 0, animate);
		AnimateOpacity(InspectorPanel, visible ? 1 : 0, animate);
		if (!visible && animate)
		{
			DispatcherTimer timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(180)
			};
			timer.Tick += delegate
			{
				timer.Stop();
				if (!ViewModel.ShowInspector)
				{
					InspectorPanel.Visibility = Visibility.Collapsed;
					InspectorGap.Visibility = Visibility.Collapsed;
				}
			};
			timer.Start();
		}
		else if (!visible)
		{
			InspectorPanel.Visibility = Visibility.Collapsed;
			InspectorGap.Visibility = Visibility.Collapsed;
		}
	}

	private static void AnimateWidth(FrameworkElement element, double target, bool animate)
	{
		if (!animate)
		{
			element.BeginAnimation(WidthProperty, null);
			element.Width = target;
			return;
		}
		element.BeginAnimation(WidthProperty, CreateAnimation(target, 180));
	}

	private static void AnimateOpacity(UIElement element, double target, bool animate)
	{
		if (!animate)
		{
			element.BeginAnimation(OpacityProperty, null);
			element.Opacity = target;
			return;
		}
		element.BeginAnimation(OpacityProperty, CreateAnimation(target, 140));
	}

	private static DoubleAnimation CreateAnimation(double target, int milliseconds)
	{
		return new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		ModifierKeys modifiers = Keyboard.Modifiers;

		if (modifiers == ModifierKeys.Control && e.Key == Key.F)
		{
			SearchBox.Focus();
			Keyboard.Focus(SearchBox);
			SearchBox.SelectAll();
			e.Handled = true;
			return;
		}

		if (modifiers == ModifierKeys.Control && e.Key == Key.N)
		{
			OnAddClick(this, new RoutedEventArgs());
			e.Handled = true;
			return;
		}

		if (modifiers == ModifierKeys.None && e.Key == Key.F5)
		{
			if (ViewModel.RefreshCommand.CanExecute(null))
			{
				ViewModel.RefreshCommand.Execute(null);
			}
			e.Handled = true;
			return;
		}

		if (modifiers == ModifierKeys.None && e.Key == Key.Escape)
		{
			if (!string.IsNullOrEmpty(ViewModel.SearchQuery))
			{
				ViewModel.SearchQuery = string.Empty;
				e.Handled = true;
				return;
			}
			if (ViewModel.IsInspectorOpen)
			{
				ViewModel.IsInspectorOpen = false;
				e.Handled = true;
				return;
			}
			if (ViewModel.SelectedApp != null)
			{
				ViewModel.ClearSelectionCommand.Execute(null);
				e.Handled = true;
			}
			return;
		}

		bool isSearchFocused = SearchBox.IsKeyboardFocusWithin;
		bool isAppListFocused = AppsList.IsKeyboardFocused
			|| (Keyboard.FocusedElement is ListBoxItem focusedItem
				&& ItemsControl.ItemsControlFromItemContainer(focusedItem) == AppsList);
		if (!isSearchFocused && !isAppListFocused)
		{
			return;
		}

		if (modifiers == ModifierKeys.None && (e.Key == Key.Down || e.Key == Key.Up))
		{
			MoveSelectionByOffset(e.Key == Key.Down ? 1 : -1);
			e.Handled = true;
			return;
		}

		AppItemViewModel? selected = ViewModel.SelectedApp;
		if (selected == null)
		{
			return;
		}

		if (modifiers == ModifierKeys.Control && e.Key == Key.Return)
		{
			if (selected.OpenLocationCommand.CanExecute(null))
			{
				selected.OpenLocationCommand.Execute(null);
				e.Handled = true;
			}
			return;
		}

		if (modifiers == ModifierKeys.None && e.Key == Key.Return)
		{
			if (selected.LaunchCommand.CanExecute(null))
			{
				selected.LaunchCommand.Execute(null);
				e.Handled = true;
			}
			return;
		}

		if (!isAppListFocused)
		{
			return;
		}

		if (modifiers == ModifierKeys.None && e.Key == Key.F2)
		{
			if (selected.EditCommand.CanExecute(null))
			{
				selected.EditCommand.Execute(null);
				e.Handled = true;
			}
			return;
		}

		if (modifiers == ModifierKeys.None && e.Key == Key.Space)
		{
			ViewModel.ToggleInspectorCommand.Execute(null);
			e.Handled = true;
			return;
		}

		if (modifiers == ModifierKeys.None && e.Key == Key.Delete)
		{
			if (selected.RemoveCommand.CanExecute(null))
			{
				selected.RemoveCommand.Execute(null);
				e.Handled = true;
			}
		}
	}

	private void MoveSelectionByOffset(int offset)
	{
		List<AppItemViewModel> items = ViewModel.CurrentView.ToList();
		if (items.Count == 0)
		{
			return;
		}
		int currentIndex = ViewModel.SelectedApp != null ? items.IndexOf(ViewModel.SelectedApp) : -1;
		int nextIndex = currentIndex < 0
			? (offset > 0 ? 0 : items.Count - 1)
			: Math.Clamp(currentIndex + offset, 0, items.Count - 1);
		AppItemViewModel nextItem = items[nextIndex];
		ViewModel.SelectedApp = nextItem;
		AppsList.ScrollIntoView(nextItem);
	}

	private void OnListMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left || IsInsideButton(e.OriginalSource as DependencyObject))
		{
			return;
		}
		AppItemViewModel? item = GetItemFromPoint(AppsList, e.GetPosition(AppsList));
		if (item != null && item.LaunchCommand.CanExecute(null))
		{
			item.LaunchCommand.Execute(null);
			e.Handled = true;
		}
	}

	private void OnAddMenuClick(object sender, RoutedEventArgs e)
	{
		if (sender is Button button && button.ContextMenu != null)
		{
			button.ContextMenu.PlacementTarget = button;
			button.ContextMenu.IsOpen = true;
			e.Handled = true;
		}
	}

	private void OnAddClick(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "选择要添加的应用程序",
			Filter = "应用程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat",
			Multiselect = false
		};
		if (dialog.ShowDialog() == true)
		{
			AddItemsWithEdit(new[]
			{
				dialog.FileName
			});
		}
	}

	private void OnSearchHostMouseDown(object sender, MouseButtonEventArgs e)
	{
		SearchBox.Focus();
		Keyboard.Focus(SearchBox);
	}

	private void OnAddFolderClick(object sender, RoutedEventArgs e)
	{
		using FolderBrowserDialog dialog = new FolderBrowserDialog
		{
			Description = "选择要添加的文件夹",
			UseDescriptionForTitle = true
		};
		if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
		{
			AddItemsWithEdit(new[]
			{
				dialog.SelectedPath
			});
		}
	}

	private void OnEditRequested(AppItemViewModel vm)
	{
		EditAppDialog editDialog = new EditAppDialog(vm.Model, AppServices.Catalog.GetGroupNames(), isAddingNew: false)
		{
			Owner = Window.GetWindow(this)
		};
		if (editDialog.ShowDialog() == true)
		{
			AppServices.Catalog.UpdateApp(editDialog.EditedItem);
			vm.SyncFromModel();
			AppServices.StatusScheduler.RequestImmediateRefresh();
			ToastService.ShowSuccess($"已更新“{vm.DisplayName}”的配置。");
		}
	}

	private void OnRemoveRequested(AppItemViewModel vm)
	{
		ApplicationItem snapshot = vm.Model.Clone();
		string displayName = vm.DisplayName;
		AppServices.Catalog.RemoveApp(vm.Id);
		ViewModel.RemoveApp(vm.Id);
		AppServices.StatusScheduler.RequestImmediateRefresh();
		ToastService.ShowSuccess(
			$"已移除“{displayName}”",
			"撤销",
			delegate
			{
				ApplicationItem restored = AppServices.Catalog.RestoreApp(snapshot);
				int restoredIndex = AppServices.Catalog.GetAllApps().ToList().FindIndex(app => app.Id == restored.Id);
				ViewModel.RestoreApp(restored, restoredIndex);
				AppServices.StatusScheduler.RequestImmediateRefresh();
				ToastService.Show($"已恢复“{displayName}”");
			},
			durationMs: 5000);
	}

	private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (IsInsideButton(e.OriginalSource as DependencyObject))
		{
			_dragItem = null;
			_dragSourceItem = null;
			return;
		}
		_dragStartPoint = e.GetPosition(this);
		ListBox listBox = (ListBox)sender;
		Point point = e.GetPosition(listBox);
		_dragItem = GetItemFromPoint(listBox, point);
		_dragSourceItem = GetItemContainerFromPoint(listBox, point);
		if (_dragItem != null)
		{
			ViewModel.SelectedApp = _dragItem;
			listBox.SelectedItem = _dragItem;
		}
	}

	private static bool IsInsideButton(DependencyObject? element)
	{
		while (element != null && element is not Button)
		{
			element = VisualTreeHelper.GetParent(element);
		}
		return element is Button;
	}

	private void OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed && _dragItem != null && ViewModel.CanReorder)
		{
			Point position = e.GetPosition(this);
			if (!(Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance) || !(Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance))
			{
				if (_dragSourceItem != null)
				{
					_dragSourceItem.Tag = "Dragging";
				}
				DataObject data = new DataObject(typeof(AppItemViewModel), _dragItem);
				DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
				ClearDragVisualState();
			}
		}
	}

	private void OnDrop(object sender, DragEventArgs e)
	{
		if (TryHandleExternalDrop(e))
		{
			ClearDragVisualState();
			return;
		}
		if (sender is not ListBox listBox)
		{
			ClearDragVisualState();
			return;
		}
		AppItemViewModel? target = GetItemFromPoint(listBox, e.GetPosition(listBox));
		CommitDrop(target, e);
	}

	private void OnListDragEnter(object sender, DragEventArgs e)
	{
		HandleListDragHint(e);
	}

	private void OnListDragOver(object sender, DragEventArgs e)
	{
		HandleListDragHint(e);
	}

	private void OnItemDragEnter(object sender, DragEventArgs e)
	{
		AppItemViewModel? dragItem = ResolveDragItem(e);
		if (ViewModel.CanReorder && dragItem != null && sender is ListBoxItem item && item.DataContext != dragItem)
		{
			UpdateInsertIndicator(item, dragItem, e);
			e.Handled = true;
		}
	}

	private void OnItemDragOver(object sender, DragEventArgs e)
	{
		if (HasExternalFileDrop(e))
		{
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
			return;
		}
		AppItemViewModel? dragItem = ResolveDragItem(e);
		if (ViewModel.CanReorder && dragItem != null && sender is ListBoxItem item && item.DataContext != dragItem)
		{
			UpdateInsertIndicator(item, dragItem, e);
			e.Effects = DragDropEffects.Move;
			e.Handled = true;
		}
	}

	private void OnItemDragLeave(object sender, DragEventArgs e)
	{
		if (sender is ListBoxItem item && item == _dragOverItem)
		{
			item.Tag = string.Empty;
			_dragOverItem = null;
			_dragOverPosition = string.Empty;
		}
	}

	private void OnItemDrop(object sender, DragEventArgs e)
	{
		if (TryHandleExternalDrop(e))
		{
			ClearDragVisualState();
			return;
		}
		if (sender is not ListBoxItem item)
		{
			return;
		}
		if (FindParentListBox(item) == null)
		{
			ClearDragVisualState();
			return;
		}
		CommitDrop(item.DataContext as AppItemViewModel, e);
	}

	private static AppItemViewModel? GetItemFromPoint(ListBox listBox, Point point)
	{
		IInputElement inputElement = listBox.InputHitTest(point);
		DependencyObject? element = inputElement as DependencyObject;
		while (element != null && element is not ListBoxItem)
		{
			element = VisualTreeHelper.GetParent(element);
		}
		if (element is ListBoxItem item)
		{
			return item.DataContext as AppItemViewModel;
		}
		return null;
	}

	private static ListBoxItem? GetItemContainerFromPoint(ListBox listBox, Point point)
	{
		IInputElement inputElement = listBox.InputHitTest(point);
		DependencyObject? element = inputElement as DependencyObject;
		while (element != null && element is not ListBoxItem)
		{
			element = VisualTreeHelper.GetParent(element);
		}
		return element as ListBoxItem;
	}

	private void ClearDragVisualState()
	{
		if (_dragSourceItem != null)
		{
			_dragSourceItem.Tag = string.Empty;
			_dragSourceItem = null;
		}
		if (_dragOverItem != null)
		{
			_dragOverItem.Tag = string.Empty;
			_dragOverItem = null;
		}
		_dragOverPosition = string.Empty;
		_dragItem = null;
	}

	private void SetDragOverItem(ListBoxItem item, AppItemViewModel dragItem, string position)
	{
		if (item.DataContext != dragItem)
		{
			if (_dragOverItem != null && _dragOverItem != item)
			{
				_dragOverItem.Tag = string.Empty;
			}
			_dragOverItem = item;
			if (!string.Equals(_dragOverItem.Tag as string, position, StringComparison.Ordinal))
			{
				_dragOverItem.Tag = position;
			}
			_dragOverPosition = position;
		}
	}

	private AppItemViewModel? ResolveDragItem(DragEventArgs e)
	{
		object data = e.Data.GetData(typeof(AppItemViewModel));
		if (data is AppItemViewModel app)
		{
			return app;
		}
		return _dragItem;
	}

	private static bool HasExternalFileDrop(DragEventArgs e)
	{
		if (e.Data.GetDataPresent(typeof(AppItemViewModel)))
		{
			return false;
		}
		return e.Data.GetDataPresent(DataFormats.FileDrop);
	}

	private void HandleListDragHint(DragEventArgs e)
	{
		if (HasExternalFileDrop(e))
		{
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
			return;
		}
		if (ResolveDragItem(e) != null && ViewModel.CanReorder)
		{
			e.Effects = DragDropEffects.Move;
			e.Handled = true;
			return;
		}
		e.Effects = DragDropEffects.None;
	}

	private bool TryHandleExternalDrop(DragEventArgs e)
	{
		if (!HasExternalFileDrop(e))
		{
			return false;
		}
		if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
		{
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return true;
		}
		bool added = AddItemsWithEdit(paths);
		e.Effects = added ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
		return true;
	}

	private bool AddItemsWithEdit(IEnumerable<string> rawPaths)
	{
		List<string> inputPaths = rawPaths.Where((string path) => !string.IsNullOrWhiteSpace(path)).Select((string path) => path.Trim()).ToList();
		IReadOnlyList<string> supportedPaths = AppServices.Catalog.GetSupportedInputPaths(inputPaths);
		if (supportedPaths.Count == 0)
		{
			ToastService.ShowWarning("仅支持添加 .exe、.lnk、.bat 或文件夹。");
			return false;
		}
		Window? owner = Window.GetWindow(this);
		List<ApplicationItem> addedItems = new List<ApplicationItem>();
		try
		{
			foreach (string path in supportedPaths)
			{
				ApplicationItem item = AppServices.Catalog.AddApp(path);
				addedItems.Add(item);
				EditAppDialog editDialog = new EditAppDialog(item, AppServices.Catalog.GetGroupNames(), isAddingNew: true)
				{
					Owner = owner
				};
				if (editDialog.ShowDialog() != true)
				{
					AppServices.Catalog.RemoveApp(item.Id);
					addedItems.Remove(item);
					continue;
				}
				AppServices.Catalog.UpdateApp(editDialog.EditedItem);
			}
			ViewModel.AddApps(addedItems);
			AppServices.StatusScheduler.RequestImmediateRefresh();
			if (addedItems.Count == 0)
			{
				return false;
			}
			if (supportedPaths.Count != inputPaths.Count)
			{
				ToastService.ShowWarning($"已添加 {addedItems.Count} 项，部分不支持或重复的路径已忽略。");
			}
			else
			{
				ToastService.ShowSuccess(addedItems.Count == 1
					? $"已添加“{addedItems[0].DisplayName}”。"
					: $"已成功添加 {addedItems.Count} 个应用。");
			}
			return true;
		}
		catch (Exception ex)
		{
			RollbackAddedItems(addedItems);
			ToastService.ShowError("添加失败：" + ex.Message);
			return false;
		}
	}

	private void RollbackAddedItems(IReadOnlyList<ApplicationItem> addedItems)
	{
		foreach (ApplicationItem item in addedItems)
		{
			AppServices.Catalog.RemoveApp(item.Id);
		}
		foreach (ApplicationItem item in addedItems)
		{
			ViewModel.RemoveApp(item.Id);
		}
	}

	private void CommitDrop(AppItemViewModel? target, DragEventArgs e)
	{
		AppItemViewModel? dragItem = ResolveDragItem(e);
		if (dragItem == null)
		{
			ClearDragVisualState();
			return;
		}
		bool insertAfter = string.Equals(_dragOverPosition, "Bottom", StringComparison.Ordinal);
		bool handled = ViewModel.TryReorderApp(dragItem, target, insertAfter);
		ClearDragVisualState();
		if (handled)
		{
			e.Handled = true;
		}
	}

	private static ListBox? FindParentListBox(DependencyObject child)
	{
		DependencyObject? current = child;
		while (current != null && current is not ListBox)
		{
			current = VisualTreeHelper.GetParent(current);
		}
		return current as ListBox;
	}

	private void UpdateInsertIndicator(ListBoxItem item, AppItemViewModel dragItem, DragEventArgs e)
	{
		Point position = e.GetPosition(item);
		bool isTop = position.Y <= item.ActualHeight / 2.0;
		SetDragOverItem(item, dragItem, isTop ? "Top" : "Bottom");
	}
}
