using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using AppHub.Models;
using DrawingFontStyle = System.Drawing.FontStyle;

namespace AppHub.Infrastructure;

public sealed class TrayIconService : IDisposable
{
	private const string UngroupedName = "\u672a\u5206\u7ec4";

	private const uint FileAttributeDirectory = 16u;

	private const uint ShgfiIcon = 0x100;

	private const uint ShgfiSmallIcon = 1u;

	private const uint ShgfiUseFileAttributes = 0x10;

	private const int SubMenuHorizontalOverlap = 2;

	private const int MenuCornerRadius = 10;

	private const int MenuItemCornerRadius = 6;

	private const int MenuItemHorizontalInset = 4;

	private const int MenuItemVerticalInset = 2;

	private static readonly Font MenuFont = new Font("Microsoft YaHei UI", 9.5f, DrawingFontStyle.Regular, GraphicsUnit.Point);

	private static readonly Image RunningDot = CreateStatusDot(ColorTranslator.FromHtml("#22C55E"));

	private static readonly Image StoppedDot = CreateStatusDot(ColorTranslator.FromHtml("#94A3B8"));

	private readonly Dictionary<Guid, CachedAppIcon> _appIconCache = new Dictionary<Guid, CachedAppIcon>();

	private readonly List<Image> _menuScopedImages = new List<Image>();

	private readonly Window _window;

	private readonly NotifyIcon _notifyIcon;

	private readonly ContextMenuStrip _menu;

	private bool _showRunningOnlyApps;

	private bool _balloonShown;

	private bool _isBackgroundMode;

	private bool _menuNeedsRefresh = true;

	private bool _keepMenuOpenOnNextItemClick;

	private int _menuAnimationToken;

	public event EventHandler<bool>? BackgroundModeChanged;

	public TrayIconService(Window window)
	{
		_window = window;
		_menu = BuildMenu();
		_notifyIcon = new NotifyIcon
		{
			Text = "AppHub",
			Icon = GetAppIcon(),
			Visible = false,
			ContextMenuStrip = _menu
		};
		_notifyIcon.MouseClick += OnNotifyIconMouseClick;
		_window.StateChanged += OnWindowStateChanged;
		_window.Closed += delegate
		{
			Dispose();
		};
	}

