using System.Threading.Tasks;

namespace CommunityToolkit.Mvvm.Input;

public interface IAsyncRelayCommand : IRelayCommand
{
	Task ExecuteAsync(object? parameter = null);
}
