using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using AppHub.Helpers;

namespace AppHub.Services;

public sealed class StorageService : IDisposable
{
	private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly object _sync = new object();

	private Timer? _debounceTimer;

	private object? _pendingData;

	private bool _disposed;

	public T? Load<T>()
	{
		string path = GetPath("config.json");
		if (!File.Exists(path))
		{
			return default(T);
		}
		try
		{
			return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions);
		}
		catch
		{
			string backup = path + ".corrupt";
			File.Copy(path, backup, overwrite: true);
			return default(T);
		}
	}

	public void Save<T>(T data)
	{
		string path = GetPath("config.json");
		WriteFile(path, data);
	}

	public void ScheduleSave<T>(T data, int debounceMs = 500)
	{
		lock (_sync)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			_pendingData = data;
			if (_debounceTimer == null)
			{
				_debounceTimer = new Timer(delegate
				{
					FlushPending();
				}, null, -1, -1);
			}
			_debounceTimer.Change(debounceMs, -1);
		}
	}

	public string GetPath(string filename)
	{
		string appDataDirectory = PathHelper.GetAppDataDirectory();
		PathHelper.EnsureDirectory(appDataDirectory);
		return Path.Combine(appDataDirectory, filename);
	}

	public void FlushNow()
	{
		FlushPending();
	}

	private void FlushPending()
	{
		object data;
		lock (_sync)
		{
			data = _pendingData;
			_pendingData = null;
		}
		if (data != null)
		{
			string path = GetPath("config.json");
			WriteFile(path, data);
		}
	}

	private void WriteFile<T>(string path, T data)
	{
		PathHelper.EnsureDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Config path invalid."));
		string text = path + ".tmp";
		string json = JsonSerializer.Serialize(data, _jsonOptions);
		File.WriteAllText(text, json);
		File.Move(text, path, overwrite: true);
	}

	public void Dispose()
	{
		Timer? timer;
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			timer = _debounceTimer;
			_debounceTimer = null;
		}
		timer?.Dispose();
		FlushPending();
	}
}
