using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace AppHub.Infrastructure;

public sealed class TrayIconService : IDisposable
{
	private readonly Window _window;

	private readonly NotifyIcon _notifyIcon;

	private bool _balloonShown;

	private bool _isBackgroundMode;

	public event EventHandler<bool>? BackgroundModeChanged;

	public TrayIconService(Window window)
	{
		_window = window;
		_notifyIcon = new NotifyIcon
		{
			Text = "AppHub",
			Icon = GetAppIcon(),
			Visible = false,
			ContextMenuStrip = BuildMenu()
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
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		ToolStripMenuItem openItem = new ToolStripMenuItem("\u6253\u5f00 AppHub");
		openItem.Click += delegate
		{
			Restore();
		};
		ToolStripMenuItem exitItem = new ToolStripMenuItem("\u9000\u51fa");
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
		contextMenuStrip.Items.Add(openItem);
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add(exitItem);
		return contextMenuStrip;
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
}
