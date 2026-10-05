using Application = System.Windows.Application;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Core.Enums;
using Core.Logging;
using Core.Models;
using Core.Services.Twitch.Helix;
using Core.Stores;
using UI.Views;
using MessageBox = System.Windows.MessageBox;

namespace UI.Accounts
{
    /// <summary>
    /// Owns every configured account and runs their sessions in parallel.
    /// </summary>
    public sealed class AccountManager : INotifyPropertyChanged
    {
        /// <summary>Number of accounts after which the user is warned about resource usage when adding more.</summary>
        public const int WarnAccountThreshold = 10;

        private static readonly Lazy<AccountManager> InstanceLazy = new(() => new AccountManager());

        private readonly AccountStore _store = new();
        private readonly SemaphoreSlim _startupGate = new(3, 3);
        private readonly System.Timers.Timer _refreshTimer = new(TimeSpan.FromHours(1).TotalMilliseconds);
        private readonly SemaphoreSlim _helixLock = new(1, 1);
        private bool _initialized;
        private AccountSession? _selectedSession;
        private AccountSession? _helixSession;

        private AccountManager()
        {
            Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        }

        /// <summary>Gets the singleton instance.</summary>
        public static AccountManager Instance => InstanceLazy.Value;

        /// <summary>Gets all account sessions.</summary>
        public ObservableCollection<AccountSession> Sessions { get; } = [];

        /// <summary>Gets or sets the account whose campaigns the inventory page shows.</summary>
        public AccountSession? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (_selectedSession == value)
                    return;

