using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LiveSplit.TheRun.Races;

internal sealed class RaceRoomForm : Form
{
    private readonly TheRunRaceAPI api;
    private readonly string roomUrl;
    private readonly string raceId;
    private readonly WebView2 webView;
    private readonly Label loadingLabel;
    private readonly JavaScriptSerializer serializer = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool navigatingToLogin;
    private bool navigatingBackToRace;
    private bool closeAfterUnjoin;

    public RaceRoomForm(
        TheRunRaceAPI api,
        string raceId,
        string roomUrl,
        bool alwaysOnTop)
    {
        this.api = api;
        this.raceId = raceId;
        this.roomUrl = roomUrl;

        Text = "therun.gg - " + raceId;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 480);
        ClientSize = new Size(1100, 760);
        TopMost = alwaysOnTop;

        try
        {
            string executable = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                Icon = Icon.ExtractAssociatedIcon(executable);
            }
        }
        catch
        {
        }

        loadingLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Loading therun.gg race room…",
            TextAlign = ContentAlignment.MiddleCenter
        };

        webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        Controls.Add(webView);
        Controls.Add(loadingLabel);
        Load += OnLoaded;
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;
        Show();
    }

    private async void OnLoaded(object sender, EventArgs e)
    {
        DebugLog.Info("Initializing race-room WebView.");
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LiveSplit",
                    "TheRunWebView2"));
            lifetimeCancellation.Token.ThrowIfCancellationRequested();
            await webView.EnsureCoreWebView2Async(environment);
            lifetimeCancellation.Token.ThrowIfCancellationRequested();
            webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
            webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            loadingLabel.Visible = false;
            webView.Visible = true;
            webView.Source = new Uri(roomUrl);
            DebugLog.Info("Race-room WebView initialized.");
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
            DebugLog.Info("Race-room WebView initialization cancelled.");
        }
        catch (Exception ex)
        {
            DebugLog.Error("Race-room WebView initialization failed.", ex);
            ShowWebView2RuntimeDialog();
        }
    }

    private async void OnNavigationCompleted(
        object sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            if (!e.IsSuccess || webView.Source == null)
            {
                DebugLog.Info("WebView navigation did not complete successfully. Error: " + e.WebErrorStatus + ".");
                return;
            }

            string host = webView.Source.Host.ToLowerInvariant();
            DebugLog.Info("WebView navigation completed. Host: " + host + ".");
            if (host == "id.twitch.tv" || host.EndsWith(".twitch.tv"))
            {
                // Let the user complete Twitch authentication in the same WebView.
                return;
            }

            if (host != "therun.gg" && !host.EndsWith(".therun.gg"))
            {
                return;
            }

            IReadOnlyList<CoreWebView2Cookie> cookies =
                await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://therun.gg");
            if (lifetimeCancellation.IsCancellationRequested || IsDisposed)
            {
                return;
            }
            bool hasSession = false;
            foreach (CoreWebView2Cookie cookie in cookies)
            {
                if (cookie.Name == "session_id" && !string.IsNullOrWhiteSpace(cookie.Value))
                {
                    hasSession = true;
                    break;
                }
            }

            if (hasSession)
            {
                DebugLog.Info("therun.gg session cookie detected.");
                navigatingToLogin = false;
                if (!navigatingBackToRace && !IsRaceRoomUrl(webView.Source))
                {
                    navigatingBackToRace = true;
                    webView.Source = new Uri(roomUrl);
                }
                else if (IsRaceRoomUrl(webView.Source))
                {
                    navigatingBackToRace = false;
                }

                return;
            }

            if (!navigatingToLogin)
            {
                DebugLog.Info("No therun.gg session detected; opening official login flow.");
                navigatingToLogin = true;
                await NavigateToOfficialLogin();
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                DebugLog.Error("Race-room navigation handling failed.", ex);
            }
        }
    }

    private void OnNavigationStarting(
        object sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri) || !IsAllowedWebViewUri(uri))
        {
            e.Cancel = true;
            DebugLog.Info("Blocked race-room navigation to an untrusted origin.");
            if (e.IsUserInitiated && uri?.Scheme is "http" or "https")
            {
                OpenExternalLink(uri);
            }
        }
    }

    private bool IsAllowedWebViewUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        string host = uri.Host.ToLowerInvariant();
        return host == "therun.gg"
            || host.EndsWith(".therun.gg")
            || host == "id.twitch.tv"
            || host.EndsWith(".twitch.tv");
    }

    private async Task<string> GetSessionId()
    {
        IReadOnlyList<CoreWebView2Cookie> cookies =
            await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://therun.gg");
        foreach (CoreWebView2Cookie cookie in cookies)
        {
            if (cookie.Name == "session_id" && !string.IsNullOrWhiteSpace(cookie.Value))
            {
                return cookie.Value;
            }
        }
        return null;
    }

    private async Task NavigateToOfficialLogin()
    {
        // The production Twitch client ID and callback are owned by therun.gg.
        // Read the official login URL generated by their page instead of embedding credentials here.
        const string script =
            "(() => { const link = document.querySelector(\"a[href^='https://id.twitch.tv/oauth2/authorize']\"); return link ? link.href : null; })()";

        for (int attempt = 0; attempt < 20 && !IsDisposed; attempt++)
        {
            try
            {
                string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                if (lifetimeCancellation.IsCancellationRequested || IsDisposed)
                {
                    return;
                }
                string loginUrl = serializer.Deserialize<string>(result);
                if (!string.IsNullOrWhiteSpace(loginUrl))
                {
                    webView.Source = new Uri(loginUrl);
                    return;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Error("Could not inspect the therun.gg login page.", ex);
            }

            try
            {
                await Task.Delay(250, lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        navigatingToLogin = false;
        MessageBox.Show(
            "The therun.gg login link could not be found. You can still use the Login button on the page.",
            "therun.gg Login",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri))
        {
            return;
        }

        if (IsAllowedWebViewUri(uri))
        {
            webView.Source = uri;
        }
        else if (e.IsUserInitiated && uri.Scheme is "http" or "https")
        {
            OpenExternalLink(uri);
        }
    }

    private static void OpenExternalLink(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Error("Could not open an external race-room link.", ex);
        }
    }

    private bool IsRaceRoomUrl(Uri uri)
    {
        return string.Equals(
            uri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            roomUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private void OnDocumentTitleChanged(object sender, object e)
    {
        if (!string.IsNullOrWhiteSpace(webView.CoreWebView2?.DocumentTitle))
        {
            Text = webView.CoreWebView2.DocumentTitle + " - LiveSplit";
        }
    }

    private async void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (closeAfterUnjoin)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        e.Cancel = true;
        closeAfterUnjoin = true;
        try
        {
            string sessionId = await GetSessionId();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                await api.LeaveRace(raceId, sessionId);
                DebugLog.Info("Unjoined race while closing the race room. Race ID: " + raceId + ".");
            }
        }
        catch (Exception ex)
        {
            // Closing must still succeed when the user was not participating,
            // the countdown already started, or therun.gg is unavailable.
            DebugLog.Info("Could not unjoin while closing the race room. Race ID: " +
                raceId + ". " + ex.Message);
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((Action)Close);
            }
        }
    }

    private void OnFormClosed(object sender, FormClosedEventArgs e)
    {
        lifetimeCancellation.Cancel();
        DebugLog.Info("Disposing race-room WebView.");
        api.OnRoomClosed(this);
        webView.Dispose();
    }

    private void ShowWebView2RuntimeDialog()
    {
        DialogResult result = MessageBox.Show(
            "This race window requires the Microsoft Edge WebView2 Runtime. " +
            "Do you want to open the download page in your default browser?",
            "Microsoft Edge WebView2 Runtime Required",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            OpenExternalLink(new Uri("https://aka.ms/winui2/webview2download"));
        }

        Close();
    }
}
