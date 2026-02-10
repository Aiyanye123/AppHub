using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppHub.Helpers;

namespace AppHub.Infrastructure;

public sealed class AppLogger
{
	private readonly object _sync = new object();

	private string _logDirectory;

	private long _maxLogSizeBytes;

	public AppLogger(string logDirectory)
	{
		_logDirectory = logDirectory;
	}

	public void SetLogDirectory(string logDirectory)
	{
		_logDirectory = logDirectory;
	}

	public void SetLogMaxSizeBytes(long maxLogSizeBytes)
	{
		_maxLogSizeBytes = maxLogSizeBytes;
	}

	public void Debug(string message)
	{
		Log(LogLevel.Debug, message);
	}

	public void Info(string message)
	{
		Log(LogLevel.Info, message);
	}

	public void Warn(string message)
	{
		Log(LogLevel.Warn, message);
	}

	public void Error(string message, Exception? ex = null)
	{
		Log(LogLevel.Error, message, ex);
	}

	public void Fatal(string message, Exception? ex = null)
	{
		Log(LogLevel.Fatal, message, ex);
	}

	public int DeleteAllLogs()
	{
		lock (_sync)
		{
			if (!Directory.Exists(_logDirectory))
			{
				return 0;
			}
			int deleted = 0;
			foreach (string path in Directory.EnumerateFiles(_logDirectory, "*.log", SearchOption.TopDirectoryOnly))
			{
				try
				{
					File.Delete(path);
					deleted++;
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
			return deleted;
		}
	}

	private void Log(LogLevel level, string message, Exception? ex = null)
	{
		DateTime timestamp = DateTime.Now;
		string line = $"[{timestamp:HH:mm:ss}] [{level}] {message}";
		if (ex != null)
		{
			line += $" | {ex}";
		}
		lock (_sync)
		{
			PathHelper.EnsureDirectory(_logDirectory);
			string fileName = $"{timestamp:yyyy-MM-dd}.log";
			string path = Path.Combine(_logDirectory, fileName);
			File.AppendAllText(path, line + Environment.NewLine);
			EnforceLogLimit(path);
		}
	}

	private void EnforceLogLimit(string currentLogPath)
	{
		if (_maxLogSizeBytes <= 0)
		{
			return;
		}
		List<FileInfo> files = new DirectoryInfo(_logDirectory).EnumerateFiles("*.log", SearchOption.TopDirectoryOnly).ToList();
		long totalSize = 0L;
		foreach (FileInfo file in files)
		{
			totalSize += file.Length;
		}
		if (totalSize <= _maxLogSizeBytes)
		{
			return;
		}
		if (File.Exists(currentLogPath))
		{
			try
			{
				File.Delete(currentLogPath);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}
