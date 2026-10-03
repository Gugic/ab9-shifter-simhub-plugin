using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using AB9ActiveShifter.Updates;

namespace AB9ActiveShifter.UI
{
    public partial class SettingsControl
    {
        private UpdateService _updateService;
        private bool _refreshingUpdates;
        private string _renderedReleaseNotes;

        private void AttachUpdates()
        {
            DetachUpdates();
            _updateService = Plugin?.Updates;
            if (_updateService != null) _updateService.Changed += OnUpdateStateChanged;
            RefreshUpdates();
        }

        private void DetachUpdates()
        {
            if (_updateService != null) _updateService.Changed -= OnUpdateStateChanged;
            _updateService = null;
        }

        private void OnUpdateStateChanged()
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke((Action)(() => { if (IsLoaded) RefreshUpdates(); }));
        }

        private void RefreshUpdates()
        {
            if (Plugin?.Store == null || _updateService == null) return;
            UpdateState state = _updateService.State;
            ReleaseInfo release = state.Release;
            bool available = release != null && release.IsNewer;
            bool dismissed = available && Plugin.Store.DismissedUpdateVersion == release.Version;
            bool actionable = available || state.RestartRequired;
            string action = state.RestartRequired ? "Restart SimHub" : "Install update";

            UpdateInstalledVersionText.Text = "Installed version: v" + PluginInfo.Version;
            UpdateStatusText.Text = state.Status;
            if (available && !release.CanInstall && !state.Busy)
                UpdateStatusText.Text += " Open release notes to download this release manually.";
            UpdateLastCheckedText.Text = state.LastChecked.HasValue ? "Last checked: " + state.LastChecked.Value.ToString("g") : "Not checked yet";
            CheckUpdatesButton.IsEnabled = !state.Busy && !state.RestartRequired;
            OptionsInstallButton.Visibility = actionable ? Visibility.Visible : Visibility.Collapsed;
            OptionsInstallButton.Content = action;
            OptionsInstallButton.IsEnabled = !state.Busy && (state.RestartRequired || release?.CanInstall == true);
            BannerInstallButton.Content = action;
            BannerInstallButton.IsEnabled = OptionsInstallButton.IsEnabled;
            BannerInstallButton.ToolTip = OptionsInstallButton.ToolTip = state.RestartRequired ?
                "Restarts SimHub to load the installed update." : "Downloads and installs the release. Restart SimHub afterward to load it.";
            OptionsDismissButton.Visibility = actionable && !dismissed ? Visibility.Visible : Visibility.Collapsed;
            OptionsDismissButton.IsEnabled = !state.Busy;
            UpdateBanner.Visibility = actionable && !dismissed && Plugin.CurrentOperatingMode != Core.OperatingMode.Ab9HPattern ? Visibility.Visible : Visibility.Collapsed;
            UpdateBannerText.Text = state.Status;
            ReleaseNotesButton.IsEnabled = release != null;
            ReleaseNotesPanel.Visibility = release != null ? Visibility.Visible : Visibility.Collapsed;
            ReleaseNotesTitle.Text = release != null ? "What's new in v" + release.Version : "";
            string notes = release?.Notes ?? "";
            if (_renderedReleaseNotes != notes)
            {
                ReleaseNotesText.Document = RenderReleaseNotes(notes);
                _renderedReleaseNotes = notes;
            }
            _refreshingUpdates = true;
            try { AutomaticUpdatesToggle.IsChecked = Plugin.Store.CheckUpdatesAutomatically; }
            finally { _refreshingUpdates = false; }
        }

        private void OnAutomaticUpdatesChanged(object sender, RoutedEventArgs e)
        {
            if (_refreshingUpdates || Plugin?.Store == null || _updateService == null) return;
            Plugin.Store.CheckUpdatesAutomatically = AutomaticUpdatesToggle.IsChecked == true;
            Plugin.SaveStore();
            _updateService.Configure(Plugin.Store.CheckUpdatesAutomatically);
        }

