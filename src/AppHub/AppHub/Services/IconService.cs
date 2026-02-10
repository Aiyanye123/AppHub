using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppHub.Helpers;
using AppHub.Infrastructure;
using AppHub.Models;

namespace AppHub.Services;

public sealed class IconService
{
	private readonly AppLogger _logger;

	public IconService(AppLogger logger)
	{
		_logger = logger;
	}

	public ImageSource? GetIcon(ApplicationItem app)
	{
		string cachePath = GetCachePath(app.Id);
		if (app.IconSource == IconSource.Custom && !string.IsNullOrWhiteSpace(app.CustomIconPath) && File.Exists(app.CustomIconPath))
		{
			return LoadBitmap(app.CustomIconPath);
		}
		if (File.Exists(cachePath))
		{
			return LoadBitmap(cachePath);
		}
		if (string.IsNullOrWhiteSpace(app.TargetPath) || !File.Exists(app.TargetPath))
		{
			return null;
		}
		try
		{
			byte[] bytes = ExtractIcon(app.TargetPath);
			if (bytes.Length == 0)
			{
				return null;
			}
			PathHelper.EnsureDirectory(PathHelper.GetIconCacheDirectory());
			File.WriteAllBytes(cachePath, bytes);
			return LoadBitmap(cachePath);
		}
		catch (Exception ex)
		{
			_logger.Warn("Icon extract failed: " + ex.Message);
			return null;
		}
	}

	public byte[] ExtractIcon(string path)
	{
		using Icon icon = Icon.ExtractAssociatedIcon(path);
		if (icon == null)
		{
			return Array.Empty<byte>();
		}
		using Bitmap bmp = icon.ToBitmap();
		using MemoryStream stream = new MemoryStream();
		bmp.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	public void SetCustomIcon(ApplicationItem app, string path)
	{
		app.IconSource = IconSource.Custom;
		app.CustomIconPath = path;
	}

	public void ClearCache()
	{
		string dir = PathHelper.GetIconCacheDirectory();
		if (Directory.Exists(dir))
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	private static string GetCachePath(Guid id)
	{
		return Path.Combine(PathHelper.GetIconCacheDirectory(), $"{id}.png");
	}

	private static BitmapImage LoadBitmap(string path)
	{
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(path, UriKind.Absolute);
		bitmapImage.EndInit();
		((Freezable)bitmapImage).Freeze();
		return bitmapImage;
	}
}
