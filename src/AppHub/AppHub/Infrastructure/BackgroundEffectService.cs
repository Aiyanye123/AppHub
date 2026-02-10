using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using AppHub.Models;

namespace AppHub.Infrastructure;

public static class BackgroundEffectService
{
	private const int DefaultStrength = 40;

	public static void Apply(Window? window, AppSettings? settings)
	{
		if (window == null || settings == null)
		{
			return;
		}
		if (!TryFindWindowElements(window, out Grid? imageLayer, out Border? overlayLayer))
		{
			return;
		}
		ApplyPanelOpacity(settings);
		BackgroundConfig config = GetActiveConfig(settings);
		bool hasImageBackground = CanRender(config);
		ApplyScrollBarStyle(settings.IsDarkMode, hasImageBackground);
		if (!hasImageBackground)
		{
			ClearLayers(imageLayer, overlayLayer);
			SetPageRootTransparent(isTransparent: false);
			return;
		}
		int strength = NormalizeStrength(config.Strength);
		imageLayer.Visibility = Visibility.Visible;
		imageLayer.Opacity = 1.0;
		imageLayer.Background = CreateImageBrush(config.ImagePath);
		imageLayer.Effect = null;
		overlayLayer.Visibility = Visibility.Visible;
		switch (config.Style)
		{
		case BackgroundStyle.ImageOnly:
			overlayLayer.Background = Brushes.Transparent;
			overlayLayer.Effect = null;
			break;
		case BackgroundStyle.FrostedGlass:
			imageLayer.Effect = new BlurEffect
			{
				Radius = 1.0 + strength / 100.0 * 16.0
			};
			overlayLayer.Background = new SolidColorBrush(Color.FromArgb((byte)(18 + strength), 255, 255, 255));
			overlayLayer.Effect = null;
			break;
		case BackgroundStyle.Dimmed:
			overlayLayer.Background = new SolidColorBrush(Color.FromArgb((byte)(18 + strength), 10, 18, 30));
			overlayLayer.Effect = null;
			break;
		default:
			overlayLayer.Background = Brushes.Transparent;
			overlayLayer.Effect = null;
			break;
		}
		SetPageRootTransparent(isTransparent: true);
	}

	public static void Reset(Window? window, AppSettings settings, bool isDarkMode)
	{
		if (isDarkMode)
		{
			settings.DarkBackgroundImagePath = string.Empty;
			settings.DarkBackgroundStyle = BackgroundStyle.None;
			settings.DarkBackgroundEffectStrength = DefaultStrength;
		}
		else
		{
			settings.LightBackgroundImagePath = string.Empty;
			settings.LightBackgroundStyle = BackgroundStyle.None;
			settings.LightBackgroundEffectStrength = DefaultStrength;
		}
		Apply(window, settings);
	}

	private static BackgroundConfig GetActiveConfig(AppSettings settings)
	{
		if (settings.IsDarkMode)
		{
			return new BackgroundConfig(settings.DarkBackgroundImagePath ?? string.Empty, settings.DarkBackgroundStyle, settings.DarkBackgroundEffectStrength);
		}
		return new BackgroundConfig(settings.LightBackgroundImagePath ?? string.Empty, settings.LightBackgroundStyle, settings.LightBackgroundEffectStrength);
	}

	private static bool CanRender(BackgroundConfig config)
	{
		return config.Style != BackgroundStyle.None && !string.IsNullOrWhiteSpace(config.ImagePath) && File.Exists(config.ImagePath);
	}

	private static bool TryFindWindowElements(Window window, out Grid? imageLayer, out Border? overlayLayer)
	{
		imageLayer = window.FindName("BackgroundImageLayer") as Grid;
		overlayLayer = window.FindName("BackgroundOverlayLayer") as Border;
		return imageLayer != null && overlayLayer != null;
	}

	private static void ClearLayers(Grid imageLayer, Border overlayLayer)
	{
		imageLayer.Background = null;
		imageLayer.Effect = null;
		imageLayer.Visibility = Visibility.Collapsed;
		overlayLayer.Background = Brushes.Transparent;
		overlayLayer.Effect = null;
		overlayLayer.Visibility = Visibility.Collapsed;
	}

	private static void SetPageRootTransparent(bool isTransparent)
	{
		ResourceDictionary? resources = Application.Current?.Resources;
		if (resources == null)
		{
			return;
		}
		if (resources.Contains("PageRootBgBrush") && resources["PageRootBgBrush"] is SolidColorBrush brush)
		{
			Color target = isTransparent ? Colors.Transparent : ResolveAppBgColor(resources);
			if (!brush.IsFrozen)
			{
				brush.Color = target;
			}
			else
			{
				SolidColorBrush clone = brush.Clone();
				clone.Color = target;
				resources["PageRootBgBrush"] = clone;
			}
			return;
		}
		resources["PageRootBgBrush"] = new SolidColorBrush(isTransparent ? Colors.Transparent : ResolveAppBgColor(resources));
	}

