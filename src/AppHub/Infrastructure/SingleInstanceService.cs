using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppHub.Infrastructure;

public sealed class SingleInstanceService : IDisposable
{
	private const int CommandLimit = 64;

	private const int ConnectionTimeoutMs = 5000;

	private static readonly string PipeName = $"AppHub.SingleInstance.{Process.GetCurrentProcess().SessionId}";

	private readonly Action<Guid?> _onCommand;

	private readonly Action<Exception> _onError;

	private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

	private readonly Task _listenerTask;

	private bool _disposed;

	public SingleInstanceService(Action<Guid?> onCommand, Action<Exception> onError)
	{
		_onCommand = onCommand;
		_onError = onError;
		_listenerTask = ListenAsync();
	}

	public static async Task SendAsync(Guid? appId)
	{
		using CancellationTokenSource timeout = new CancellationTokenSource(ConnectionTimeoutMs);
		using NamedPipeClientStream pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
			PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
		string command = appId.HasValue ? $"launch {appId.Value:D}\n" : "show\n";
		await pipe.WriteAsync(Encoding.ASCII.GetBytes(command), timeout.Token).ConfigureAwait(false);
		string response = await ReadMessageAsync(pipe, timeout.Token).ConfigureAwait(false);
		if (response != "ok")
		{
			throw new IOException("AppHub did not acknowledge the command.");
		}
	}

	private async Task ListenAsync()
	{
		try
		{
			while (!_shutdown.IsCancellationRequested)
			{
				using NamedPipeServerStream pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
					PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
				await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
				using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
				timeout.CancelAfter(ConnectionTimeoutMs);
				try
				{
					string command = await ReadMessageAsync(pipe, timeout.Token).ConfigureAwait(false);
					Guid? appId = ParseCommand(command);
					_onCommand(appId);
					await pipe.WriteAsync(Encoding.ASCII.GetBytes("ok\n"), timeout.Token).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
				{
					_onError(new TimeoutException("Single-instance command timed out."));
				}
				catch (Exception ex) when (ex is IOException || ex is FormatException)
				{
					_onError(ex);
				}
			}
		}
		catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_onError(ex);
		}
	}

	private static Guid? ParseCommand(string command)
	{
		if (command == "show")
		{
			return null;
		}
		if (command.StartsWith("launch ", StringComparison.Ordinal)
			&& Guid.TryParseExact(command.Substring("launch ".Length), "D", out Guid appId))
		{
			return appId;
		}
		throw new FormatException("Invalid single-instance command.");
	}

	private static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[CommandLimit];
		int length = 0;
		while (length < buffer.Length)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				throw new IOException("Single-instance connection closed before the command completed.");
			}
			if (buffer[length] == (byte)'\n')
			{
				return Encoding.ASCII.GetString(buffer, 0, length);
			}
			length += read;
		}
		throw new FormatException("Single-instance command exceeds the size limit.");
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_shutdown.Cancel();
		_ = _listenerTask.ContinueWith(_ => _shutdown.Dispose(), TaskScheduler.Default);
	}
}
