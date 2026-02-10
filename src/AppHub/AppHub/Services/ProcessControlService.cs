using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AppHub.Helpers;
using AppHub.Infrastructure;
using AppHub.Models;

namespace AppHub.Services;

public sealed class ProcessControlService
{
	private const int MinCloseGracePeriodMs = 200;

	private const int MaxCloseGracePeriodMs = 120000;

	private const int DefaultCloseGracePeriodMs = 5000;

	private readonly AppCatalogService _catalog;

	private readonly AppSettings _settings;

	private readonly AppLogger _logger;

	private readonly Dictionary<Guid, HashSet<int>> _trackedPids = new Dictionary<Guid, HashSet<int>>();

	private readonly object _trackedPidsSync = new object();

	private int _refreshing;

	public event EventHandler<ProcessStatusesChangedEventArgs>? ProcessStatusesChanged;

	public ProcessControlService(AppCatalogService catalog, AppSettings settings, AppLogger logger)
	{
		_catalog = catalog;
		_settings = settings;
		_logger = logger;
	}

	public void RegisterProcess(Guid appId, int pid)
	{
		lock (_trackedPidsSync)
		{
			if (!_trackedPids.TryGetValue(appId, out HashSet<int> set))
			{
				set = new HashSet<int>();
				_trackedPids[appId] = set;
			}
			set.Add(pid);
		}
	}

	public ProcessStatus GetRunningStatus(Guid appId)
	{
		ApplicationItem? app = _catalog.GetAppById(appId);
		if (app == null)
		{
			return new ProcessStatus();
		}
		ProcessSnapshot snapshot = CaptureProcessSnapshot();
		return BuildStatus(appId, app, snapshot);
	}

	public async Task RefreshAllStatusAsync()
	{
		if (Interlocked.Exchange(ref _refreshing, 1) == 1)
		{
			return;
		}
		try
		{
			List<(Guid Id, ProcessStatus Status)> snapshots = await Task.Run(delegate
			{
				List<(Guid, ProcessStatus)> list = new List<(Guid, ProcessStatus)>();
				IReadOnlyList<ApplicationItem> apps = _catalog.GetAllApps();
				ProcessSnapshot processSnapshot = CaptureProcessSnapshot();
				foreach (ApplicationItem current in apps)
				{
					list.Add((current.Id, BuildStatus(current.Id, current, processSnapshot)));
				}
				return list;
			});
			Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
			dispatcher.Invoke((Action)delegate
			{
				Dictionary<Guid, ProcessStatus> batch = snapshots.ToDictionary(item => item.Id, item => item.Status);
				this.ProcessStatusesChanged?.Invoke(this, new ProcessStatusesChangedEventArgs(batch));
			});
		}
		finally
		{
			Interlocked.Exchange(ref _refreshing, 0);
		}
	}

	public async Task<CloseResult> CloseAppAsync(Guid appId, bool force)
	{
		if (_catalog.GetAppById(appId) == null)
		{
			return new CloseResult
			{
				Success = false,
				ErrorMessage = "\u5e94\u7528\u4e0d\u5b58\u5728\u3002"
			};
		}
		ProcessStatus status = GetRunningStatus(appId);
		if (!status.IsRunning)
		{
			return new CloseResult
			{
				Success = false,
				ErrorMessage = "\u5e94\u7528\u672a\u8fd0\u884c\u3002"
			};
		}
		if (!force)
		{
			foreach (int matchedProcessId in status.MatchedProcessIds)
			{
				foreach (nint topLevelWindow in ProcessHelper.GetTopLevelWindows(matchedProcessId))
				{
					ProcessHelper.SendClose(topLevelWindow);
				}
			}
			await Task.Delay(NormalizeCloseGracePeriodMs(_settings.CloseGracePeriodMs));
			status = GetRunningStatus(appId);
			if (!status.IsRunning)
			{
				AppServices.StatusScheduler.RequestImmediateRefresh();
				return new CloseResult
				{
					Success = true
				};
			}
			if (_settings.AllowForceKill)
			{
				return await CloseAppAsync(appId, force: true);
			}
			return new CloseResult
			{
				Success = false,
				ErrorMessage = "\u5e94\u7528\u672a\u5728\u9650\u5b9a\u65f6\u95f4\u5185\u9000\u51fa\u3002"
			};
		}
		if (!_settings.AllowForceKill)
		{
			return new CloseResult
			{
				Success = false,
				ErrorMessage = "\u5df2\u7981\u7528\u5f3a\u5236\u7ed3\u675f\u8fdb\u7a0b\u3002"
			};
		}
		foreach (int pid in status.MatchedProcessIds)
		{
			try
			{
				Process.GetProcessById(pid).Kill(entireProcessTree: true);
			}
			catch (Exception ex)
			{
				_logger.Warn($"Force kill failed for pid {pid}: {ex.Message}");
			}
		}
		await Task.Delay(300);
		ProcessStatus verify = GetRunningStatus(appId);
		AppServices.StatusScheduler.RequestImmediateRefresh();
		if (verify.IsRunning)
		{
			return new CloseResult
			{
				Success = false,
				ErrorMessage = "\u7ec8\u6b62\u8fdb\u7a0b\u5931\u8d25\u3002"
			};
		}
		return new CloseResult
		{
			Success = true
		};
	}

