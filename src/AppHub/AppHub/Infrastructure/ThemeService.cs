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
				UpdateBrush(resources, "AppBgBrush", "#0B1220");
				UpdateBrush(resources, "PageRootBgBrush", "#0B1220");
				UpdateBrush(resources, "CardBgBrush", "#0F172A");
				UpdateBrush(resources, "SidebarBgBrush", "#0F172A");
				UpdateBrush(resources, "ProgramCardBgBrush", "#0F172A");
				UpdateBrush(resources, "PrimaryBrush", "#1E293B");
				UpdateBrush(resources, "PrimaryHoverBrush", "#334155");
				UpdateBrush(resources, "PrimaryPressedBrush", "#475569");
				UpdateBrush(resources, "AccentBrush", "#38BDF8");
				UpdateBrush(resources, "SuccessBrush", "#22C55E");
				UpdateBrush(resources, "TextBrush", "#E2E8F0");
				UpdateBrush(resources, "SubtleTextBrush", "#94A3B8");
				UpdateBrush(resources, "BorderBrush", "#1F2937");
				UpdateBrush(resources, "HoverBgBrush", "#111827");
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
