using System.Windows.Input;

namespace CommunityToolkit.Mvvm.Input;

public interface IRelayCommand : ICommand
{
	void NotifyCanExecuteChanged();
}