        private async void OnCheckUpdates(object sender, RoutedEventArgs e)
        {
            if (_updateService == null) return;
            // A manual check brings a dismissed version back into view.
            Plugin.Store.DismissedUpdateVersion = null;
            Plugin.SaveStore();
            await _updateService.CheckAsync();
        }

        private async void OnInstallUpdate(object sender, RoutedEventArgs e)
        {
            if (_updateService == null || _updateService.State.Busy) return;
            if (_updateService.State.RestartRequired)
            {
                if (!Plugin.RestartAfterUpdate())
                    MessageBox.Show("Please close and start SimHub to load the installed update.", "AB9 Active Shifter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Plugin.Store.DismissedUpdateVersion = null;
            Plugin.SaveStore();
            await _updateService.InstallAsync();
        }

        private void OnDismissUpdate(object sender, RoutedEventArgs e)
        {
            ReleaseInfo release = _updateService?.State.Release;
            if (release == null || _updateService.State.Busy) return;
            Plugin.Store.DismissedUpdateVersion = release.Version;
            Plugin.SaveStore();
            RefreshUpdates();
        }

        private void OnOpenReleaseNotes(object sender, RoutedEventArgs e)
        {
            string url = _updateService?.State.Release?.PageUrl;
            if (url == null) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the release notes: " + ex.Message, "AB9 Active Shifter", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // A small, text-only Markdown view: no HTML, embedded content, or executable links.
        // The full GitHub page remains available through Open release notes.
        private FlowDocument RenderReleaseNotes(string notes)
        {
            FlowDocument document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = ReleaseNotesText.FontFamily,
                FontSize = ReleaseNotesText.FontSize
            };
            document.SetBinding(TextElement.ForegroundProperty, new Binding("Foreground") { Source = ReleaseNotesText });
            Paragraph quote = null;
            bool code = false;
            foreach (string raw in notes.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal)) { code = !code; quote = null; continue; }
                if (line.Length == 0) { quote = null; continue; }
                bool quoted = !code && line.StartsWith(">", StringComparison.Ordinal);
                if (quoted) line = line.Substring(1).TrimStart();
                bool heading = !code && (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("[!", StringComparison.Ordinal));
                if (heading)
                {
                    line = line.TrimStart('#').TrimStart();
                    if (line.StartsWith("[!", StringComparison.Ordinal))
                    {
                        line = line.Trim('[', '!', ']').ToLowerInvariant();
                        if (line.Length > 0) line = char.ToUpperInvariant(line[0]) + line.Substring(1);
                    }
                }
                bool bullet = !code && (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal));
                if (bullet) line = "• " + line.Substring(2);
                Paragraph paragraph;
                if (quoted && quote != null && !heading)
                {
                    paragraph = quote;
                    paragraph.Inlines.Add(new Run(" "));
                }
                else
                {
                    paragraph = new Paragraph { Margin = new Thickness(bullet ? 12 : 0, 0, 0, 8) };
                    if (heading) paragraph.FontWeight = FontWeights.Bold;
                    if (code) paragraph.FontFamily = new FontFamily("Consolas");
                    document.Blocks.Add(paragraph);
                    quote = quoted && !heading ? paragraph : null;
                }
                int offset = 0;
                foreach (Match match in Regex.Matches(line, @"\*\*[^*]+\*\*|`[^`]+`|\[([^\]]+)\]\([^)]+\)"))
                {
                    paragraph.Inlines.Add(new Run(line.Substring(offset, match.Index - offset)));
                    string text = match.Value;
                    if (text.StartsWith("**", StringComparison.Ordinal))
                        paragraph.Inlines.Add(new Bold(new Run(text.Substring(2, text.Length - 4))));
                    else if (text.StartsWith("`", StringComparison.Ordinal))
                        paragraph.Inlines.Add(new Run(text.Substring(1, text.Length - 2)) { FontFamily = new FontFamily("Consolas") });
                    else paragraph.Inlines.Add(new Run(match.Groups[1].Value));
                    offset = match.Index + match.Length;
                }
                paragraph.Inlines.Add(new Run(line.Substring(offset)));
            }
            return document;
        }
    }
}
