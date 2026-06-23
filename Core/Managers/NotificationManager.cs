using Microsoft.Toolkit.Uwp.Notifications;

namespace Core.Managers
{
    /// <summary>
    /// Provides Windows toast notifications for application events.
    /// </summary>
    public class NotificationManager
    {
        /// <summary>
        /// Displays a Windows toast notification with the specified title and message.
        /// </summary>
        /// <param name="title">The title text shown in the notification.</param>
        /// <param name="message">The body text shown in the notification.</param>
        /// <param name="timeoutSeconds">The number of seconds before the notification expires. Defaults to 1 second.</param>
        public static void ShowNotification(string title, string message, double timeoutSeconds = 1)
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show(toast =>
                {
                    toast.ExpirationTime = DateTime.Now.AddSeconds(timeoutSeconds);
                });
        }
    }
}