using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AppHub.Controls;

public partial class AppCard : UserControl
{
	public AppCard()
	{
		InitializeComponent();
	}

	private void OnMoreClick(object sender, RoutedEventArgs e)
	{
		if (sender is Button button && base.ContextMenu != null)
		{
			base.ContextMenu.PlacementTarget = button;
			base.ContextMenu.Placement = PlacementMode.Bottom;
			base.ContextMenu.DataContext = base.DataContext;
			base.ContextMenu.IsOpen = true;
			e.Handled = true;
		}
	}
}
