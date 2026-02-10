using System;
using System.Threading.Tasks;

namespace CommunityToolkit.Mvvm.Input;

public sealed class AsyncRelayCommand : IAsyncRelayCommand
{
	private readonly Func<Task> _executeAsync;

	private readonly Func<bool>? _canExecute;

	private bool _isRunning;

	public event EventHandler? CanExecuteChanged;

	public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
	{
		_executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
		_canExecute = canExecute;
	}

	public bool CanExecute(object? parameter)
	{
		if (_isRunning)
		{
			return false;
		}
		return _canExecute?.Invoke() ?? true;
	}

	public async void Execute(object? parameter)
	{
		await ExecuteAsync(parameter);
	}

	public async Task ExecuteAsync(object? parameter = null)
	{
		if (!CanExecute(parameter))
		{
			return;
		}
		try
		{
			_isRunning = true;
			NotifyCanExecuteChanged();
			await _executeAsync();
		}
		finally
		{
			_isRunning = false;
			NotifyCanExecuteChanged();
		}
	}

	public void NotifyCanExecuteChanged()
	{
		this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
