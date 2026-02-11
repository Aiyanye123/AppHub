using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
	private Point _dragStartPoint;

	private AppItemViewModel? _dragItem;

	private ListBoxItem? _dragSourceItem;

	private ListBoxItem? _dragOverItem;

	private string _dragOverPosition = string.Empty;

	private OverviewViewModel ViewModel => (OverviewViewModel)base.DataContext;

	public OverviewView()
	{
		InitializeComponent();
		if (base.DataContext is OverviewViewModel vm)
		{
			vm.EditRequested += OnEditRequested;
			vm.RemoveRequested += OnRemoveRequested;
		}
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		AppServices.StatusScheduler.RequestImmediateRefresh();
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
	}

	private void OnAddClick(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "应用程序 (*.exe;*.lnk)|*.exe;*.lnk",
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
		EditAppDialog editDialog = new EditAppDialog(vm.Model)
		{
			Owner = Window.GetWindow((DependencyObject)(object)this)
		};
		if (editDialog.ShowDialog() == true)
		{
			AppServices.Catalog.UpdateApp(editDialog.EditedItem);
			vm.SyncFromModel();
			AppServices.StatusScheduler.RequestImmediateRefresh();
		}
	}

	private void OnRemoveRequested(AppItemViewModel vm)
	{
		if (MessageBox.Show($"\u786e\u8ba4\u79fb\u9664 \"{vm.DisplayName}\" \u5417\uff1f", "\u79fb\u9664\u5e94\u7528", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
		{
			AppServices.Catalog.RemoveApp(vm.Id);
			ViewModel.LoadApps();
			AppServices.StatusScheduler.RequestImmediateRefresh();
		}
	}

	private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_dragStartPoint = e.GetPosition(this);
		ListBox listBox = (ListBox)sender;
		Point point = e.GetPosition(listBox);
		_dragItem = GetItemFromPoint(listBox, point);
		_dragSourceItem = GetItemContainerFromPoint(listBox, point);
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
		CommitDrop(listBox, target, e);
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
		if (FindParentListBox(item) is not ListBox listBox)
		{
			ClearDragVisualState();
			return;
		}
		CommitDrop(listBox, item.DataContext as AppItemViewModel, e);
	}

	private static AppItemViewModel? GetItemFromPoint(ListBox listBox, Point point)
	{
		IInputElement inputElement = listBox.InputHitTest(point);
		DependencyObject? element = (DependencyObject)((inputElement is DependencyObject) ? inputElement : null);
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
		DependencyObject? element = (DependencyObject)((inputElement is DependencyObject) ? inputElement : null);
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
			MessageBox.Show("仅支持添加 .exe、.lnk 或文件夹。", "无法添加", MessageBoxButton.OK, MessageBoxImage.Information);
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
				EditAppDialog editDialog = new EditAppDialog(item)
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
			ViewModel.LoadApps();
			AppServices.StatusScheduler.RequestImmediateRefresh();
			if (addedItems.Count == 0)
			{
				return false;
			}
			if (supportedPaths.Count != inputPaths.Count)
			{
				MessageBox.Show($"已添加 {addedItems.Count} 项，部分不支持或重复的路径已忽略。", "添加完成", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			return true;
		}
		catch (Exception ex)
		{
			foreach (ApplicationItem item2 in addedItems)
			{
				AppServices.Catalog.RemoveApp(item2.Id);
			}
			ViewModel.LoadApps();
			MessageBox.Show("添加失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
			return false;
		}
	}

	private void CommitDrop(ListBox listBox, AppItemViewModel? target, DragEventArgs e)
	{
		AppItemViewModel? dragItem = ResolveDragItem(e);
		if (dragItem == null || !ViewModel.CanReorder)
		{
			ClearDragVisualState();
			return;
		}
		if (target == null)
		{
			int lastIndex = ViewModel.Apps.Count - 1;
			int sourceIndex = ViewModel.Apps.IndexOf(dragItem);
			if (sourceIndex >= 0 && sourceIndex != lastIndex)
			{
				ViewModel.Apps.Move(sourceIndex, lastIndex);
				ViewModel.CommitReorder();
			}
			ClearDragVisualState();
			e.Handled = true;
			return;
		}
		if (target == dragItem)
		{
			ClearDragVisualState();
			e.Handled = true;
			return;
		}
		int targetIndex = ViewModel.Apps.IndexOf(target);
		int fromIndex = ViewModel.Apps.IndexOf(dragItem);
		if (targetIndex < 0 || fromIndex < 0)
		{
			ClearDragVisualState();
			return;
		}
		if (string.Equals(_dragOverPosition, "Bottom", StringComparison.Ordinal))
		{
			targetIndex++;
		}
		if (targetIndex > fromIndex)
		{
			targetIndex--;
		}
		targetIndex = Math.Clamp(targetIndex, 0, ViewModel.Apps.Count - 1);
		if (fromIndex != targetIndex)
		{
			ViewModel.Apps.Move(fromIndex, targetIndex);
			ViewModel.CommitReorder();
		}
		ClearDragVisualState();
		e.Handled = true;
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
