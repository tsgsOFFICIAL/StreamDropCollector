using System.Runtime.CompilerServices;
using System.ComponentModel;

namespace UI.Models
{
    public sealed class EligibleStreamer : INotifyPropertyChanged
    {
        public EligibleStreamer(string login, string? profileImageUrl = null)
        {
            Login = login;
            Name = login;
            Initials = BuildInitials(login);
            AvatarColorIndex = BuildColorIndex(login);
            ProfileImageUrl = profileImageUrl;
        }

        public string Login { get; }
        public string Name { get; }
        public string Initials { get; }
        public int AvatarColorIndex { get; }

        /// <summary>
        /// Kick channel API: <c>user.profile_pic</c>. Empty until live/channel metadata is fetched.
        /// </summary>
        private string? _profileImageUrl;
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

        public bool HasProfileImage => !string.IsNullOrWhiteSpace(ProfileImageUrl);

        private bool _isLive;
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

        public bool IsOffline => !IsLive;

        public double ChipOpacity => IsLive ? 1.0 : 0.78;

        private bool _isClickable;
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