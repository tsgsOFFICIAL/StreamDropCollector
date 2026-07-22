using Core.Logging;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Tracks which campaigns are currently selected per platform for mining.
    /// </summary>
    public sealed class ActiveCampaignSelectionContext
    {
        private DropsCampaign? _currentTwitchCampaign;
        private DropsCampaign? _currentKickCampaign;

        /// <summary>
        /// Gets or sets the Twitch campaign currently being mined.
        /// </summary>
        public DropsCampaign? CurrentTwitchCampaign
        {
            get => _currentTwitchCampaign;
            set
            {
                if (!ReferenceEquals(_currentTwitchCampaign, value))
                    AppLogger.Debug("SelectionContext", $"CurrentTwitchCampaign CHANGE {_currentTwitchCampaign?.Id ?? "(none)"} -> {value?.Id ?? "(none)"}");
                _currentTwitchCampaign = value;
            }
        }

        /// <summary>
        /// Gets or sets the Kick campaign currently being mined.
        /// </summary>
        public DropsCampaign? CurrentKickCampaign
        {
            get => _currentKickCampaign;
            set
            {
                if (!ReferenceEquals(_currentKickCampaign, value))
                    AppLogger.Debug("SelectionContext", $"CurrentKickCampaign CHANGE {_currentKickCampaign?.Id ?? "(none)"} -> {value?.Id ?? "(none)"}");
                _currentKickCampaign = value;
            }
        }
    }
}