	private ContextMenuStrip BuildMenu()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			ShowImageMargin = true,
			ShowCheckMargin = false,
			DropShadowEnabled = false,
			Padding = new Padding(4),
			Font = MenuFont
		};
		contextMenuStrip.Opening += OnMenuOpening;
		contextMenuStrip.Closing += OnMenuClosing;
		RefreshMenu(contextMenuStrip);
		return contextMenuStrip;
	}

	private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
	{
		try
		{
			if (_menuNeedsRefresh)
			{
				RefreshMenu(_menu);
			}
			else
			{
				ApplyMenuAppearance(_menu);
			}
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Tray menu refresh failed", ex);
		}
	}

	private void OnMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
	{
		if (_keepMenuOpenOnNextItemClick && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
		{
			e.Cancel = true;
		}
		_keepMenuOpenOnNextItemClick = false;
	}

	private void RefreshMenu(ContextMenuStrip menu, bool keepBottomEdge = false, bool animateVerticalShift = false)
	{
		bool keepLocation = menu.Visible;
		System.Drawing.Point currentLocation = menu.Location;
		int currentBottom = menu.Bottom;
		int animationToken = ++_menuAnimationToken;
		RebuildMenuItems(menu);
		ApplyMenuAppearance(menu);
		if (keepLocation)
		{
			int y = keepBottomEdge ? currentBottom - menu.Height : currentLocation.Y;
			if (animateVerticalShift && y != currentLocation.Y)
			{
				AnimateMenuVerticalShift(menu, currentLocation.X, currentLocation.Y, y, animationToken);
			}
			else
			{
				menu.Location = new System.Drawing.Point(currentLocation.X, y);
			}
		}
		_menuNeedsRefresh = false;
	}

	private async void AnimateMenuVerticalShift(ContextMenuStrip menu, int x, int fromY, int toY, int token)
	{
		const int frameCount = 8;
		const int frameDelayMs = 12;
		for (int i = 1; i <= frameCount; i++)
		{
			if (token != _menuAnimationToken || !menu.Visible)
			{
				return;
			}

			double t = (double)i / frameCount;
			double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
			int y = fromY + (int)Math.Round((toY - fromY) * eased);
			menu.Location = new System.Drawing.Point(x, y);
			await Task.Delay(frameDelayMs);
		}
	}

	private void RebuildMenuItems(ContextMenuStrip menu)
	{
		menu.SuspendLayout();
		try
		{
			menu.Items.Clear();
			DisposeMenuScopedImages();

			ToolStripMenuItem runningOnlyItem = CreateMenuItem("\u4ec5\u663e\u793a\u8fd0\u884c\u4e2d\u5e94\u7528");
			runningOnlyItem.CheckOnClick = true;
			runningOnlyItem.Checked = _showRunningOnlyApps;
			runningOnlyItem.Click += delegate
			{
				_keepMenuOpenOnNextItemClick = true;
				_showRunningOnlyApps = runningOnlyItem.Checked;
				RefreshMenu(menu, keepBottomEdge: true, animateVerticalShift: true);
			};
			menu.Items.Add(runningOnlyItem);
			menu.Items.Add(new ToolStripSeparator());

			IReadOnlyList<ApplicationItem> apps = AppServices.Catalog.GetAllApps();
			if (apps.Count == 0)
			{
				ToolStripMenuItem emptyItem = CreateMenuItem("\u6682\u65e0\u5e94\u7528");
				emptyItem.Enabled = false;
				menu.Items.Add(emptyItem);
			}
			else
			{
				Dictionary<Guid, bool> runningStates = BuildRunningStates(apps);
				List<ApplicationItem> visibleApps = _showRunningOnlyApps ? apps.Where((ApplicationItem app) => runningStates.TryGetValue(app.Id, out bool isRunning) && isRunning).ToList() : apps.ToList();
				if (visibleApps.Count == 0)
				{
					ToolStripMenuItem noRunningItem = CreateMenuItem("\u6682\u65e0\u8fd0\u884c\u4e2d\u5e94\u7528");
					noRunningItem.Enabled = false;
					menu.Items.Add(noRunningItem);
				}
				else
				{
					List<ApplicationGroupMenu> groups = BuildGroupMenus(visibleApps);
					foreach (ApplicationGroupMenu group in groups)
					{
						int runningCount = group.Apps.Count((ApplicationItem app) => runningStates.TryGetValue(app.Id, out bool isRunning) && isRunning);
						ToolStripMenuItem groupItem = CreateMenuItem($"{group.Name} ({runningCount}/{group.Apps.Count})");
						groupItem.Image = runningCount > 0 ? RunningDot : StoppedDot;
						AttachDropDownSnap(groupItem);

						foreach (ApplicationItem app in group.Apps)
						{
							bool isRunning2 = runningStates.TryGetValue(app.Id, out bool value) && value;
							ToolStripMenuItem appItem = CreateMenuItem(ResolveTrayAppText(app));
							Image appStatusWithIcon = CreateAppStatusIcon(app, isRunning2);
							appItem.Image = appStatusWithIcon;
							_menuScopedImages.Add(appStatusWithIcon);
							appItem.ToolTipText = app.TargetPath;
							appItem.ShortcutKeyDisplayString = isRunning2 ? "\u5173\u95ed" : "\u542f\u52a8";
							appItem.Click += delegate
							{
								ToggleAppFromTray(app, isRunning2);
							};
							groupItem.DropDownItems.Add(appItem);
						}

						menu.Items.Add(groupItem);
					}
				}
			}

			menu.Items.Add(new ToolStripSeparator());

			ToolStripMenuItem openItem = CreateMenuItem("\u6253\u5f00 AppHub");
			openItem.Click += delegate
			{
				Restore();
			};
			menu.Items.Add(openItem);

			ToolStripMenuItem exitItem = CreateMenuItem("\u9000\u51fa");
			exitItem.Click += delegate
			{
				_notifyIcon.Visible = false;
				if (_window is MainWindow mainWindow)
				{
					mainWindow.RequestClose();
				}
				else
				{
					_window.Close();
				}
			};
			menu.Items.Add(exitItem);
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Build tray menu failed", ex);
			menu.Items.Clear();
			ToolStripMenuItem failedItem = CreateMenuItem("\u83dc\u5355\u52a0\u8f7d\u5931\u8d25");
			failedItem.Enabled = false;
			menu.Items.Add(failedItem);
			menu.Items.Add(new ToolStripSeparator());
			ToolStripMenuItem openItem = CreateMenuItem("\u6253\u5f00 AppHub");
			openItem.Click += delegate
			{
				Restore();
			};
			menu.Items.Add(openItem);
		}
		finally
		{
			menu.ResumeLayout(performLayout: false);
		}
	}

	private void DisposeMenuScopedImages()
	{
		foreach (Image menuScopedImage in _menuScopedImages)
		{
			menuScopedImage.Dispose();
		}
		_menuScopedImages.Clear();
	}

	private Image CreateAppStatusIcon(ApplicationItem app, bool isRunning)
	{
		const int iconSize = 18;
		const int dotSpacing = 3;
		Image appIcon = GetOrCreateAppIcon(app, iconSize);
		Image dot = isRunning ? RunningDot : StoppedDot;
		Bitmap composite = new Bitmap(dot.Width + dotSpacing + iconSize, iconSize);
		using Graphics graphics = Graphics.FromImage(composite);
		graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
		graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
		graphics.Clear(Color.Transparent);
		graphics.DrawImage(dot, 0, (iconSize - dot.Height) / 2, dot.Width, dot.Height);
		graphics.DrawImage(appIcon, dot.Width + dotSpacing, 0, iconSize, iconSize);
		return composite;
	}

	private Image GetOrCreateAppIcon(ApplicationItem app, int iconSize)
	{
		string signature = BuildIconSignature(app);
		if (_appIconCache.TryGetValue(app.Id, out CachedAppIcon? cached) && string.Equals(cached.Signature, signature, StringComparison.Ordinal))
		{
			return cached.Icon;
		}
		Image icon = LoadAppIcon(app, iconSize);
		if (_appIconCache.TryGetValue(app.Id, out CachedAppIcon? old))
		{
			old.Icon.Dispose();
		}
		_appIconCache[app.Id] = new CachedAppIcon(signature, icon);
		return icon;
	}

	private static string BuildIconSignature(ApplicationItem app)
	{
		return $"{app.IconSource}|{app.CustomIconPath}|{app.TargetPath}|{app.SourceType}";
	}

	private static Image LoadAppIcon(ApplicationItem app, int size)
	{
		if (app.IconSource == IconSource.Custom && !string.IsNullOrWhiteSpace(app.CustomIconPath) && File.Exists(app.CustomIconPath))
		{
			try
			{
				return LoadScaledImageFromFile(app.CustomIconPath, size);
			}
			catch
			{
			}
		}
		if (!string.IsNullOrWhiteSpace(app.TargetPath))
		{
			string path = app.TargetPath;
			if (app.SourceType == SourceType.Folder || Directory.Exists(path))
			{
				return LoadFolderIcon(size);
			}
			if (File.Exists(path))
			{
				try
				{
					using Icon? icon = Icon.ExtractAssociatedIcon(path);
					if (icon != null)
					{
						using Bitmap iconBitmap = icon.ToBitmap();
						return ScaleToSquare(iconBitmap, size);
					}
				}
				catch
				{
				}
			}
		}
		using Bitmap fallback = SystemIcons.Application.ToBitmap();
		return ScaleToSquare(fallback, size);
	}

	private static Image LoadFolderIcon(int size)
	{
		if (TryGetShellFolderIcon(out Bitmap? folderBitmap) && folderBitmap != null)
		{
			using (folderBitmap)
			{
				return ScaleToSquare(folderBitmap, size);
			}
		}
		using Bitmap fallback = SystemIcons.Application.ToBitmap();
		return ScaleToSquare(fallback, size);
	}

	private static Image LoadScaledImageFromFile(string path, int size)
	{
		try
		{
			using Image source = Image.FromFile(path);
			return ScaleToSquare(source, size);
		}
		catch
		{
			using Bitmap fallback = SystemIcons.Application.ToBitmap();
			return ScaleToSquare(fallback, size);
		}
	}

	private static Bitmap ScaleToSquare(Image source, int size)
	{
		Bitmap result = new Bitmap(size, size);
		using Graphics graphics = Graphics.FromImage(result);
		graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
		graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
		graphics.Clear(Color.Transparent);
		float ratio = Math.Min((float)size / source.Width, (float)size / source.Height);
		int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
		int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
		int x = (size - width) / 2;
		int y = (size - height) / 2;
		graphics.DrawImage(source, x, y, width, height);
		return result;
	}

	private static Dictionary<Guid, bool> BuildRunningStates(IReadOnlyList<ApplicationItem> apps)
	{
		Dictionary<Guid, bool> result = new Dictionary<Guid, bool>(apps.Count);
		foreach (ApplicationItem app in apps)
		{
			result[app.Id] = AppServices.ProcessService.GetRunningStatus(app.Id).IsRunning;
		}
		return result;
	}

	private static List<ApplicationGroupMenu> BuildGroupMenus(IReadOnlyList<ApplicationItem> apps)
	{
		Dictionary<string, List<ApplicationItem>> map = new Dictionary<string, List<ApplicationItem>>(StringComparer.OrdinalIgnoreCase);
		foreach (ApplicationItem app in apps)
		{
			string groupName = NormalizeGroupName(app.GroupName);
			if (!map.TryGetValue(groupName, out List<ApplicationItem>? list))
			{
				list = new List<ApplicationItem>();
				map[groupName] = list;
			}
			list.Add(app);
		}

		IEnumerable<string> orderedGroupNames = map.Keys.OrderBy(delegate(string name)
		{
			return string.Equals(name, UngroupedName, StringComparison.Ordinal) ? 0 : 1;
		}).ThenBy((string name) => name, StringComparer.OrdinalIgnoreCase);

		List<ApplicationGroupMenu> result = new List<ApplicationGroupMenu>();
		foreach (string groupName2 in orderedGroupNames)
		{
			result.Add(new ApplicationGroupMenu(groupName2, map[groupName2]));
		}
		return result;
	}

	private static string NormalizeGroupName(string? groupName)
	{
		return string.IsNullOrWhiteSpace(groupName) ? UngroupedName : groupName.Trim();
	}

	private static string ResolveTrayAppText(ApplicationItem app)
	{
		if (!string.IsNullOrWhiteSpace(app.DisplayName))
		{
			return app.DisplayName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(app.TargetPath))
		{
			try
			{
				if (Directory.Exists(app.TargetPath))
				{
					string directoryName = new DirectoryInfo(app.TargetPath).Name;
					if (!string.IsNullOrWhiteSpace(directoryName))
					{
						return directoryName;
					}
				}
				else
				{
					string fileName = Path.GetFileNameWithoutExtension(app.TargetPath);
					if (!string.IsNullOrWhiteSpace(fileName))
					{
						return fileName;
					}
				}
			}
			catch
			{
			}
		}
		return "\u672a\u547d\u540d\u5e94\u7528";
	}

	private void ToggleAppFromTray(ApplicationItem app, bool isRunning)
	{
		_menuNeedsRefresh = true;
		if (isRunning)
		{
			_ = ExecuteOnUiThreadAsync(delegate
			{
				return CloseAppFromTrayAsync(app);
			});
		}
		else
		{
			ExecuteOnUiThread(delegate
			{
				LaunchAppFromTray(app);
			});
		}
	}

	private void LaunchAppFromTray(ApplicationItem app)
	{
		try
		{
			LaunchResult result = AppServices.LaunchService.Launch(app.Id);
			if (!result.Success)
			{
				ShowMenuOperationResult("\u542f\u52a8\u5931\u8d25", app.DisplayName, result.ErrorMessage, ToolTipIcon.Warning);
			}
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Launch from tray failed", ex);
			ShowMenuOperationResult("\u542f\u52a8\u5931\u8d25", app.DisplayName, ex.Message, ToolTipIcon.Error);
		}
	}

	private async Task CloseAppFromTrayAsync(ApplicationItem app)
	{
		try
		{
			CloseResult result = await AppServices.ProcessService.CloseAppAsync(app.Id, force: false);
			if (!result.Success)
			{
				ShowMenuOperationResult("\u5173\u95ed\u5931\u8d25", app.DisplayName, result.ErrorMessage, ToolTipIcon.Warning);
			}
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Close from tray failed", ex);
			ShowMenuOperationResult("\u5173\u95ed\u5931\u8d25", app.DisplayName, ex.Message, ToolTipIcon.Error);
		}
	}

	private void ShowMenuOperationResult(string title, string appName, string? detail, ToolTipIcon icon)
	{
		string message = string.IsNullOrWhiteSpace(detail) ? appName : $"{appName}\uff1a{detail}";
		_notifyIcon.ShowBalloonTip(1200, title, message, icon);
	}

	private void ExecuteOnUiThread(Action action)
	{
		if (_window.Dispatcher.CheckAccess())
		{
			action();
			return;
		}
		_window.Dispatcher.Invoke(action);
	}

	private Task ExecuteOnUiThreadAsync(Func<Task> action)
	{
		if (_window.Dispatcher.CheckAccess())
		{
			return action();
		}
		return _window.Dispatcher.InvokeAsync(action).Task.Unwrap();
	}

	private static ToolStripMenuItem CreateMenuItem(string text)
	{
		return new ToolStripMenuItem(text)
		{
			Padding = new Padding(8, 5, 8, 5),
			ImageScaling = ToolStripItemImageScaling.None
		};
	}

	private void ApplyMenuAppearance(ContextMenuStrip menu)
	{
		MenuPalette palette = MenuPalette.FromTheme(AppServices.Config.Settings.IsDarkMode);
		menu.RenderMode = ToolStripRenderMode.Professional;
		menu.Renderer = new TrayMenuRenderer(new TrayMenuColorTable(palette), palette);
		menu.ShowCheckMargin = false;
		menu.ShowImageMargin = true;
		menu.DropShadowEnabled = false;
		menu.Padding = new Padding(4);
		menu.BackColor = palette.Background;
		menu.ForeColor = palette.Text;
		menu.Font = MenuFont;
		ApplyMenuItemAppearance(menu.Items, palette);
	}

	private static void ApplyMenuItemAppearance(ToolStripItemCollection items, MenuPalette palette)
	{
		foreach (ToolStripItem item in items)
		{
			if (item is ToolStripMenuItem menuItem)
			{
				menuItem.BackColor = palette.Background;
				menuItem.ForeColor = menuItem.Enabled ? palette.Text : palette.SubtleText;
				menuItem.DropDown.BackColor = palette.Background;
				menuItem.DropDown.ForeColor = palette.Text;
				menuItem.DropDown.Font = MenuFont;
				if (menuItem.DropDown is ToolStripDropDownMenu dropDownMenu)
				{
					dropDownMenu.ShowImageMargin = true;
					dropDownMenu.ShowCheckMargin = false;
					dropDownMenu.DropShadowEnabled = false;
					dropDownMenu.Padding = new Padding(4);
					dropDownMenu.Margin = Padding.Empty;
				}
				ApplyMenuItemAppearance(menuItem.DropDownItems, palette);
			}
		}
	}

	private static void AttachDropDownSnap(ToolStripMenuItem menuItem)
	{
		menuItem.DropDownOpened += delegate
		{
			SnapDropDownToParent(menuItem);
			menuItem.Owner?.Invalidate(menuItem.Bounds);
		};
		menuItem.DropDownClosed += delegate
		{
			menuItem.Owner?.Invalidate(menuItem.Bounds);
		};
	}

	private static void SnapDropDownToParent(ToolStripMenuItem menuItem)
	{
		if (menuItem.Owner == null)
		{
			return;
		}
		ToolStripDropDown dropDown = menuItem.DropDown;
		if (!dropDown.Visible)
		{
			return;
		}
		System.Drawing.Point ownerScreen = menuItem.Owner.PointToScreen(System.Drawing.Point.Empty);
		Rectangle bounds = menuItem.Bounds;
		int x = ownerScreen.X + bounds.Right - SubMenuHorizontalOverlap;
		int y = ownerScreen.Y + bounds.Top;
		dropDown.Location = new System.Drawing.Point(x, y);
	}

	private static bool TryGetShellFolderIcon(out Bitmap? bitmap)
	{
		bitmap = null;
		ShFileInfo fileInfo = default(ShFileInfo);
		nint result = SHGetFileInfo("folder", FileAttributeDirectory, ref fileInfo, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
		if (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			using Icon icon = Icon.FromHandle(fileInfo.hIcon);
			bitmap = icon.ToBitmap();
			return true;
		}
		finally
		{
			DestroyIcon(fileInfo.hIcon);
		}
	}

	private void OnWindowStateChanged(object? sender, EventArgs e)
	{
		if (_window.WindowState == WindowState.Minimized)
		{
			_notifyIcon.Visible = true;
		}
		else if (_window.IsVisible)
		{
			_notifyIcon.Visible = true;
			SetBackgroundMode(isBackground: false);
		}
	}

	private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			Restore();
		}
	}

	public void HideToTray()
	{
		_window.Hide();
		_notifyIcon.Visible = true;
		_menuNeedsRefresh = true;
		SetBackgroundMode(isBackground: true);
		if (!_balloonShown)
		{
			_notifyIcon.ShowBalloonTip(1000, "AppHub", "AppHub \u6b63\u5728\u6258\u76d8\u4e2d\u8fd0\u884c\u3002", ToolTipIcon.Info);
			_balloonShown = true;
		}
	}

	private void Restore()
	{
		_window.Show();
		_window.WindowState = WindowState.Normal;
		_window.Activate();
		_notifyIcon.Visible = true;
		SetBackgroundMode(isBackground: false);
	}

	private static Icon GetAppIcon()
	{
		try
		{
			string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrWhiteSpace(exePath))
			{
				Icon? icon = Icon.ExtractAssociatedIcon(exePath);
				if (icon != null)
				{
					return icon;
				}
			}
		}
		catch
		{
		}
		return SystemIcons.Application;
	}

	public void Dispose()
	{
		SetBackgroundMode(isBackground: false);
		_notifyIcon.Visible = false;
		_notifyIcon.MouseClick -= OnNotifyIconMouseClick;
		_menu.Opening -= OnMenuOpening;
		_menu.Closing -= OnMenuClosing;
		DisposeMenuScopedImages();
		foreach (CachedAppIcon icon in _appIconCache.Values)
		{
			icon.Icon.Dispose();
		}
		_appIconCache.Clear();
		_notifyIcon.Dispose();
	}

	private void SetBackgroundMode(bool isBackground)
	{
		if (_isBackgroundMode != isBackground)
		{
			_isBackgroundMode = isBackground;
			this.BackgroundModeChanged?.Invoke(this, isBackground);
		}
	}

	private static Bitmap CreateStatusDot(Color color)
	{
		Bitmap bitmap = new Bitmap(10, 10);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
		graphics.Clear(Color.Transparent);
		using SolidBrush brush = new SolidBrush(color);
		graphics.FillEllipse(brush, 1, 1, 8, 8);
		return bitmap;
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

	private sealed record ApplicationGroupMenu(string Name, IReadOnlyList<ApplicationItem> Apps);

	private sealed record CachedAppIcon(string Signature, Image Icon);

	private readonly struct MenuPalette
	{
		public Color Background { get; }

		public Color Hover { get; }

		public Color Pressed { get; }

		public Color Border { get; }

		public Color Text { get; }

		public Color SubtleText { get; }

		public MenuPalette(Color background, Color hover, Color pressed, Color border, Color text, Color subtleText)
		{
			Background = background;
			Hover = hover;
			Pressed = pressed;
			Border = border;
			Text = text;
			SubtleText = subtleText;
		}

		public static MenuPalette FromTheme(bool isDarkMode)
		{
			return isDarkMode ? new MenuPalette(ColorTranslator.FromHtml("#111827"), ColorTranslator.FromHtml("#374151"), ColorTranslator.FromHtml("#4B5563"), ColorTranslator.FromHtml("#374151"), ColorTranslator.FromHtml("#E5E7EB"), ColorTranslator.FromHtml("#9CA3AF")) : new MenuPalette(ColorTranslator.FromHtml("#FFFFFF"), ColorTranslator.FromHtml("#F0F0F0"), ColorTranslator.FromHtml("#F0F0F0"), ColorTranslator.FromHtml("#E5E7EB"), ColorTranslator.FromHtml("#1F2937"), ColorTranslator.FromHtml("#6B7280"));
		}
	}

	private sealed class TrayMenuColorTable : ProfessionalColorTable
	{
		private readonly MenuPalette _palette;

		public override Color ToolStripDropDownBackground => _palette.Background;

		public override Color MenuItemSelected => _palette.Hover;

		public override Color MenuItemSelectedGradientBegin => _palette.Hover;

		public override Color MenuItemSelectedGradientEnd => _palette.Hover;

		public override Color MenuItemBorder => _palette.Border;

		public override Color MenuBorder => _palette.Border;

		public override Color MenuItemPressedGradientBegin => _palette.Pressed;

		public override Color MenuItemPressedGradientMiddle => _palette.Pressed;

		public override Color MenuItemPressedGradientEnd => _palette.Pressed;

		public override Color SeparatorDark => _palette.Border;

		public override Color SeparatorLight => _palette.Border;

		public override Color ImageMarginGradientBegin => _palette.Background;

		public override Color ImageMarginGradientMiddle => _palette.Background;

		public override Color ImageMarginGradientEnd => _palette.Background;

		public TrayMenuColorTable(MenuPalette palette)
		{
			_palette = palette;
			UseSystemColors = false;
		}
	}

	private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
	{
		private readonly MenuPalette _palette;

		public TrayMenuRenderer(ProfessionalColorTable colorTable, MenuPalette palette)
			: base(colorTable)
		{
			_palette = palette;
			RoundedEdges = false;
		}

		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
		{
			Rectangle borderRect = new Rectangle(System.Drawing.Point.Empty, e.ToolStrip.Size);
			borderRect.Width--;
			borderRect.Height--;
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = CreateRoundedRectanglePath(borderRect, MenuCornerRadius);
			using Pen pen = new Pen(_palette.Border);
			e.Graphics.DrawPath(pen, path);
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
		{
			Rectangle backgroundRect = new Rectangle(System.Drawing.Point.Empty, e.ToolStrip.Size);
			if (backgroundRect.Width <= 1 || backgroundRect.Height <= 1)
			{
				return;
			}

			backgroundRect.Width--;
			backgroundRect.Height--;
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = CreateRoundedRectanglePath(backgroundRect, MenuCornerRadius);
			Region? oldRegion = e.ToolStrip.Region;
			e.ToolStrip.Region = new Region(path);
			oldRegion?.Dispose();
			using SolidBrush brush = new SolidBrush(_palette.Background);
			e.Graphics.FillPath(brush, path);
		}

		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
		{
			bool isTopLevelItem = e.Item.Owner is ContextMenuStrip;
			if (isTopLevelItem)
			{
				base.OnRenderMenuItemBackground(e);
				return;
			}

			bool hasVisibleDropDown = e.Item is ToolStripMenuItem menuItem && menuItem.DropDown.Visible;
			bool isActive = e.Item.Pressed || e.Item.Selected || hasVisibleDropDown;
			int horizontalInset = MenuItemHorizontalInset;
			int verticalInset = MenuItemVerticalInset;

			Rectangle bounds = e.Item.Bounds;
			Rectangle fillRect = Rectangle.Inflate(bounds, -horizontalInset, -verticalInset);
			if (fillRect.Width <= 0 || fillRect.Height <= 0)
			{
				return;
			}

			Color fillColor = _palette.Background;
			if (e.Item.Pressed)
			{
				fillColor = _palette.Pressed;
			}
			else if (isActive)
			{
				fillColor = _palette.Hover;
			}

			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = CreateRoundedRectanglePath(fillRect, MenuItemCornerRadius);
			using SolidBrush brush = new SolidBrush(fillColor);
			e.Graphics.FillPath(brush, path);
			if (isActive)
			{
				using Pen pen = new Pen(_palette.Border);
				e.Graphics.DrawPath(pen, path);
			}
		}

		protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
		{
			int y = e.Item.Bounds.Height / 2;
			using Pen pen = new Pen(_palette.Border);
			e.Graphics.DrawLine(pen, 12, y, e.Item.Bounds.Width - 12, y);
		}

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			e.TextColor = e.Item.Enabled ? _palette.Text : _palette.SubtleText;
			base.OnRenderItemText(e);
		}

		private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
		{
			GraphicsPath path = new GraphicsPath();
			if (rect.Width <= 0 || rect.Height <= 0)
			{
				return path;
			}

			int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
			int diameter = safeRadius * 2;
			Rectangle arc = new Rectangle(rect.Location, new System.Drawing.Size(diameter, diameter));

			path.AddArc(arc, 180, 90);
			arc.X = rect.Right - diameter;
			path.AddArc(arc, 270, 90);
			arc.Y = rect.Bottom - diameter;
			path.AddArc(arc, 0, 90);
			arc.X = rect.Left;
			path.AddArc(arc, 90, 90);
			path.CloseFigure();
			return path;
		}
	}
}
