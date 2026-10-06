{
  "version": "1.4.1",
  "type": "Patch",
  "changelog": "Reworked how updates are installed: the app now downloads the latest GitHub release, so installs made with Setup are updated by running the new installer (Windows now shows the right version under Installed apps and the uninstaller is kept), while portable installs are updated in place. Downloads retry and recover from stalls, and a GitHub error no longer breaks the update. The version shown in Windows is also corrected on start-up.",
  "historic_versions": [
    {
      "version": "1.4.0",
      "type": "Feature",
      "changelog": "The game white/black-list now offers every game the platforms list, not just ones with an active campaign, and has a search box (also in per-account settings). Campaigns and rewards show an estimated time to complete, and the dashboard shows a live countdown for the current campaign and drop. Eligible-streamer lists now show a \"Finding streamers…\" placeholder while they load, and the live count shows ? until it is known; live status loads much faster on both Twitch and Kick. Fixed a whitelist that excludes everything not stopping the stream already being mined, Twitch not mining after accounts were re-enabled until a restart, and the health check falsely forcing repeated stream re-evaluations."
    },
    {
      "version": "1.3.2",
      "type": "Patch",
      "changelog": "Maintenance release with no functional changes."
    },
    {
      "version": "1.3.1",
      "type": "Patch",
      "changelog": "Reworked the update experience: Update Now now opens a progress window with a live log and progress bar, and downloads are far more reliable on slow connections (limited parallel downloads, automatic retries, stall detection, Cancel and Retry). An update that fails partway can no longer leave you with a half-updated install. Nightly builds are now also offered the matching stable release."
    },
    {
      "version": "1.3.0",
      "type": "Feature",
      "changelog": "Added multi-account support: log in to as many Twitch and Kick accounts as you like, and every account mines in parallel in its own isolated browser profile. Each account has its own card on the dashboard, can be enabled, disabled (which closes its browser immediately) or removed, and can override the global auto-claim, level farming, mining priority and game filter settings. The Inventory page has an account picker, and you are warned before adding more than 10 accounts."
    },
    {
      "version": "1.2.0",
      "type": "Feature",
      "changelog": "Added optional Kick level farming: when no Kick drops are available, the app watches a top streamer to earn viewer levels, and shows your level, XP and progress to the next level on the dashboard."
    },
    {
      "version": "1.1.3",
      "type": "Hotfix",
      "changelog": "Fixed Kick drop claims consistently failing due to a WebView message race with concurrent channel status polling."
    },
    {
      "version": "1.1.2",
      "type": "Hotfix",
      "changelog": "Hotfix for Twitch campaign matching (same issue as Kick had previously)"
    },
    {
      "version": "1.1.1",
      "type": "Patch",
      "changelog": "Fixed Kick campaigns restricted to specific channels sometimes mining a streamer that isn't actually part of that campaign, which silently earned no drop progress. The remembered-streamer preference is now validated against each campaign's own channel list before use."
    },
    {
      "version": "1.1.0",
      "type": "Feature",
      "changelog": "Added support for Kick general drops (\"watch anyone\" and category-wide campaigns) - the app now automatically discovers and watches a live streamer for these instead of skipping them."
    },
    {
      "version": "1.0.23",
      "type": "Feature",
      "changelog": "Added the ability to manually switch campaigns."
    },
    {
      "version": "1.0.22",
      "type": "Patch",
      "changelog": "Hotfix."
    },
    {
      "version": "1.0.21",
      "type": "Patch",
      "changelog": "More bug fixes."
    },
    {
      "version": "1.0.20",
      "type": "Optimization",
      "changelog": "Bug fixes and performance optimizations."
    },
    {
      "version": "1.0.19",
      "type": "Optimization",
      "changelog": "Bug fixes and performance optimizations."
    },
    {
      "version": "1.0.18",
      "type": "Feature",
      "changelog": "Added missing Kick campaigns for site wide drops & big improvements on Twitch drops loading + Image caching for all sites."
    },
    {
      "version": "1.0.17",
      "type": "Patch",
      "changelog": "Fixed an issue with Kick deserialization of JSON data & an several minor issues related to progress tracking & automatic claiming of rewards."
    },
    {
      "version": "1.0.16",
      "type": "Patch",
      "changelog": "Fixed startup settings-save race that could overwrite whitelist values, kept inactive whitelisted slugs visible in settings, and removed inactive placeholders immediately when unchecked or cleared."
    },
    {
      "version": "1.0.15",
      "type": "Patch",
      "changelog": "Added persistent GQL hash caching with retry-aware fallback, immediate claimed-badge inventory updates after auto-claim, remembered streamer selection, and verbose-gated debug/cache logging."
    },
    {
      "version": "1.0.14",
      "type": "Patch",
      "changelog": "Improved WebView waiting reliability and dispatcher async handling."
    },
    {
      "version": "1.0.13",
      "type": "Patch",
      "changelog": "Added verbose debug toggle in Settings, improved Twitch/Kick selection diagnostics, and fixed reward progress percentage tracking while keeping campaign progress tracking intact."
    },
    {
      "version": "1.0.12",
      "type": "Patch",
      "changelog": "Added comprehensive diagnostic logging and a new 'Open Logs Folder' option in Settings"
    },
    {
      "version": "1.0.11",
      "type": "Patch",
      "changelog": "Added comprehensive diagnostic logging and a new 'Open Logs Folder' option in Settings"
    },
    {
      "version": "1.0.10",
      "type": "Patch",
      "changelog": "Fixed WebView contention during drops refresh and stream watching"
    },
    {
      "version": "1.0.9",
      "type": "Patch",
      "changelog": "Fixed a minor issue where start minimized didn't take effect"
    },
    {
      "version": "1.0.8",
      "type": "Patch",
      "changelog": "Highlight current campaign/reward with \"WATCHING\" badges"
    },
    {
      "version": "1.0.7",
      "type": "Patch",
      "changelog": "Improved stream selection and monitoring to verify that the watched Twitch or Kick stream matches the required game/category for each drops campaign"
    },
    {
      "version": "1.0.6",
      "type": "Patch",
      "changelog": "Fixed a bug in prioritizing streamers to watch before general drops"
    },
    {
      "version": "1.0.5",
      "type": "Patch",
      "changelog": "fixed counting error (%) and bug where after claiming all campaigns it would take an hour to idle again.."
    },
    {
      "version": "1.0.4",
      "type": "Patch",
      "changelog": "Fixed a few minor issues with claim status for twitch rewards."
    },
    {
      "version": "1.0.3",
      "type": "Patch",
      "changelog": "Added drop progress to the dashboard."
    },
    {
      "version": "1.0.2",
      "type": "Patch",
      "changelog": "Github Directory Downloader module updated."
    },
    {
      "version": "1.0.1",
      "type": "Bugfix",
      "changelog": "Added Kick bearer token for claim request."
    },
    {
      "version": "1.0.0",
      "type": "Release",
      "changelog": "Initial Release."
    }
  ]
}
