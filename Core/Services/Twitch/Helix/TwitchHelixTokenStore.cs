using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.Services.Twitch.Helix
{
    /// <summary>
    /// Persists the Twitch Helix refresh token under the SDC app-data folder (DPAPI-protected on Windows).
    /// </summary>
    internal static class TwitchHelixTokenStore
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Stream Drop Collector");

        private static readonly string FilePath = Path.Combine(FolderPath, "helix-auth.dat");

        public static void SaveRefreshToken(string refreshToken)
        {
            Directory.CreateDirectory(FolderPath);
            byte[] plain = Encoding.UTF8.GetBytes(refreshToken);
            byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, protectedBytes);
        }

        public static string? LoadRefreshToken()
        {
            if (!File.Exists(FilePath))
                return TryMigrateLegacyPlaintext();

            try
            {
                byte[] protectedBytes = File.ReadAllBytes(FilePath);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);

            string legacyPath = Path.Combine(FolderPath, "helix-auth.json");
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }

        private static string? TryMigrateLegacyPlaintext()
        {
            string legacyPath = Path.Combine(FolderPath, "helix-auth.json");
            if (!File.Exists(legacyPath))
                return null;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
                string? refreshToken = doc.RootElement.TryGetProperty("RefreshToken", out JsonElement token)
                    ? token.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    SaveRefreshToken(refreshToken);
                    File.Delete(legacyPath);
                }

                return refreshToken;
            }
            catch
            {
                return null;
            }
        }
    }
}