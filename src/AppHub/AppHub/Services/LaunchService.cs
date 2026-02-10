using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AppHub.Infrastructure;
using AppHub.Models;

namespace AppHub.Services;

public sealed class LaunchService
{
	private readonly AppCatalogService _catalog;

	private readonly ProcessControlService _processControl;

	private readonly AppLogger _logger;

	public LaunchService(AppCatalogService catalog, ProcessControlService processControl, AppLogger logger)
	{
		_catalog = catalog;
		_processControl = processControl;
		_logger = logger;
	}

	public LaunchResult Launch(Guid appId)
	{
		ApplicationItem app = _catalog.GetAppById(appId);
		if (app == null)
		{
			return new LaunchResult
			{
				Success = false,
				ErrorMessage = "\u5e94\u7528\u4e0d\u5b58\u5728\u3002"
			};
		}
		return LaunchInternal(app, app.Arguments);
	}

	public LaunchResult LaunchWithArgs(Guid appId, string args)
	{
		ApplicationItem app = _catalog.GetAppById(appId);
		if (app == null)
		{
			return new LaunchResult
			{
				Success = false,
				ErrorMessage = "\u5e94\u7528\u4e0d\u5b58\u5728\u3002"
			};
		}
		return LaunchInternal(app, args);
	}

	public void OpenFileLocation(Guid appId)
	{
		ApplicationItem app = _catalog.GetAppById(appId);
		if (app == null)
		{
			return;
		}
		string path = app.TargetPath;
		if (File.Exists(path))
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
			{
				UseShellExecute = true
			});
		}
	}

	private LaunchResult LaunchInternal(ApplicationItem app, string args)
	{
		if (AppServices.Config.Settings.LaunchBehavior == LaunchBehavior.FocusExisting)
		{
			ProcessStatus existingStatus = _processControl.GetRunningStatus(app.Id);
			if (existingStatus.IsRunning)
			{
				if (TryActivateRunningWindow(existingStatus.MatchedProcessIds, out int focusedPid))
				{
					return new LaunchResult
					{
						Success = true,
						ProcessId = focusedPid
					};
				}
				return new LaunchResult
				{
					Success = false,
					ErrorMessage = "\u68c0\u6d4b\u5230\u5e94\u7528\u5df2\u8fd0\u884c\uff0c\u4f46\u65e0\u6cd5\u5207\u6362\u5230\u5df2\u6709\u7a97\u53e3\u3002"
				};
			}
		}
		if (string.IsNullOrWhiteSpace(app.TargetPath) || !File.Exists(app.TargetPath))
		{
			return new LaunchResult
			{
				Success = false,
				ErrorMessage = "\u76ee\u6807\u8def\u5f84\u65e0\u6548\u3002"
			};
		}
		try
		{
			Process process = Process.Start(new ProcessStartInfo
			{
				FileName = app.TargetPath,
				Arguments = args,
				WorkingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory) ? Path.GetDirectoryName(app.TargetPath) ?? string.Empty : app.WorkingDirectory,
				UseShellExecute = true
			});
			if (process == null)
			{
				return new LaunchResult
				{
					Success = false,
					ErrorMessage = "\u542f\u52a8\u8fdb\u7a0b\u5931\u8d25\u3002"
				};
			}
			app.LastLaunchTime = DateTime.Now;
			_processControl.RegisterProcess(app.Id, process.Id);
			AppServices.StatusScheduler.RequestImmediateRefresh();
			return new LaunchResult
			{
				Success = true,
				ProcessId = process.Id
			};
		}
		catch (Exception ex)
		{
			_logger.Error("Launch failed", ex);
			return new LaunchResult
			{
				Success = false,
				ErrorMessage = ex.Message
			};
		}
	}

	private static bool TryActivateRunningWindow(IReadOnlyList<int> processIds, out int focusedPid)
	{
		for (int i = 0; i < processIds.Count; i++)
		{
			int pid = processIds[i];
			try
			{
				using Process process = Process.GetProcessById(pid);
				process.Refresh();
				nint handle = process.MainWindowHandle;
				if (handle == IntPtr.Zero)
				{
					continue;
				}
				ShowWindowAsync(handle, 9);
				SetForegroundWindow(handle);
				focusedPid = pid;
				return true;
			}
			catch
			{
			}
		}
		focusedPid = 0;
		return false;
	}

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);
}
