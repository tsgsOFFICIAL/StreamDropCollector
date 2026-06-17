using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI.Models
{
    public sealed class PlatformConnectionState : INotifyPropertyChanged
    {
        public PlatformConnectionState(string platformName, string brandBrushKey, string loginButtonLabel)
        {
            PlatformName = platformName;
            BrandBrushKey = brandBrushKey;
            _loginButtonText = loginButtonLabel;
        }

        public string PlatformName { get; }
        public string BrandBrushKey { get; }

        private string _connectionStatus = "Not Connected";
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        private string _connectionColor = "Red";
        public string ConnectionColor
        {
            get => _connectionColor;
            set { _connectionColor = value; OnPropertyChanged(); }
        }

        private string _loginButtonText;
        public string LoginButtonText
        {
            get => _loginButtonText;
            set { _loginButtonText = value; OnPropertyChanged(); }
        }

        private bool _isLoginEnabled = false;
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