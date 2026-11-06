using System;
using System.Windows;
using System.Windows.Controls;
using AppHub.Infrastructure;

namespace AppHub.Views.Dialogs;

public sealed class LaunchArgumentsDialog : Window
{
	private readonly TextBox _argumentsBox;

	public string Arguments => _argumentsBox.Text;

	public LaunchArgumentsDialog(string appName, string arguments)
	{
		Title = "带参数启动";
		Width = 460;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		ShowInTaskbar = false;
		UseLayoutRounding = true;
		SetResourceReference(BackgroundProperty, "AppBgBrush");
		StackPanel content = new StackPanel { Margin = new Thickness(22) };
		TextBlock title = new TextBlock
		{
			Text = appName,
			FontSize = 15,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, 0, 0, 16),
			TextWrapping = TextWrapping.Wrap
		};
		title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
		content.Children.Add(title);
		TextBlock label = new TextBlock { Text = "启动参数", Margin = new Thickness(0, 0, 0, 6) };
		label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
		content.Children.Add(label);
		_argumentsBox = new TextBox { Text = arguments, MinWidth = 300 };
		content.Children.Add(_argumentsBox);
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 18, 0, 0)
		};
		Button launchButton = new Button { Content = "启动", IsDefault = true, Padding = new Thickness(20, 9, 20, 9) };
		launchButton.Click += delegate { DialogResult = true; };
		Button cancelButton = new Button
		{
			Content = "取消",
			IsCancel = true,
			Padding = new Thickness(20, 9, 20, 9),
			Margin = new Thickness(10, 0, 0, 0)
		};
		cancelButton.SetResourceReference(StyleProperty, "SecondaryButton");
		buttons.Children.Add(launchButton);
		buttons.Children.Add(cancelButton);
		content.Children.Add(buttons);
		Content = content;
		SourceInitialized += delegate { ThemeService.ApplyWindowChromeTheme(this, AppServices.Config.Settings.IsDarkMode); };
		Loaded += delegate
		{
			_argumentsBox.Focus();
			_argumentsBox.SelectAll();
		};
	}
}
