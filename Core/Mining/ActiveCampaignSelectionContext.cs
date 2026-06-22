using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Tracks which campaigns are currently selected per platform for mining.
    /// </summary>
    public sealed class ActiveCampaignSelectionContext
    {
        /// <summary>
        /// Gets or sets the Twitch campaign currently being mined.
        /// </summary>
        public DropsCampaign? CurrentTwitchCampaign { get; set; }

        /// <summary>
        /// Gets or sets the Kick campaign currently being mined.
        /// </summary>
        public DropsCampaign? CurrentKickCampaign { get; set; }
    }
}