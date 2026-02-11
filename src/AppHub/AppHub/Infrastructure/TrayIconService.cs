using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using AppHub.Models;
using DrawingFontStyle = System.Drawing.FontStyle;

namespace AppHub.Infrastructure;

public sealed class TrayIconService : IDisposable
{
	private const string UngroupedName = "\u672a\u5206\u7ec4";

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
		_notifyIcon.DoubleClick += delegate
		{
			Restore();
		};
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
			Font = MenuFont
		};
		contextMenuStrip.Opening += OnMenuOpening;
		ApplyMenuAppearance(contextMenuStrip);
		RebuildMenuItems(contextMenuStrip);
		return contextMenuStrip;
	}

	private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
	{
		try
		{
			RebuildMenuItems(_menu);
			ApplyMenuAppearance(_menu);
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Tray menu refresh failed", ex);
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
				_showRunningOnlyApps = runningOnlyItem.Checked;
				RebuildMenuItems(menu);
				ApplyMenuAppearance(menu);
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
			if (File.Exists(path) || Directory.Exists(path))
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
				}
				ApplyMenuItemAppearance(menuItem.DropDownItems, palette);
			}
		}
	}

	private void OnWindowStateChanged(object? sender, EventArgs e)
	{
		if (_window.WindowState == WindowState.Minimized)
		{
			_notifyIcon.Visible = false;
		}
		else if (_window.IsVisible)
		{
			_notifyIcon.Visible = false;
			SetBackgroundMode(isBackground: false);
		}
	}

	public void HideToTray()
	{
		_window.Hide();
		_notifyIcon.Visible = true;
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
		_notifyIcon.Visible = false;
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
		_menu.Opening -= OnMenuOpening;
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
			return isDarkMode ? new MenuPalette(ColorTranslator.FromHtml("#0F172A"), ColorTranslator.FromHtml("#162033"), ColorTranslator.FromHtml("#1E293B"), ColorTranslator.FromHtml("#1F2937"), ColorTranslator.FromHtml("#E2E8F0"), ColorTranslator.FromHtml("#94A3B8")) : new MenuPalette(ColorTranslator.FromHtml("#FFFFFF"), ColorTranslator.FromHtml("#F1F5F9"), ColorTranslator.FromHtml("#E2E8F0"), ColorTranslator.FromHtml("#E2E8F0"), ColorTranslator.FromHtml("#1E293B"), ColorTranslator.FromHtml("#64748B"));
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
			using Pen pen = new Pen(_palette.Border);
			e.Graphics.DrawRectangle(pen, borderRect);
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
	}
}
