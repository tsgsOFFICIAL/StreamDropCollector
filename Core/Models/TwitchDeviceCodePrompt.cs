namespace Core.Models
{
    /// <summary>
    /// Device-code OAuth details shown to the user during Twitch Helix authentication.
    /// </summary>
    /// <remarks>Passed to the UI prompt callback from <see cref="Interfaces.ITwitchHelixService.EnsureAuthenticatedAsync"/>.</remarks>
    /// <param name="VerificationUri">Base Twitch activation URI from the device-code response.</param>
    /// <param name="UserCode">Code the user enters on the activation page when needed.</param>
    /// <param name="ActivationUrl">URL to load in the auth WebView (prefers <c>verification_uri_complete</c>).</param>
    public sealed record TwitchDeviceCodePrompt(string VerificationUri, string UserCode, string ActivationUrl);
}