	private ProcessStatus BuildStatus(Guid appId, ApplicationItem app, ProcessSnapshot snapshot)
	{
		List<int> matched = new List<int>();
		lock (_trackedPidsSync)
		{
			if (_trackedPids.TryGetValue(appId, out HashSet<int> pids))
			{
				int[] tracked = pids.ToArray();
				foreach (int pid in tracked)
				{
					if (snapshot.LiveProcessIds.Contains(pid))
					{
						matched.Add(pid);
					}
					else
					{
						pids.Remove(pid);
					}
				}
			}
		}
		if (matched.Count == 0 && app.TrackProcess && !string.IsNullOrWhiteSpace(app.TargetPath))
		{
			string normalizedTarget = PathHelper.NormalizePath(app.TargetPath);
			if (snapshot.ProcessIdsByPath.TryGetValue(normalizedTarget, out List<int> byPath))
			{
				matched.AddRange(byPath);
			}
		}
		List<int> normalizedMatched = matched.Distinct().ToList();
		if (normalizedMatched.Count > 0)
		{
			lock (_trackedPidsSync)
			{
				if (!_trackedPids.TryGetValue(appId, out HashSet<int> trackedPids))
				{
					trackedPids = new HashSet<int>();
					_trackedPids[appId] = trackedPids;
				}
				foreach (int pid in normalizedMatched)
				{
					trackedPids.Add(pid);
				}
			}
		}
		return new ProcessStatus
		{
			MatchedProcessIds = normalizedMatched,
			IsRunning = normalizedMatched.Count > 0,
			LastSeenTime = normalizedMatched.Count > 0 ? DateTime.Now : null
		};
	}

	private ProcessSnapshot CaptureProcessSnapshot()
	{
		HashSet<int> running = new HashSet<int>();
		Dictionary<string, List<int>> pathToPids = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
		foreach (Process process in Process.GetProcesses())
		{
			using (process)
			{
				try
				{
					running.Add(process.Id);
				}
				catch
				{
					continue;
				}
				try
				{
					string path = process.MainModule?.FileName ?? string.Empty;
					if (string.IsNullOrWhiteSpace(path))
					{
						continue;
					}
					string normalizedPath = PathHelper.NormalizePath(path);
					if (!pathToPids.TryGetValue(normalizedPath, out List<int> pids))
					{
						pids = new List<int>();
						pathToPids[normalizedPath] = pids;
					}
					pids.Add(process.Id);
				}
				catch (Exception ex) when (IsExpectedPathReadFailure(ex))
				{
				}
				catch (Exception ex)
				{
					_logger.Debug($"Process path unavailable for {process.Id}: {ex.Message}");
				}
			}
		}
		return new ProcessSnapshot(running, pathToPids);
	}

	private static bool IsExpectedPathReadFailure(Exception ex)
	{
		return ex is UnauthorizedAccessException || ex is Win32Exception { NativeErrorCode: 5 } || ex is InvalidOperationException;
	}

	private static int NormalizeCloseGracePeriodMs(int value)
	{
		if (value <= 0)
		{
			return DefaultCloseGracePeriodMs;
		}
		return Math.Clamp(value, MinCloseGracePeriodMs, MaxCloseGracePeriodMs);
	}

	private sealed class ProcessSnapshot
	{
		public HashSet<int> LiveProcessIds { get; }

		public Dictionary<string, List<int>> ProcessIdsByPath { get; }

		public ProcessSnapshot(HashSet<int> liveProcessIds, Dictionary<string, List<int>> processIdsByPath)
		{
			LiveProcessIds = liveProcessIds;
			ProcessIdsByPath = processIdsByPath;
		}
	}
}
