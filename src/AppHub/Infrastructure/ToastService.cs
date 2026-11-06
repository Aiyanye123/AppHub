using System;
using System.Windows;

namespace AppHub.Infrastructure;

public enum ToastSeverity
{
	Info,
	Success,
	Warning,
	Error
}

public sealed record ToastMessage(
	string Message,
	ToastSeverity Severity = ToastSeverity.Info,
	string? ActionText = null,
	Action? ActionCallback = null,
	int DurationMs = 4000);

public static class ToastService
{
	public static event Action<ToastMessage>? ToastRequested;

	public static void Show(
		string message,
		ToastSeverity severity = ToastSeverity.Info,
		string? actionText = null,
		Action? actionCallback = null,
		int durationMs = 4000)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}
		Application? app = Application.Current;
		if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
		{
			app.Dispatcher.BeginInvoke(new Action(delegate
			{
				ToastRequested?.Invoke(new ToastMessage(message, severity, actionText, actionCallback, durationMs));
			}));
			return;
		}
		ToastRequested?.Invoke(new ToastMessage(message, severity, actionText, actionCallback, durationMs));
	}

	public static void ShowSuccess(string message, string? actionText = null, Action? actionCallback = null, int durationMs = 4000)
	{
		Show(message, ToastSeverity.Success, actionText, actionCallback, durationMs);
	}

	public static void ShowWarning(string message, int durationMs = 4500)
	{
		Show(message, ToastSeverity.Warning, null, null, durationMs);
	}

	public static void ShowError(string message, int durationMs = 5000)
	{
		Show(message, ToastSeverity.Error, null, null, durationMs);
	}
}
