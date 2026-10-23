namespace Core.Enums
{
    /// <summary>
    /// Result of inspecting a platform page for a login session.
    /// </summary>
    public enum LoginState
    {
        /// <summary>
        /// The page has not rendered enough to tell, or the script could not run (for example mid-navigation).
        /// </summary>
        Unknown,
        /// <summary>
        /// The page rendered and shows the platform's login button.
        /// </summary>
        LoggedOut,
        /// <summary>
        /// The page rendered and shows no login button.
        /// </summary>
        LoggedIn
    }
}