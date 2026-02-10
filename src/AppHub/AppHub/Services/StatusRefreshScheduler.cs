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

	private bool _isBackground;

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
			if (_worker != null)
			{
				return;
			}
			_cts = new CancellationTokenSource();
			_worker = Task.Run(() => RunAsync(_cts.Token));
		}
		RequestImmediateRefresh();
	}

	public void Stop()
	{
		CancellationTokenSource? cts;
		Task? worker;
		lock (_sync)
		{
			cts = _cts;
			worker = _worker;
			_cts = null;
			_worker = null;
		}
		if (cts == null)
		{
			return;
		}
		cts.Cancel();
		RequestImmediateRefresh();
		try
		{
			worker?.Wait(TimeSpan.FromSeconds(2.0));
		}
		catch (AggregateException ex) when (AreCancellationExceptions(ex))
		{
		}
		finally
		{
			cts.Dispose();
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
			if (_worker == null)
			{
				return;
			}
		}
		_refreshSignal.Release();
	}

	private async Task RunAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			TimeSpan interval = GetCurrentInterval();
			try
			{
				Task delayTask = Task.Delay(interval, token);
				Task signalTask = _refreshSignal.WaitAsync(token);
				await Task.WhenAny(delayTask, signalTask);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			if (token.IsCancellationRequested)
			{
				break;
			}
			try
			{
				await _processService.RefreshAllStatusAsync();
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

	private static bool AreCancellationExceptions(AggregateException ex)
	{
		foreach (Exception inner in ex.InnerExceptions)
		{
			if (inner is not TaskCanceledException && inner is not OperationCanceledException)
			{
				return false;
			}
		}
		return true;
	}

	public void Dispose()
	{
		Stop();
		_refreshSignal.Dispose();
	}
}
