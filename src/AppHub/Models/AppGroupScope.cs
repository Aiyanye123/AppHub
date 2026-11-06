using System;

namespace AppHub.Models;

public static class AppGroupScope
{
	public const string AllGroups = "__all_groups__";

	public static string Normalize(string? scope)
	{
		if (string.IsNullOrWhiteSpace(scope))
		{
			return AllGroups;
		}
		return scope.Trim().ToUpperInvariant();
	}
}
