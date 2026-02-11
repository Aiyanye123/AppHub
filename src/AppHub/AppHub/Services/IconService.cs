using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppHub.Helpers;
using AppHub.Infrastructure;
using AppHub.Models;

namespace AppHub.Services;

public sealed class IconService
{
	private const uint ShgfiIcon = 0x100;

	private const uint ShgfiLargeIcon = 0x0;

	private const uint ShgfiUseFileAttributes = 0x10;

	private static readonly object FolderIconSync = new object();

	private static ImageSource? _defaultFolderIcon;

	private readonly AppLogger _logger;

	public IconService(AppLogger logger)
	{
		_logger = logger;
	}

	public ImageSource? GetIcon(ApplicationItem app)
	{
		if (app.IconSource == IconSource.Custom && !string.IsNullOrWhiteSpace(app.CustomIconPath) && File.Exists(app.CustomIconPath))
		{
			return LoadBitmap(app.CustomIconPath);
		}
		if (app.SourceType == SourceType.Folder || IsDirectoryPath(app.TargetPath))
		{
			return GetDefaultFolderIcon();
		}
		string cachePath = GetCachePath(app.Id);
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

	private static ImageSource? GetDefaultFolderIcon()
	{
		if (_defaultFolderIcon != null)
		{
			return _defaultFolderIcon;
		}
		lock (FolderIconSync)
		{
			if (_defaultFolderIcon == null)
			{
				_defaultFolderIcon = CreateDefaultFolderIcon();
			}
		}
		return _defaultFolderIcon;
	}

	private static ImageSource? CreateDefaultFolderIcon()
	{
		ShFileInfo fileInfo = default(ShFileInfo);
		nint result = SHGetFileInfo("folder", 16u, ref fileInfo, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
		if (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			BitmapSource iconSource = Imaging.CreateBitmapSourceFromHIcon(fileInfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			iconSource.Freeze();
			return iconSource;
		}
		finally
		{
			DestroyIcon(fileInfo.hIcon);
		}
	}

	private static bool IsDirectoryPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		return Directory.Exists(path);
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyIcon(nint hIcon);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct ShFileInfo
	{
		public nint hIcon;

		public int iIcon;

		public uint dwAttributes;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szDisplayName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string szTypeName;
	}
}
