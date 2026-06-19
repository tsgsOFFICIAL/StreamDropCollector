using System.Runtime.CompilerServices;
using System.ComponentModel;

namespace UI.Models
{
    /// <summary>
    /// Bindable eligible streamer chip with avatar, live state, and profile image metadata.
    /// </summary>
    public sealed class EligibleStreamer : INotifyPropertyChanged
    {
        /// <summary>
        /// Creates a streamer chip from a channel login and optional profile image URL.
        /// </summary>
        /// <param name="login">Channel login slug.</param>
        /// <param name="profileImageUrl">Optional profile image URL from platform APIs.</param>
        public EligibleStreamer(string login, string? profileImageUrl = null)
        {
            Login = login;
            Name = login;
            Initials = BuildInitials(login);
            AvatarColorIndex = BuildColorIndex(login);
            ProfileImageUrl = profileImageUrl;
        }

        /// <summary>Channel login slug.</summary>
        public string Login { get; }

        /// <summary>Display name shown on the chip.</summary>
        public string Name { get; }

        /// <summary>Avatar fallback initials derived from the login.</summary>
        public string Initials { get; }

        /// <summary>Deterministic palette index for the avatar background brush.</summary>
        public int AvatarColorIndex { get; }

        private string? _profileImageUrl;

        /// <summary>
        /// Profile image URL from platform channel metadata (e.g. Kick <c>user.profile_pic</c>,
        /// Twitch <c>profile_image_url</c>), or <see langword="null"/> until fetched.
        /// </summary>
        public string? ProfileImageUrl
        {
            get => _profileImageUrl;
            set
            {
                string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (_profileImageUrl == normalized)
                    return;

                _profileImageUrl = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasProfileImage));
            }
        }

        /// <summary>Whether a non-empty profile image URL is available.</summary>
        public bool HasProfileImage => !string.IsNullOrWhiteSpace(ProfileImageUrl);

        private bool _isLive;

        /// <summary>Whether the streamer is currently live.</summary>
        public bool IsLive
        {
            get => _isLive;
            set
            {
                if (_isLive == value)
                    return;

                _isLive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOffline));
                OnPropertyChanged(nameof(ChipOpacity));
            }
        }

        /// <summary>Inverse of <see cref="IsLive"/> for binding convenience.</summary>
        public bool IsOffline => !IsLive;

        /// <summary>Chip opacity; reduced when the streamer is offline.</summary>
        public double ChipOpacity => IsLive ? 1.0 : 0.78;

        private bool _isClickable;

        /// <summary>Whether the chip accepts click input to expand or filter the list.</summary>
        public bool IsClickable
        {
            get => _isClickable;
            set
            {
                if (_isClickable == value)
                    return;

                _isClickable = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string BuildInitials(string name)
        {
            string clean = new string(name.Where(char.IsLetterOrDigit).ToArray());
            if (clean.Length <= 3)
                return clean;

            return clean[..2].ToUpperInvariant();
        }

        private static int BuildColorIndex(string name)
        {
            int hash = 0;
            foreach (char c in name)
                hash = c + ((hash << 5) - hash);

            return Math.Abs(hash) % 8;
        }
    }
}