                _selectedSession = value;
                OnPropertyChanged();
                SelectedSessionChanged?.Invoke(value);
            }
        }

        /// <summary>Raised when an account is enabled or disabled, so account pickers can refilter.</summary>
        public event Action? EnabledAccountsChanged;

        /// <summary>Raised when the inventory's selected account changes.</summary>
        public event Action<AccountSession?>? SelectedSessionChanged;

        /// <summary>Gets an aggregate status line for the dashboard.</summary>
        public string Summary
        {
            get
            {
                int total = Sessions.Count;
                int mining = Sessions.Count(s => s.IsMining);
                int connected = Sessions.Count(s => s.IsConnected);
                return $"{mining} mining • {connected}/{total} connected";
            }
        }

        /// <summary>Gets the aggregate miner state label.</summary>
        public string MinerStatus => Sessions.Any(s => s.IsMining) ? "Mining"
            : Sessions.Count == 0 ? "No accounts"
            : Sessions.Any(s => s.IsConnected) ? "Idle"
            : "Need login";

        /// <summary>
        /// Loads the saved accounts and starts them, validating logins with bounded concurrency.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            _initialized = true;

            foreach (AccountModel model in _store.Load())
                AddSession(model);

            SelectedSession = Sessions.FirstOrDefault(s => s.Enabled);
            SweepOrphanProfiles();

            _refreshTimer.Elapsed += (_, _) => Application.Current.Dispatcher.InvokeAsync(RefreshAllStaggeredAsync);
            _refreshTimer.AutoReset = true;
            _refreshTimer.Start();

            await Task.WhenAll(Sessions.ToList().Select(ValidateWithGateAsync));
        }

        /// <summary>
        /// Adds a new account for the platform and opens its login window.
        /// </summary>
        /// <returns><see langword="false"/> when the user cancelled the high-account-count warning.</returns>
        public async Task<bool> AddAccountAsync(Platform platform)
        {
            if (Sessions.Count >= WarnAccountThreshold
                && MessageBox.Show(
                    $"You already have {Sessions.Count} accounts.\n\n" +
                    "Every account runs its own browser and live stream at the same time, which uses a lot of memory, CPU and bandwidth, " +
                    "and many simultaneous accounts may trigger rate limits or account restrictions on the platforms.\n\n" +
                    "Add another account anyway?",
                    "Many accounts",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return false;
            }

            int number = Sessions.Count(s => s.Model.Platform == platform) + 1;
            AccountModel model = new() { Platform = platform, DisplayName = $"{platform} account {number}" };

            AccountSession session = AddSession(model);
            Save();

            await session.LoginAsync();
            return true;
        }

        /// <summary>
        /// Removes an account, stopping its session and deleting its browser profile.
        /// </summary>
        public void RemoveAccount(AccountSession session)
        {
            if (MessageBox.Show(
                    $"Remove '{session.DisplayName}' ({session.PlatformName})? This logs the account out and deletes its saved session.",
                    "Remove account",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            if (SelectedSession == session)
                SelectedSession = Sessions.FirstOrDefault(s => s != session && s.Enabled);

            if (_helixSession == session)
            {
                _helixSession = null;
            }

            Sessions.Remove(session);
            session.ModelChanged -= OnSessionModelChanged;
            session.Dispose();
            Save();
            OnPropertyChanged(nameof(MinerStatus));

            if (_helixSession == null && Sessions.FirstOrDefault(s => s.IsTwitch && s.IsConnected) is { } next)
            {
                _helixSession = next;
                next.Engine.UseHelixWatcher = true;
            }

            if (session.Model.UserDataFolder is { } folder)
                _ = Task.Run(() => TryDeleteFolderWithRetries(folder));
        }

        private AccountSession AddSession(AccountModel model)
        {
            AccountSession session = new(model);
            session.ModelChanged += OnSessionModelChanged;
            session.Connected += OnSessionConnected;
            session.PropertyChanged += OnSessionPropertyChanged;

            // Keep Twitch accounts above Kick accounts, preserving creation order within each platform.
            int index = Sessions.Count;
            for (int i = 0; i < Sessions.Count; i++)
            {
                if (Sessions[i].Model.Platform > model.Platform)
                {
                    index = i;
                    break;
                }
            }

            Sessions.Insert(index, session);
            return session;
        }

        private void OnSessionModelChanged(AccountSession _) => Save();

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AccountSession.Enabled) && sender is AccountSession changed)
            {
                // The inventory can only show an enabled account.
                if (!changed.Enabled && SelectedSession == changed)
                    SelectedSession = Sessions.FirstOrDefault(s => s.Enabled);
                else if (changed.Enabled && SelectedSession == null)
                    SelectedSession = changed;

                EnabledAccountsChanged?.Invoke();
            }

            if (e.PropertyName is nameof(AccountSession.StatusText) or nameof(AccountSession.IsConnected))
            {
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(MinerStatus));
            }
        }

        private void Save() => _store.Save(Sessions.Select(s => s.Model).ToList());

        private async Task ValidateWithGateAsync(AccountSession session)
        {
            await _startupGate.WaitAsync();
            try
            {
                await session.ValidateAsync();
            }
            finally
            {
                _startupGate.Release();
            }
        }

        private async Task RefreshAllStaggeredAsync()
        {
            foreach (AccountSession session in Sessions.ToList())
            {
                session.ScheduleDropsLoad();
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        /// <summary>
        /// Ensures Twitch Helix API access once (it is shared and only needs one logged-in Twitch account) and lets that
        /// account's engine drive the shared EventSub watcher.
        /// </summary>
        private void OnSessionConnected(AccountSession session)
        {
            if (!session.IsTwitch)
                return;

            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await _helixLock.WaitAsync();
                try
                {
                    if (_helixSession == null)
                    {
                        _helixSession = session;
                        session.Engine.UseHelixWatcher = true;
                    }

                    if (TwitchHelixService.Instance.IsAuthenticated)
                    {
                        session.Engine.ScheduleTwitchStreamerMetadataRefresh();
                        return;
                    }

                    bool authenticated = await TwitchHelixService.Instance.EnsureAuthenticatedAsync(async prompt =>
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => new TwitchHelixAuthWindow(prompt).ShowDialog());
                    });

                    if (authenticated)
                        session.Engine.ScheduleTwitchStreamerMetadataRefresh();
                }
                finally
                {
                    _helixLock.Release();
                }
            });
        }

        /// <summary>
        /// Deletes browser profiles left behind by removed accounts (for example when a folder was locked at removal time).
        /// </summary>
        private void SweepOrphanProfiles()
        {
            try
            {
                if (!Directory.Exists(AccountModel.AccountsRootFolder))
                    return;

                HashSet<string> known = Sessions.Select(s => s.Model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string dir in Directory.GetDirectories(AccountModel.AccountsRootFolder))
                {
                    if (!known.Contains(Path.GetFileName(dir)))
                        _ = Task.Run(() => TryDeleteFolderWithRetries(dir));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Accounts", $"Orphan profile sweep failed: {ex.Message}");
            }
        }

        private static async Task TryDeleteFolderWithRetries(string folder)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, recursive: true);

                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
            }

            AppLogger.Warn("Accounts", $"Could not delete profile folder yet (will retry next start): {folder}");
        }

        /// <summary>Raised when a bindable property changes.</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}