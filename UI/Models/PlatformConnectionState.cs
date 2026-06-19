using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI.Models
{
    /// <summary>
    /// Bindable connection and login state for a streaming platform.
    /// </summary>
    public sealed class PlatformConnectionState : INotifyPropertyChanged
    {
        /// <summary>
        /// Initializes a new platform connection state with display metadata.
        /// </summary>
        /// <param name="platformName">Human-readable platform name.</param>
        /// <param name="brandBrushKey">Application resource key for the platform brand brush.</param>
        /// <param name="loginButtonLabel">Default label for the login button.</param>
        public PlatformConnectionState(string platformName, string brandBrushKey, string loginButtonLabel)
        {
            PlatformName = platformName;
            BrandBrushKey = brandBrushKey;
            _loginButtonText = loginButtonLabel;
        }

        /// <summary>Human-readable platform name.</summary>
        public string PlatformName { get; }

        /// <summary>Application resource key for the platform brand brush.</summary>
        public string BrandBrushKey { get; }

        private string _connectionStatus = "Not Connected";

        /// <summary>Current connection status text.</summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        private string _connectionColor = "Red";

        /// <summary>Brush resource key or color token for the connection indicator.</summary>
        public string ConnectionColor
        {
            get => _connectionColor;
            set { _connectionColor = value; OnPropertyChanged(); }
        }

        private string _loginButtonText;

        /// <summary>Label displayed on the platform login button.</summary>
        public string LoginButtonText
        {
            get => _loginButtonText;
            set { _loginButtonText = value; OnPropertyChanged(); }
        }

        private bool _isLoginEnabled = false;

        /// <summary>Whether the login button is enabled.</summary>
        public bool IsLoginEnabled
        {
            get => _isLoginEnabled;
            set { _isLoginEnabled = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}