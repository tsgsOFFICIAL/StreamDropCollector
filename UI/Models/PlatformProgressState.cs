using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI.Models
{
    public sealed class PlatformProgressState : INotifyPropertyChanged
    {
        public PlatformProgressState(string platformName, string brandBrushKey)
        {
            PlatformName = platformName;
            BrandBrushKey = brandBrushKey;
        }

        public string PlatformName { get; }
        public string BrandBrushKey { get; }

        private byte _campaignProgress;
        public byte CampaignProgress
        {
            get => _campaignProgress;
            set { _campaignProgress = value; OnPropertyChanged(); }
        }

        private byte _dropProgress;
        public byte DropProgress
        {
            get => _dropProgress;
            set { _dropProgress = value; OnPropertyChanged(); }
        }

        private string _campaignName = string.Empty;
        public string CampaignName
        {
            get => _campaignName;
            set { _campaignName = value; OnPropertyChanged(); }
        }

        private string _campaignImageUrl = string.Empty;
        public string CampaignImageUrl
        {
            get => _campaignImageUrl;
            set { _campaignImageUrl = value; OnPropertyChanged(); }
        }

        private string _dropName = string.Empty;
        public string DropName
        {
            get => _dropName;
            set { _dropName = value; OnPropertyChanged(); }
        }

        private string _dropImageUrl = string.Empty;
        public string DropImageUrl
        {
            get => _dropImageUrl;
            set { _dropImageUrl = value; OnPropertyChanged(); }
        }

        private string _watchedChannel = string.Empty;
        public string WatchedChannel
        {
            get => _watchedChannel;
            set { _watchedChannel = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}