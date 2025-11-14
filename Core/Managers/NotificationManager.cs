using Microsoft.Toolkit.Uwp.Notifications;

namespace Core.Managers
{
    public class NotificationManager
    {
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
