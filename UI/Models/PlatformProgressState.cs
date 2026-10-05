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
            set { _campaignProgress = value; OnPropertyChanged(); NotifyLevelDerived(); }
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
            set { _campaignImageUrl = value; OnPropertyChanged(); NotifyLevelDerived(); }
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

        private bool _isLevelFarming;
        private string _levelText = string.Empty;
        private double _levelPercent;
        private string _levelBadgeUrl = string.Empty;

        /// <summary>True while the active "campaign" is level farming, so the campaign bar shows level progress instead.</summary>
        public bool IsLevelFarming
        {
            get => _isLevelFarming;
            set { _isLevelFarming = value; NotifyLevelDerived(); }
        }

        /// <summary>Level summary (for example "Level 5 - 1,200 / 4,800 XP"); empty when unknown.</summary>
        public string LevelText
        {
            get => _levelText;
            set { _levelText = value; NotifyLevelDerived(); }
        }

        /// <summary>Completion percentage (0-100) of the current level.</summary>
        public double LevelPercent
        {
            get => _levelPercent;
            set { _levelPercent = value; NotifyLevelDerived(); }
        }

        /// <summary>Kick's level badge image URL.</summary>
        public string LevelBadgeUrl
        {
            get => _levelBadgeUrl;
            set { _levelBadgeUrl = value; NotifyLevelDerived(); }
        }

        /// <summary>Value for the campaign bar: level progress while farming, otherwise campaign progress.</summary>
        public double DisplayCampaignProgress => IsLevelFarming && !string.IsNullOrEmpty(LevelText) ? LevelPercent : CampaignProgress;

        /// <summary>Caption under the campaign bar.</summary>
        public string CampaignProgressText => IsLevelFarming && !string.IsNullOrEmpty(LevelText)
            ? $"{LevelText} • {LevelPercent:F1}% to next level"
            : $"{CampaignProgress}% Complete";

        /// <summary>Image beside the campaign name: the level badge while farming, otherwise the campaign image.</summary>
        public string DisplayCampaignImageUrl => IsLevelFarming && !string.IsNullOrEmpty(LevelBadgeUrl) ? LevelBadgeUrl : CampaignImageUrl;

        /// <summary>Compact level line shown only when the campaign bar is not already showing level progress.</summary>
        public string LevelLineText => IsLevelFarming ? string.Empty : LevelText;

        private void NotifyLevelDerived()
        {
            OnPropertyChanged(nameof(IsLevelFarming));
            OnPropertyChanged(nameof(LevelText));
            OnPropertyChanged(nameof(LevelPercent));
            OnPropertyChanged(nameof(LevelBadgeUrl));
            OnPropertyChanged(nameof(DisplayCampaignProgress));
            OnPropertyChanged(nameof(CampaignProgressText));
            OnPropertyChanged(nameof(DisplayCampaignImageUrl));
            OnPropertyChanged(nameof(LevelLineText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}