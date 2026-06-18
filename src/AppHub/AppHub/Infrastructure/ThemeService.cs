using System.Windows;
using System.Windows.Media;

namespace AppHub.Infrastructure;

public static class ThemeService
{
	public static void ApplyTheme(bool isDark)
	{
		ResourceDictionary resources = Application.Current?.Resources;
		if (resources != null)
		{
			if (isDark)
			{
				UpdateBrush(resources, "AppBgBrush", "#06101D");
				UpdateBrush(resources, "PageRootBgBrush", "#06101D");
				UpdateBrush(resources, "CardBgBrush", "#101A2B");
				UpdateBrush(resources, "SidebarBgBrush", "#081321");
				UpdateBrush(resources, "ProgramCardBgBrush", "#101A2B");
				UpdateBrush(resources, "PrimaryBrush", "#2563EB");
				UpdateBrush(resources, "PrimaryHoverBrush", "#2F87FF");
				UpdateBrush(resources, "PrimaryPressedBrush", "#1D4ED8");
				UpdateBrush(resources, "AccentBrush", "#2F87FF");
				UpdateBrush(resources, "SuccessBrush", "#30D16F");
				UpdateBrush(resources, "TextBrush", "#EAF2FF");
				UpdateBrush(resources, "SubtleTextBrush", "#8EA2BF");
				UpdateBrush(resources, "BorderBrush", "#29384E");
				UpdateBrush(resources, "HoverBgBrush", "#172842");
				UpdateBrush(resources, "DisabledBgBrush", "#1F2937");
				UpdateBrush(resources, "DisabledBorderBrush", "#334155");
				UpdateBrush(resources, "DisabledTextBrush", "#64748B");
				UpdateBrush(resources, "ScrollBarTrackBrush", "#00FFFFFF");
				UpdateBrush(resources, "ScrollBarThumbBrush", "#47FFFFFF");
				UpdateBrush(resources, "ScrollBarThumbHoverBrush", "#80FFFFFF");
				UpdateBrush(resources, "ScrollBarThumbPressedBrush", "#9EFFFFFF");
			}
			else
			{
				UpdateBrush(resources, "AppBgBrush", "#FAFAFA");
				UpdateBrush(resources, "PageRootBgBrush", "#FAFAFA");
				UpdateBrush(resources, "CardBgBrush", "#FFFFFF");
				UpdateBrush(resources, "SidebarBgBrush", "#FFFFFF");
				UpdateBrush(resources, "ProgramCardBgBrush", "#FFFFFF");
				UpdateBrush(resources, "PrimaryBrush", "#0F172A");
				UpdateBrush(resources, "PrimaryHoverBrush", "#1E293B");
				UpdateBrush(resources, "PrimaryPressedBrush", "#334155");
				UpdateBrush(resources, "AccentBrush", "#0EA5E9");
				UpdateBrush(resources, "SuccessBrush", "#22C55E");
				UpdateBrush(resources, "TextBrush", "#1E293B");
				UpdateBrush(resources, "SubtleTextBrush", "#6B86B1");
				UpdateBrush(resources, "BorderBrush", "#E8E8E8");
				UpdateBrush(resources, "HoverBgBrush", "#F1F5F9");
				UpdateBrush(resources, "DisabledBgBrush", "#CBD5E1");
				UpdateBrush(resources, "DisabledBorderBrush", "#E2E8F0");
				UpdateBrush(resources, "DisabledTextBrush", "#94A3B8");
				UpdateBrush(resources, "ScrollBarTrackBrush", "#00FFFFFF");
				UpdateBrush(resources, "ScrollBarThumbBrush", "#33000000");
				UpdateBrush(resources, "ScrollBarThumbHoverBrush", "#66000000");
				UpdateBrush(resources, "ScrollBarThumbPressedBrush", "#8C000000");
			}
		}
	}

	private static void UpdateBrush(ResourceDictionary resources, string key, string colorHex)
	{
		Color color = (Color)ColorConverter.ConvertFromString(colorHex);
		if (resources[key] is SolidColorBrush brush)
		{
			if (!((Freezable)brush).IsFrozen)
			{
				brush.Color = color;
				return;
			}
			SolidColorBrush clone = brush.Clone();
			clone.Color = color;
			resources[key] = clone;
		}
		else
		{
			resources[key] = new SolidColorBrush(color);
		}
	}
}
