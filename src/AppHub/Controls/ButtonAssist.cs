using System.Windows;
using System.Windows.Media;

namespace AppHub.Controls;

public static class ButtonAssist
{
	public static readonly DependencyProperty HoverBackgroundProperty =
		DependencyProperty.RegisterAttached(
			"HoverBackground",
			typeof(Brush),
			typeof(ButtonAssist),
			new FrameworkPropertyMetadata(null));

	public static readonly DependencyProperty PressedBackgroundProperty =
		DependencyProperty.RegisterAttached(
			"PressedBackground",
			typeof(Brush),
			typeof(ButtonAssist),
			new FrameworkPropertyMetadata(null));

	public static readonly DependencyProperty HoverBorderBrushProperty =
		DependencyProperty.RegisterAttached(
			"HoverBorderBrush",
			typeof(Brush),
			typeof(ButtonAssist),
			new FrameworkPropertyMetadata(null));

	public static readonly DependencyProperty CornerRadiusProperty =
		DependencyProperty.RegisterAttached(
			"CornerRadius",
			typeof(CornerRadius),
			typeof(ButtonAssist),
			new FrameworkPropertyMetadata(new CornerRadius(8)));

	public static Brush? GetHoverBackground(DependencyObject obj) => (Brush?)obj.GetValue(HoverBackgroundProperty);
	public static void SetHoverBackground(DependencyObject obj, Brush? value) => obj.SetValue(HoverBackgroundProperty, value);

	public static Brush? GetPressedBackground(DependencyObject obj) => (Brush?)obj.GetValue(PressedBackgroundProperty);
	public static void SetPressedBackground(DependencyObject obj, Brush? value) => obj.SetValue(PressedBackgroundProperty, value);

	public static Brush? GetHoverBorderBrush(DependencyObject obj) => (Brush?)obj.GetValue(HoverBorderBrushProperty);
	public static void SetHoverBorderBrush(DependencyObject obj, Brush? value) => obj.SetValue(HoverBorderBrushProperty, value);

	public static CornerRadius GetCornerRadius(DependencyObject obj) => (CornerRadius)obj.GetValue(CornerRadiusProperty);
	public static void SetCornerRadius(DependencyObject obj, CornerRadius value) => obj.SetValue(CornerRadiusProperty, value);
}
