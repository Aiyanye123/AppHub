using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AppHub.Infrastructure;

public sealed class GlobalHotkeyService : IDisposable
{
	private const int HotkeyId = 0x4841;
	private const int HotkeyMessage = 0x0312;
	private const uint NoRepeat = 0x4000;

	private readonly IntPtr _windowHandle;
	private readonly HwndSource _source;
	private readonly Action _activate;
	private bool _registered;
	private bool _disposed;

	public string Error { get; private set; } = string.Empty;

	public GlobalHotkeyService(Window window, Action activate)
	{
		_activate = activate;
		_windowHandle = new WindowInteropHelper(window).EnsureHandle();
		_source = HwndSource.FromHwnd(_windowHandle) ?? throw new InvalidOperationException("Main window handle is unavailable.");
		_source.AddHook(OnWindowMessage);
	}

	public void Update(string? hotkey)
	{
		if (_disposed)
		{
			return;
		}
		Unregister();
		Error = string.Empty;
		if (string.IsNullOrWhiteSpace(hotkey))
		{
			return;
		}
		if (!TryParse(hotkey, out uint modifiers, out uint virtualKey))
		{
			Error = "格式无效，请输入 Ctrl+Alt+Space 等组合键";
			return;
		}
		_registered = RegisterHotKey(_windowHandle, HotkeyId, modifiers | NoRepeat, virtualKey);
		if (!_registered)
		{
			Error = $"无法注册快捷键，可能已被占用（错误 {Marshal.GetLastWin32Error()}）";
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		Unregister();
		_source.RemoveHook(OnWindowMessage);
		_disposed = true;
	}

	private void Unregister()
	{
		if (_registered)
		{
			UnregisterHotKey(_windowHandle, HotkeyId);
			_registered = false;
		}
	}

	private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (message == HotkeyMessage && wParam.ToInt32() == HotkeyId && _registered)
		{
			handled = true;
			_activate();
		}
		return IntPtr.Zero;
	}

	private static bool TryParse(string hotkey, out uint modifiers, out uint virtualKey)
	{
		modifiers = 0;
		virtualKey = 0;
		string[] parts = hotkey.Split('+');
		if (parts.Length < 2)
		{
			return false;
		}
		for (int index = 0; index < parts.Length - 1; index++)
		{
			uint modifier = parts[index].Trim().ToUpperInvariant() switch
			{
				"ALT" => 0x0001,
				"CTRL" or "CONTROL" => 0x0002,
				"SHIFT" => 0x0004,
				"WIN" or "WINDOWS" => 0x0008,
				_ => 0
			};
			if (modifier == 0 || (modifiers & modifier) != 0)
			{
				return false;
			}
			modifiers |= modifier;
		}
		try
		{
			if (new KeyConverter().ConvertFromInvariantString(parts[^1].Trim()) is not Key key ||
				key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed)
			{
				return false;
			}
			int mappedKey = KeyInterop.VirtualKeyFromKey(key);
			if (mappedKey <= 0 || mappedKey >= 0xFF ||
				mappedKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
			{
				return false;
			}
			virtualKey = (uint)mappedKey;
			return true;
		}
		catch (Exception exception) when (exception is NotSupportedException or FormatException or ArgumentException)
		{
			return false;
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
