namespace AppHub.Models;

public sealed class CloseResult
{
	public bool Success { get; init; }

	public string? ErrorMessage { get; init; }
}
