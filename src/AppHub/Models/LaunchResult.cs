namespace AppHub.Models;

public sealed class LaunchResult
{
	public bool Success { get; init; }

	public int? ProcessId { get; init; }

	public string? ErrorMessage { get; init; }

	public int? ErrorCode { get; init; }
}
