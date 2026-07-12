namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// JavaScript executed in the Kick WebView on browse/category listing pages.
    /// </summary>
    internal static class KickGeneralDropDirectoryScripts
    {
        /// <summary>
        /// Returns up to N channel logins from the browse/category listing (page order reflects the page's sort).
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

              const cards = document.querySelectorAll('a[href^="/"].aspect-video');
              for (const card of cards) {
                pushLogin(card.getAttribute('href'));
              }

              return logins.join(',');
            })();
            """;
    }
}