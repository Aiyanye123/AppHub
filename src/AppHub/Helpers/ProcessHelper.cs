using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AppHub.Helpers;

public static class ProcessHelper
{
	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	private const uint WM_CLOSE = 16u;

	public static List<nint> GetTopLevelWindows(int processId)
	{
		List<nint> handles = new List<nint>();
		EnumWindows(delegate(nint hWnd, nint lParam)
		{
			GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
			if (lpdwProcessId == processId && IsWindowVisible(hWnd))
			{
				handles.Add(hWnd);
			}
			return true;
		}, IntPtr.Zero);
		return handles;
	}

	public static void SendClose(nint hWnd)
	{
		PostMessage(hWnd, 16u, IntPtr.Zero, IntPtr.Zero);
	}

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
}
