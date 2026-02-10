using System;
using System.Collections.Generic;
using AppHub.Models;

namespace AppHub.Services;

public sealed class ProcessStatusesChangedEventArgs : EventArgs
{
	public IReadOnlyDictionary<Guid, ProcessStatus> Statuses { get; }

	public ProcessStatusesChangedEventArgs(IReadOnlyDictionary<Guid, ProcessStatus> statuses)
	{
		Statuses = statuses;
	}
}