	private static Color ResolveAppBgColor(ResourceDictionary resources)
	{
		if (resources.Contains("AppBgBrush") && resources["AppBgBrush"] is SolidColorBrush appBg)
		{
			return appBg.Color;
		}
		return (Color)ColorConverter.ConvertFromString("#FAFAFA");
	}

	private static void ApplyPanelOpacity(AppSettings settings)
	{
		ResourceDictionary? resources = Application.Current?.Resources;
		if (resources == null)
		{
			return;
		}
		int sidebarOpacity = settings.IsDarkMode ? NormalizeOpacity(settings.DarkSidebarOpacity, fallback: 88) : NormalizeOpacity(settings.LightSidebarOpacity, fallback: 92);
		int programOpacity = settings.IsDarkMode ? NormalizeOpacity(settings.DarkProgramOpacity, fallback: 88) : NormalizeOpacity(settings.LightProgramOpacity, fallback: 92);
		Color baseColor = ResolveCardBgColor(resources, settings.IsDarkMode);
		SetBrushWithOpacity(resources, "SidebarBgBrush", baseColor, sidebarOpacity);
		SetBrushWithOpacity(resources, "ProgramCardBgBrush", baseColor, programOpacity);
	}

	private static void ApplyScrollBarStyle(bool isDarkMode, bool hasImageBackground)
	{
		ResourceDictionary? resources = Application.Current?.Resources;
		if (resources == null)
		{
			return;
		}
		UpdateAlphaBrush(resources, "ScrollBarTrackBrush", 255, 255, 255, 0);
		if (hasImageBackground || isDarkMode)
		{
			UpdateAlphaBrush(resources, "ScrollBarThumbBrush", 255, 255, 255, 72);
			UpdateAlphaBrush(resources, "ScrollBarThumbHoverBrush", 255, 255, 255, 128);
			UpdateAlphaBrush(resources, "ScrollBarThumbPressedBrush", 255, 255, 255, 158);
			return;
		}
		UpdateAlphaBrush(resources, "ScrollBarThumbBrush", 0, 0, 0, 51);
		UpdateAlphaBrush(resources, "ScrollBarThumbHoverBrush", 0, 0, 0, 102);
		UpdateAlphaBrush(resources, "ScrollBarThumbPressedBrush", 0, 0, 0, 140);
	}

	private static Color ResolveCardBgColor(ResourceDictionary resources, bool isDark)
	{
		if (resources.Contains("CardBgBrush") && resources["CardBgBrush"] is SolidColorBrush cardBrush)
		{
			return Color.FromRgb(cardBrush.Color.R, cardBrush.Color.G, cardBrush.Color.B);
		}
		return (Color)ColorConverter.ConvertFromString(isDark ? "#0F172A" : "#FFFFFF");
	}

	private static void SetBrushWithOpacity(ResourceDictionary resources, string key, Color baseColor, int opacityPercent)
	{
		byte alpha = (byte)Math.Clamp((int)Math.Round(opacityPercent * 2.55), 0, 255);
		Color target = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
		if (resources.Contains(key) && resources[key] is SolidColorBrush brush)
		{
			if (!brush.IsFrozen)
			{
				brush.Color = target;
			}
			else
			{
				SolidColorBrush clone = brush.Clone();
				clone.Color = target;
				resources[key] = clone;
			}
			return;
		}
		resources[key] = new SolidColorBrush(target);
	}

	private static void UpdateAlphaBrush(ResourceDictionary resources, string key, byte r, byte g, byte b, byte a)
	{
		Color target = Color.FromArgb(a, r, g, b);
		if (resources.Contains(key) && resources[key] is SolidColorBrush brush)
		{
			if (!brush.IsFrozen)
			{
				brush.Color = target;
			}
			else
			{
				SolidColorBrush clone = brush.Clone();
				clone.Color = target;
				resources[key] = clone;
			}
			return;
		}
		resources[key] = new SolidColorBrush(target);
	}

	private static ImageBrush CreateImageBrush(string imagePath)
	{
		BitmapImage image = new BitmapImage();
		image.BeginInit();
		image.UriSource = new Uri(imagePath, UriKind.Absolute);
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
		image.EndInit();
		image.Freeze();
		return new ImageBrush(image)
		{
			Stretch = Stretch.UniformToFill,
			AlignmentX = AlignmentX.Center,
			AlignmentY = AlignmentY.Center
		};
	}

	private static int NormalizeStrength(int strength)
	{
		if (strength < 0 || strength > 100)
		{
			return DefaultStrength;
		}
		return strength;
	}

	private static int NormalizeOpacity(int opacity, int fallback)
	{
		if (opacity < 0 || opacity > 100)
		{
			return fallback;
		}
		return opacity;
	}

	private readonly record struct BackgroundConfig(string ImagePath, BackgroundStyle Style, int Strength);
}
