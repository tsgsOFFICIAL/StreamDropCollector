using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI.Models
{
    /// <summary>
    /// Bindable mining progress for a platform's active campaign and drop.
    /// </summary>
    public sealed class PlatformProgressState : INotifyPropertyChanged
    {
        /// <summary>
        /// Initializes platform progress state with display metadata.
        /// </summary>
        /// <param name="platformName">Human-readable platform name.</param>
        /// <param name="brandBrushKey">Application resource key for the platform brand brush.</param>
        public PlatformProgressState(string platformName, string brandBrushKey)
        {
            PlatformName = platformName;
            BrandBrushKey = brandBrushKey;
        }

        /// <summary>Human-readable platform name.</summary>
        public string PlatformName { get; }

        /// <summary>Application resource key for the platform brand brush.</summary>
        public string BrandBrushKey { get; }

        private byte _campaignProgress;

        /// <summary>Campaign completion percentage (0–100).</summary>
        public byte CampaignProgress
        {
            get => _campaignProgress;
            set { _campaignProgress = value; OnPropertyChanged(); }
        }

        private byte _dropProgress;

        /// <summary>Current drop completion percentage (0–100).</summary>
        public byte DropProgress
        {
            get => _dropProgress;
            set { _dropProgress = value; OnPropertyChanged(); }
        }

        private string _campaignName = string.Empty;

        /// <summary>Display name of the active campaign.</summary>
        public string CampaignName
        {
            get => _campaignName;
            set { _campaignName = value; OnPropertyChanged(); }
        }

        private string _campaignImageUrl = string.Empty;

        /// <summary>Image URL for the active campaign.</summary>
        public string CampaignImageUrl
        {
            get => _campaignImageUrl;
            set { _campaignImageUrl = value; OnPropertyChanged(); }
        }

        private string _dropName = string.Empty;

        /// <summary>Display name of the current drop.</summary>
        public string DropName
        {
            get => _dropName;
            set { _dropName = value; OnPropertyChanged(); }
        }

        private string _dropImageUrl = string.Empty;

        /// <summary>Image URL for the current drop.</summary>
        public string DropImageUrl
        {
            get => _dropImageUrl;
            set { _dropImageUrl = value; OnPropertyChanged(); }
        }

        private string _minedChannel = string.Empty;

        /// <summary>Channel login currently being mined.</summary>
        public string MinedChannel
        {
            get => _minedChannel;
            set { _minedChannel = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}