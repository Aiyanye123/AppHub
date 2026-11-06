using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Shell;
using AppHub.Models;
using AppHub.Services;

namespace AppHub.Infrastructure;

public sealed class JumpListService : IDisposable
{
	private readonly TrayIconService _trayIconService;

	private readonly JumpList _jumpList = new JumpList();

	private readonly HashSet<Guid> _removedAppIds = new HashSet<Guid>();

	private readonly string? _executablePath;

	private string? _lastSignature;

	private bool _disposed;

	public JumpListService(TrayIconService trayIconService)
	{
		_trayIconService = trayIconService;
		_executablePath = ResolveExecutablePath();
		if (_executablePath == null)
		{
			AppServices.Logger.Warn("Jump List unavailable: AppHub executable could not be located.");
			return;
		}
		_jumpList.ShowFrequentCategory = false;
		_jumpList.ShowRecentCategory = false;
		_jumpList.JumpItemsRemovedByUser += OnJumpItemsRemovedByUser;
		_jumpList.JumpItemsRejected += OnJumpItemsRejected;
		AppServices.Catalog.Changed += OnCatalogChanged;
		AppServices.TraySettingsChanged += OnCatalogChanged;
		AppServices.ProcessService.ProcessStatusesChanged += OnProcessStatusesChanged;
		try
		{
			JumpList.SetJumpList(Application.Current, _jumpList);
			Refresh();
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Jump List initialization failed", ex);
		}
	}

	private static string? ResolveExecutablePath()
	{
		string? processPath = Environment.ProcessPath;
		if (!string.IsNullOrWhiteSpace(processPath)
			&& !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
		{
			return processPath;
		}
		string appHostPath = Path.Combine(AppContext.BaseDirectory, "AppHub.exe");
		return File.Exists(appHostPath) ? appHostPath : null;
	}

	private void OnCatalogChanged(object? sender, EventArgs e)
	{
		Refresh();
	}

	private void OnProcessStatusesChanged(object? sender, ProcessStatusesChangedEventArgs e)
	{
		Refresh();
	}

	private void Refresh()
	{
		if (_disposed || _executablePath == null)
		{
			return;
		}
		try
		{
			List<JumpTask> tasks = new List<JumpTask>();
			StringBuilder signature = new StringBuilder();
			foreach (ApplicationItem app in _trayIconService.GetFrequentApps())
			{
				if (_removedAppIds.Contains(app.Id))
				{
					continue;
				}
				string title = TrayIconService.ResolveTrayAppText(app);
				signature.Append(app.Id).Append(':').Append(title.Length).Append(':').Append(title);
				tasks.Add(new JumpTask
				{
					Title = title,
					CustomCategory = "常用",
					ApplicationPath = _executablePath,
					Arguments = $"--launch {app.Id:D}",
					WorkingDirectory = Path.GetDirectoryName(_executablePath)
				});
			}
			string currentSignature = signature.ToString();
			if (string.Equals(currentSignature, _lastSignature, StringComparison.Ordinal))
			{
				return;
			}
			_jumpList.JumpItems.Clear();
			foreach (JumpTask task in tasks)
			{
				_jumpList.JumpItems.Add(task);
			}
			_jumpList.Apply();
			_lastSignature = currentSignature;
		}
		catch (Exception ex)
		{
			AppServices.Logger.Error("Jump List refresh failed", ex);
		}
	}

	private void OnJumpItemsRemovedByUser(object? sender, JumpItemsRemovedEventArgs e)
	{
		foreach (JumpItem item in e.RemovedItems)
		{
			if (item is JumpTask task)
			{
				CommandLineOptions options = CommandLineOptions.Parse(task.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
				if (options.LaunchAppId.HasValue)
				{
					_removedAppIds.Add(options.LaunchAppId.Value);
				}
			}
		}
	}

	private void OnJumpItemsRejected(object? sender, JumpItemsRejectedEventArgs e)
	{
		AppServices.Logger.Warn($"Jump List items rejected: {string.Join(", ", e.RejectionReasons)}");
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_jumpList.JumpItemsRemovedByUser -= OnJumpItemsRemovedByUser;
		_jumpList.JumpItemsRejected -= OnJumpItemsRejected;
		AppServices.Catalog.Changed -= OnCatalogChanged;
		AppServices.TraySettingsChanged -= OnCatalogChanged;
		AppServices.ProcessService.ProcessStatusesChanged -= OnProcessStatusesChanged;
	}
}
