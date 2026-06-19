namespace Core.Enums
{
    /// <summary>
    /// Represents the connection state of a platform login service.
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        /// No active session or credentials are present.
        /// </summary>
        NotConnected,
        /// <summary>
        /// Credentials or session tokens are being validated.
        /// </summary>
        Validating,
        /// <summary>
        /// The platform is authenticated and ready for drops operations.
        /// </summary>
        Connected,
        /// <summary>
        /// A login or reconnection attempt is in progress.
        /// </summary>
        Connecting
    }
}