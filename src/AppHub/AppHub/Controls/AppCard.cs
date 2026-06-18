using System.Windows;
using System.Windows.Controls;

namespace AppHub.Controls;

public partial class AppCard : UserControl
{
	public AppCard()
	{
		InitializeComponent();
	}

	private void OnMoreClick(object sender, RoutedEventArgs e)
	{
		if (sender is Button button && button.ContextMenu != null)
		{
			button.ContextMenu.PlacementTarget = button;
			button.ContextMenu.IsOpen = true;
			e.Handled = true;
		}
	}
}
