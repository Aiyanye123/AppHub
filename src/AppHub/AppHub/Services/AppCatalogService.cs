using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppHub.Helpers;
using AppHub.Infrastructure;
using AppHub.Models;

namespace AppHub.Services;

public sealed class AppCatalogService
{
	private readonly StorageService _storage;

	private readonly AppConfig _config;

	private readonly AppLogger _logger;

	public AppCatalogService(StorageService storage, AppConfig config, AppLogger logger)
	{
		_storage = storage;
		_config = config;
		_logger = logger;
	}

	public IReadOnlyList<ApplicationItem> GetAllApps()
	{
		return _config.Apps.OrderBy((ApplicationItem app) => app.SortIndex).ToList();
	}

	public ApplicationItem? GetAppById(Guid id)
	{
		return _config.Apps.FirstOrDefault((ApplicationItem app) => app.Id == id);
	}

	public ApplicationItem AddApp(string path)
	{
		ApplicationItem item = BuildItemFromPath(path);
		item.SortIndex = ((_config.Apps.Count != 0) ? (_config.Apps.Max((ApplicationItem a) => a.SortIndex) + 1) : 0);
		_config.Apps.Add(item);
		_storage.ScheduleSave(_config);
		return item;
	}

	public bool IsSupportedPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string normalizedPath = path.Trim();
		if (Directory.Exists(normalizedPath))
		{
			return true;
		}
		if (!File.Exists(normalizedPath))
		{
			return false;
		}
		string extension = Path.GetExtension(normalizedPath);
		return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
	}

	public IReadOnlyList<string> GetSupportedInputPaths(IEnumerable<string> paths)
	{
		ArgumentNullException.ThrowIfNull(paths, "paths");
		List<string> result = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in paths)
		{
			if (!IsSupportedPath(path))
			{
				continue;
			}
			string normalizedPath = PathHelper.NormalizePath(path.Trim());
			if (seen.Add(normalizedPath))
			{
				result.Add(path.Trim());
			}
		}
		return result;
	}

	public void UpdateApp(ApplicationItem item)
	{
		ApplicationItem? obj = GetAppById(item.Id) ?? throw new InvalidOperationException("App not found.");
		obj.DisplayName = item.DisplayName;
		obj.TargetPath = item.TargetPath;
		obj.ShortcutPath = item.ShortcutPath;
		obj.Arguments = item.Arguments;
		obj.WorkingDirectory = item.WorkingDirectory;
		obj.IconSource = item.IconSource;
		obj.CustomIconPath = item.IconSource == IconSource.Custom ? item.CustomIconPath : null;
		obj.GroupName = NormalizeGroupName(item.GroupName);
		obj.TrackProcess = item.TrackProcess;
		obj.IsPinned = item.IsPinned;
		obj.GroupStates = NormalizeGroupStates(item.GroupStates);
		_storage.ScheduleSave(_config);
	}

	public void SetPinned(Guid id, string groupScope, bool isPinned)
	{
		ApplicationItem item = GetAppById(id) ?? throw new InvalidOperationException("App not found.");
		string normalizedScope = AppGroupScope.Normalize(groupScope);
		item.SetPinned(normalizedScope, isPinned);
		if (string.Equals(normalizedScope, AppGroupScope.AllGroups, StringComparison.Ordinal))
		{
			item.IsPinned = isPinned;
		}
		_storage.ScheduleSave(_config);
	}

	public void RemoveApp(Guid id)
	{
		_config.Apps.RemoveAll((ApplicationItem app) => app.Id == id);
		_storage.ScheduleSave(_config);
	}

	public void ReorderApps(IReadOnlyList<Guid> ids)
	{
		for (int i = 0; i < ids.Count; i++)
		{
			ApplicationItem app = GetAppById(ids[i]);
			if (app != null)
			{
				app.SortIndex = i;
			}
		}
		_storage.ScheduleSave(_config);
	}

	public IReadOnlyList<ApplicationItem> SearchApps(string keyword)
	{
		if (string.IsNullOrWhiteSpace(keyword))
		{
			return GetAllApps();
		}
		string term = keyword.Trim();
		return (from app in _config.Apps
			where app.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) || app.GroupName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
			orderby app.SortIndex
			select app).ToList();
	}

	public IReadOnlyList<string> GetGroupNames()
	{
		return _config.Apps.Select((ApplicationItem app) => NormalizeGroupName(app.GroupName))
			.Where((string groupName) => !string.IsNullOrWhiteSpace(groupName))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy((string groupName) => groupName, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private ApplicationItem BuildItemFromPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", "path");
		}
		string normalizedPath = path.Trim();
		if (Directory.Exists(normalizedPath))
		{
			string directoryName = new DirectoryInfo(normalizedPath).Name;
			return new ApplicationItem
			{
				DisplayName = (string.IsNullOrWhiteSpace(directoryName) ? normalizedPath : directoryName),
				SourceType = SourceType.Folder,
				TargetPath = normalizedPath,
				WorkingDirectory = normalizedPath,
				TrackProcess = false
			};
		}
		if (!File.Exists(normalizedPath))
		{
			throw new FileNotFoundException("Path does not exist.", normalizedPath);
		}
		string extension = Path.GetExtension(normalizedPath);
		if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			ShortcutInfo info = ShortcutHelper.Resolve(normalizedPath);
			return new ApplicationItem
			{
				DisplayName = Path.GetFileNameWithoutExtension(normalizedPath),
				SourceType = SourceType.Shortcut,
				TargetPath = info.TargetPath,
				ShortcutPath = normalizedPath,
				Arguments = info.Arguments,
				WorkingDirectory = info.WorkingDirectory
			};
		}
		if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return new ApplicationItem
			{
				DisplayName = Path.GetFileNameWithoutExtension(normalizedPath),
				SourceType = SourceType.Exe,
				TargetPath = normalizedPath,
				WorkingDirectory = (Path.GetDirectoryName(normalizedPath) ?? string.Empty)
			};
		}
		_logger.Warn("Unsupported file type: " + normalizedPath);
		throw new InvalidOperationException("Unsupported file type: " + normalizedPath);
	}

	private static string NormalizeGroupName(string? groupName)
	{
		return string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName.Trim();
	}

	private static Dictionary<string, ApplicationGroupState> NormalizeGroupStates(Dictionary<string, ApplicationGroupState>? source)
	{
		Dictionary<string, ApplicationGroupState> result = new Dictionary<string, ApplicationGroupState>();
		if (source == null)
		{
			return result;
		}
		foreach (KeyValuePair<string, ApplicationGroupState> pair in source)
		{
			string scope = AppGroupScope.Normalize(pair.Key);
			result[scope] = new ApplicationGroupState
			{
				IsPinned = pair.Value?.IsPinned ?? false
			};
		}
		return result;
	}
}
