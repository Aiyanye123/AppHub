using System;
using System.Collections.Generic;

namespace AppHub.Models;

public sealed class ProcessStatus
{
	public bool IsRunning { get; set; }

	public List<int> MatchedProcessIds { get; set; } = new List<int>();

	public DateTime? LastSeenTime { get; set; }
}
