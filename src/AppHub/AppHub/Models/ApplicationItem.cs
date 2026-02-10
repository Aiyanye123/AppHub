using System;
using System.Collections.Generic;
using System.Linq;

namespace AppHub.Models;

public sealed class ApplicationItem
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string DisplayName { get; set; } = string.Empty;

	public SourceType SourceType { get; set; }

	public string TargetPath { get; set; } = string.Empty;

	public string? ShortcutPath { get; set; }

	public string Arguments { get; set; } = string.Empty;

	public string WorkingDirectory { get; set; } = string.Empty;

	public IconSource IconSource { get; set; }

	public string? CustomIconPath { get; set; }

	public int SortIndex { get; set; }

	public Guid? GroupId { get; set; }

	public string GroupName { get; set; } = string.Empty;

	public DateTime? LastLaunchTime { get; set; }

	public bool TrackProcess { get; set; } = true;

	public bool IsPinned { get; set; }

	public Dictionary<string, ApplicationGroupState> GroupStates { get; set; } = new Dictionary<string, ApplicationGroupState>();

	public bool GetPinned(string scope)
	{
		string key = AppGroupScope.Normalize(scope);
		return GroupStates != null && GroupStates.TryGetValue(key, out ApplicationGroupState? state) && state.IsPinned;
	}

	public void SetPinned(string scope, bool isPinned)
	{
		string key = AppGroupScope.Normalize(scope);
		GroupStates ??= new Dictionary<string, ApplicationGroupState>();
		if (!GroupStates.TryGetValue(key, out ApplicationGroupState? state))
		{
			state = new ApplicationGroupState();
			GroupStates[key] = state;
		}
		state.IsPinned = isPinned;
	}

	public ApplicationItem Clone()
	{
		return new ApplicationItem
		{
			Id = Id,
			DisplayName = DisplayName,
			SourceType = SourceType,
			TargetPath = TargetPath,
			ShortcutPath = ShortcutPath,
			Arguments = Arguments,
			WorkingDirectory = WorkingDirectory,
			IconSource = IconSource,
			CustomIconPath = CustomIconPath,
			SortIndex = SortIndex,
			GroupId = GroupId,
			GroupName = GroupName,
			LastLaunchTime = LastLaunchTime,
			TrackProcess = TrackProcess,
			IsPinned = IsPinned,
			GroupStates = (GroupStates ?? new Dictionary<string, ApplicationGroupState>()).ToDictionary((KeyValuePair<string, ApplicationGroupState> kv) => kv.Key, (KeyValuePair<string, ApplicationGroupState> kv) => new ApplicationGroupState
			{
				IsPinned = kv.Value?.IsPinned ?? false
			})
		};
	}
}
