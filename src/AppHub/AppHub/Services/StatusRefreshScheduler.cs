using System;
using System.Threading;
using System.Threading.Tasks;
using AppHub.Infrastructure;
using AppHub.Models;

namespace AppHub.Services;

public sealed class StatusRefreshScheduler : IDisposable
{
	private const int MinForegroundRefreshMs = 1000;

	private const int MaxForegroundRefreshMs = 60000;

	private const int DefaultForegroundRefreshMs = 3000;

	private const int MinBackgroundRefreshMs = 10000;

	private const int MaxBackgroundRefreshMs = 120000;

	private readonly ProcessControlService _processService;

	private readonly AppSettings _settings;

	private readonly AppLogger _logger;

	private readonly SemaphoreSlim _refreshSignal = new SemaphoreSlim(0, int.MaxValue);

	private readonly object _sync = new object();

	private CancellationTokenSource? _cts;

	private Task? _worker;

	private Task _stoppingWorkers = Task.CompletedTask;

	private bool _isBackground;

	private bool _disposed;

	public StatusRefreshScheduler(ProcessControlService processService, AppSettings settings, AppLogger logger)
	{
		_processService = processService;
		_settings = settings;
		_logger = logger;
	}

	public void Start()
	{
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_worker != null)
			{
				return;
			}
			_cts = new CancellationTokenSource();
			CancellationToken token = _cts.Token;
			_worker = Task.Run(() => RunAsync(token));
		}
		RequestImmediateRefresh();
	}

	public void Stop()
	{
		StopCore();
	}

	private Task StopCore()
	{
		CancellationTokenSource? cts;
		Task? worker;
		Task completion;
		lock (_sync)
		{
			cts = _cts;
			worker = _worker;
			_cts = null;
			_worker = null;
			if (cts == null || worker == null)
			{
				return _stoppingWorkers;
			}
			completion = worker.ContinueWith(completed =>
			{
				try
				{
					if (completed.IsFaulted)
					{
						_logger.Warn($"Status refresh worker failed: {completed.Exception?.GetBaseException().Message}");
					}
				}
				finally
				{
					cts.Dispose();
				}
			}, TaskScheduler.Default);
			_stoppingWorkers = Task.WhenAll(_stoppingWorkers, completion);
			cts.Cancel();
			return _stoppingWorkers;
		}
	}

	public void SetIsBackground(bool isBackground)
	{
		bool changed = false;
		lock (_sync)
		{
			if (_isBackground != isBackground)
			{
				_isBackground = isBackground;
				changed = true;
			}
		}
		if (changed)
		{
			RequestImmediateRefresh();
		}
	}

	public void RequestImmediateRefresh()
	{
		lock (_sync)
		{
			if (_disposed || _worker == null)
			{
				return;
			}
			_refreshSignal.Release();
		}
	}

	private async Task RunAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			TimeSpan interval = GetCurrentInterval();
			try
			{
				await _refreshSignal.WaitAsync(interval, token);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				break;
			}
			if (token.IsCancellationRequested)
			{
				break;
			}
			try
			{
				await _processService.RefreshAllStatusAsync(token);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.Warn($"Status refresh failed: {ex.Message}");
			}
		}
	}

	private TimeSpan GetCurrentInterval()
	{
		bool isBackground;
		lock (_sync)
		{
			isBackground = _isBackground;
		}
		int foreground = NormalizeInterval(_settings.RefreshIntervalMs, MinForegroundRefreshMs, MaxForegroundRefreshMs, DefaultForegroundRefreshMs);
		long scaledBackground = (long)foreground * 4L;
		int background = (int)Math.Clamp(scaledBackground, MinBackgroundRefreshMs, MaxBackgroundRefreshMs);
		return TimeSpan.FromMilliseconds(isBackground ? background : foreground);
	}

	private static int NormalizeInterval(int value, int min, int max, int fallback)
	{
		if (value <= 0)
		{
			return fallback;
		}
		return Math.Clamp(value, min, max);
	}

	public void Dispose()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
		}
		Task completion = StopCore();
		_ = completion.ContinueWith(_ => _refreshSignal.Dispose(), TaskScheduler.Default);
	}
}
