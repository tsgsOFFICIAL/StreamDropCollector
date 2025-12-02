using System.Collections.ObjectModel;
using Core.Interfaces;
using System.Windows;
using Core.Models;

namespace Core.Managers
{
    public sealed class DropsInventoryManager
    {
        private static readonly Lazy<DropsInventoryManager> _instance = new(() => new DropsInventoryManager());
        public static DropsInventoryManager Instance => _instance.Value;

        public ObservableCollection<DropsCampaign> ActiveCampaigns { get; } = new ObservableCollection<DropsCampaign>();

        public IWebViewHost? TwitchWebView { get; private set; }
        public IWebViewHost? KickWebView { get; private set; }

        private DropsInventoryManager() { }

        public void InitializeWebViews(IWebViewHost twitch, IWebViewHost kick)
        {
            TwitchWebView = twitch ?? throw new ArgumentNullException(nameof(twitch));
            KickWebView = kick ?? throw new ArgumentNullException(nameof(kick));
        }

        public void UpdateCampaigns(IEnumerable<DropsCampaign> campaigns)
         {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ActiveCampaigns.Clear();
                foreach (DropsCampaign? c in campaigns.OrderBy(x => x.GameName))
                    ActiveCampaigns.Add(c);
            });
        }
    }
}