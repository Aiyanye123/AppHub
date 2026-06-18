using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using AppHub.Infrastructure;

namespace AppHub;

public partial class MainWindow : Window
{
	private const double ExpandedSidebarWidth = 216;

	private const double CollapsedSidebarWidth = 104;

	private bool _allowClose;

	private bool _isSidebarCollapsed;

	private FrameworkElement[] SidebarTextElements => new FrameworkElement[]
	{
		BrandCopy,
		OverviewNavText,
		SettingsNavText,
		SidebarToggleText
	};

	public MainWindow()
	{
		InitializeComponent();
		base.Closing += OnClosing;
		BackgroundEffectService.Apply(this, AppServices.Config.Settings);
	}

	public void RequestClose()
	{
		_allowClose = true;
		Close();
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (!_allowClose)
		{
			e.Cancel = true;
			App.TrayIconService?.HideToTray();
		}
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
		BrandContainer.Margin = _isSidebarCollapsed ? new Thickness(20, 24, 20, 28) : new Thickness(18, 24, 18, 28);
		BrandRow.HorizontalAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
		MainTabControl.Margin = _isSidebarCollapsed ? new Thickness(16, 0, 16, 24) : new Thickness(10, 0, 10, 24);
		SidebarToggleButton.Margin = _isSidebarCollapsed ? new Thickness(22, 0, 22, 20) : new Thickness(18, 0, 18, 20);
		SidebarToggleIcon.Text = _isSidebarCollapsed ? "\uE76C" : "\uE76B";
		SidebarToggleButton.ToolTip = _isSidebarCollapsed ? "\u5c55\u5f00\u4fa7\u8fb9\u680f" : "\u6536\u8d77\u4fa7\u8fb9\u680f";
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
}
