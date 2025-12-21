using System.Windows.Media.Imaging;
using System.Net.Http;
using System.Windows;
using System.IO;
using UI.Views;

namespace UI
{
    public static class ImageEx
    {
        public static readonly DependencyProperty SourceUrlProperty = DependencyProperty.RegisterAttached("SourceUrl", typeof(string), typeof(ImageEx), new PropertyMetadata(null, OnSourceUrlChanged));

        public static void SetSourceUrl(DependencyObject obj, string value) => obj.SetValue(SourceUrlProperty, value);

        public static string GetSourceUrl(DependencyObject obj) => (string)obj.GetValue(SourceUrlProperty);

        private static async void OnSourceUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not System.Windows.Controls.Image img)
                return;

            string? url = e.NewValue as string;
            if (string.IsNullOrWhiteSpace(url))
                return;

            img.Source = null;
            img.Opacity = .3;

            // ✔ Attempt normal fetch first
            byte[]? bytes = await TryHttpFetch(url);

            // ❌ 403 or blocked -> use WebView2
            if (bytes == null)
            {
                HiddenWebViewHost webView = new HiddenWebViewHost();
                try
                {
                    await webView.EnsureInitializedAsync();
                    bytes = await webView.FetchImageBytesAsync(url);
                }
                finally
                {
                    webView.Close();
                    webView.Dispose();
                }
            }

            if (bytes == null || bytes.Length == 0)
            {
                img.Opacity = 1;
                return;
            }

            BitmapImage? bitmap = null;

            await Task.Run(() => {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.None;
                
                using MemoryStream ms = new MemoryStream(bytes);
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                bitmap = bmp;
            });

            await img.Dispatcher.InvokeAsync(() =>
            {
                img.Source = bitmap;
                img.Opacity = 1;
            });
        }

        private static async Task<byte[]?> TryHttpFetch(string url)
        {
            try
            {
                using HttpClient http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/122 Safari/537.36");

                return await http.GetByteArrayAsync(url);
            }
            catch
            {
                return null;
            }
        }
    }
}