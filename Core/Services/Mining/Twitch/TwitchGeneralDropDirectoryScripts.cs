namespace Core.Services.Mining.Twitch
{
    /// <summary>
    /// JavaScript executed in the Twitch WebView on drops-filtered directory pages.
    /// </summary>
    internal static class TwitchGeneralDropDirectoryScripts
    {
        /// <summary>
        /// Returns up to N channel logins from the directory listing (drops filter applied by the page URL).
        /// </summary>
        public static string GetTopStreamerLoginsJs(int limit) =>
            $$"""
            (() => {
              const limit = {{limit}};
              const seen = new Set();
              const logins = [];

              const pushLogin = (href) => {
                if (!href || logins.length >= limit) return;
                const path = href.startsWith('http')
                  ? new URL(href).pathname
                  : href;
                const login = path.replace(/^\//, '').split('/')[0];
                if (!login) return;
                const key = login.toLowerCase();
                if (seen.has(key)) return;
                seen.add(key);
                logins.push(login);
              };

              const firstItems = document.querySelectorAll('div[data-target="directory-first-item"]');
              for (const item of firstItems) {
                const link = item.querySelector('a[href^="/"]');
                if (link) pushLogin(link.getAttribute('href'));
              }

              const previewLinks = document.querySelectorAll('a[data-a-target="preview-card-image-link"]');
              for (const link of previewLinks) {
                pushLogin(link.getAttribute('href'));
              }

              return logins.join(',');
            })();
            """;
    }
}