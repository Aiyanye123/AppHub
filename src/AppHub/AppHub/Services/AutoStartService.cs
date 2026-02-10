using System.Diagnostics;
using AppHub.Helpers;
using AppHub.Infrastructure;

namespace AppHub.Services;

public sealed class AutoStartService
{
	private const string AppName = "AppHub";

	private readonly AppLogger _logger;

	public AutoStartService(AppLogger logger)
	{
		_logger = logger;
	}

	public bool IsEnabled()
	{
		return RegistryHelper.IsAutoStartEnabled("AppHub");
	}

	public void Enable()
	{
		string path = GetStartupPath();
		RegistryHelper.SetAutoStart("AppHub", path, string.Empty);
		_logger.Info("Auto-start enabled.");
	}

	public void Disable()
	{
		RegistryHelper.RemoveAutoStart("AppHub");
		_logger.Info("Auto-start disabled.");
	}

	public string GetStartupPath()
	{
		return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
	}
}
