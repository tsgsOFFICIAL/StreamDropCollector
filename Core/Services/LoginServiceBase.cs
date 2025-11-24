using Core.Interfaces;
using Core.Enums;

namespace Core.Services
{
    public abstract class LoginServiceBase : ILoginService
    {
        private ConnectionStatus _connectionStatus;
        public ConnectionStatus? Status
        {
            get => _connectionStatus;
            protected set
            {
                if (value.HasValue && _connectionStatus != value.Value)
                {
                    _connectionStatus = value.Value;
                    UpdateStatus(_connectionStatus);
                }
            }
        }

        protected LoginServiceBase()
        {
            StatusChanged += status => {
                _connectionStatus = status;
            };
        }

        /// <summary>
        /// Occurs when the connection status changes.
        /// </summary>
        /// <remarks>Subscribers are notified whenever the connection status transitions to a new state.
        /// Handlers receive the updated <see cref="ConnectionStatus"/> value as an argument. This event is typically
        /// used to monitor connectivity and respond to status changes in real time.</remarks>
        public event Action<ConnectionStatus>? StatusChanged;
        /// <summary>
        /// Raises the StatusChanged event to notify subscribers of a change in connection status.
        /// </summary>
        /// <remarks>This method should be called whenever the connection status changes to ensure that
        /// all registered listeners are notified. If there are no subscribers to the StatusChanged event, this method
        /// has no effect.</remarks>
        /// <param name="status">The new connection status to be provided to event subscribers.</param>
        protected void UpdateStatus(ConnectionStatus status) => StatusChanged?.Invoke(status);
        public abstract Task ValidateCredentialsAsync(IWebViewHost host);
        protected static async Task<string> GetPageHtmlAsync(IWebViewHost host)
        {
            string htmlRaw = await host.ExecuteScriptAsync("document.documentElement.outerHTML;");
            return System.Text.Json.JsonSerializer.Deserialize<string>(htmlRaw) ?? "";
        }
    }
}