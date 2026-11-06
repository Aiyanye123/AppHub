using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using AppHub.Helpers;

namespace AppHub.Services;

public sealed class StorageService : IDisposable
{
	private const string ConfigFileName = "config.json";

	private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly object _sync = new object();

	private readonly string _appDataDirectory;

	private Timer? _debounceTimer;

	private object? _pendingData;

	private bool _disposed;

	public StorageService()
		: this(PathHelper.GetAppDataDirectory())
	{
	}

	public StorageService(string appDataDirectory)
	{
		if (string.IsNullOrWhiteSpace(appDataDirectory))
		{
			throw new ArgumentException("App data directory is required.", nameof(appDataDirectory));
		}

		_appDataDirectory = Path.GetFullPath(appDataDirectory);
	}

	public T? Load<T>()
	{
		string path = GetPath(ConfigFileName);
		if (TryLoad(path, out T? value))
		{
			return value;
		}

		if (File.Exists(path))
		{
			File.Copy(path, path + ".corrupt", overwrite: true);
		}

		string backupPath = GetBackupPath(path);
		if (!TryLoad(backupPath, out value))
		{
			return default;
		}

		File.Copy(backupPath, path, overwrite: true);
		return value;
	}

	public void Save<T>(T data)
	{
		string path = GetPath(ConfigFileName);
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
		PathHelper.EnsureDirectory(_appDataDirectory);
		return Path.Combine(_appDataDirectory, filename);
	}

	public void FlushNow()
	{
		FlushPending();
	}

	private void FlushPending()
	{
		object? data;
		lock (_sync)
		{
			data = _pendingData;
			_pendingData = null;
		}
		if (data != null)
		{
			string path = GetPath(ConfigFileName);
			WriteFile(path, data);
		}
	}

	private bool TryLoad<T>(string path, out T? value)
	{
		value = default;
		if (!File.Exists(path))
		{
			return false;
		}

		try
		{
			string json = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(json))
			{
				return false;
			}

			value = JsonSerializer.Deserialize<T>(json, _jsonOptions);
			return value != null;
		}
		catch (JsonException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private void WriteFile<T>(string path, T data)
	{
		PathHelper.EnsureDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Config path invalid."));
		string tempPath = path + ".tmp";
		string json = JsonSerializer.Serialize(data, _jsonOptions);
		File.WriteAllText(tempPath, json);
		if (File.Exists(path))
		{
			File.Copy(path, GetBackupPath(path), overwrite: true);
		}
		File.Move(tempPath, path, overwrite: true);
	}

	private static string GetBackupPath(string path)
	{
		return path + ".bak";
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
