using Core.Enums;

namespace Core.Interfaces
{
    /// <summary>
    /// Defines a contract for validating platform login credentials and reporting connection status.
    /// </summary>
    public interface ILoginService
    {
        /// <summary>
        /// Occurs when the connection status changes.
        /// </summary>
        /// <remarks>Subscribers receive the updated <see cref="ConnectionStatus"/> whenever the login
        /// state transitions.</remarks>
        event Action<ConnectionStatus>? StatusChanged;
        /// <summary>
        /// Asynchronously validates stored or interactive login credentials for the platform.
        /// </summary>
        /// <param name="host">The web view host used to access the platform login page and session data. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous validation operation.</returns>
        Task ValidateCredentialsAsync(IWebViewHost host);
        /// <summary>
        /// Gets the current connection status for the platform login session.
        /// </summary>
        ConnectionStatus? Status { get; }
    }
}