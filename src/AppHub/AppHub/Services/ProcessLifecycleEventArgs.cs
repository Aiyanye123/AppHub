using System;

namespace AppHub.Services;

public sealed class ProcessLifecycleEventArgs : EventArgs
{
	public int ProcessId { get; }

	public string? ImagePath { get; }

	public DateTime Timestamp { get; }

	public ProcessLifecycleEventArgs(int processId, string? imagePath, DateTime timestamp)
	{
		ProcessId = processId;
		ImagePath = imagePath;
		Timestamp = timestamp;
	}
}
