using Core.Enums;

namespace Core.Interfaces
{
    public interface ILoginService
    {
        event Action<ConnectionStatus>? StatusChanged;
        Task ValidateCredentialsAsync(IWebViewHost host);
        ConnectionStatus? Status { get; }
    }
}