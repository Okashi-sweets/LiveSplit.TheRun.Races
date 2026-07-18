using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
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
    private bool navigatingToLogin;
#if !LITE_ROOM
    private bool navigatingBackToRace;
#endif
    private bool closeAfterUnjoin;
#if LITE_ROOM
    private bool liteLoaded;
    private CancellationTokenSource liteCancellation;
#endif

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
            await webView.EnsureCoreWebView2Async(environment);
            webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
#if LITE_ROOM
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
#endif
            loadingLabel.Visible = false;
            webView.Visible = true;
#if LITE_ROOM
            string sessionId = await GetSessionId();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                LoadLiteRoom();
            }
            else
            {
                webView.Source = new Uri(roomUrl);
            }
#else
            webView.Source = new Uri(roomUrl);
#endif
            DebugLog.Info("Race-room WebView initialized.");
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
        if (!e.IsSuccess || webView.Source == null)
        {
            DebugLog.Info("WebView navigation did not complete successfully. Error: " + e.WebErrorStatus + ".");
            return;
        }

        string host = webView.Source.Host.ToLowerInvariant();
        DebugLog.Info("WebView navigation completed. Host: " + host + ".");
        if (host == "id.twitch.tv")
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
#if LITE_ROOM
            if (!liteLoaded)
            {
                LoadLiteRoom();
            }
            return;
#else
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
#endif
        }

        if (!navigatingToLogin)
        {
            DebugLog.Info("No therun.gg session detected; opening official login flow.");
            navigatingToLogin = true;
            await NavigateToOfficialLogin();
        }
    }

#if LITE_ROOM
    private void LoadLiteRoom()
    {
        liteLoaded = true;
        try
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "LiveSplit.TheRun.Races.Assets.LiteRaceRoom.html");
            if (stream == null)
            {
                throw new InvalidOperationException("The lightweight race-room resource is missing.");
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            string html = reader.ReadToEnd().Replace("{{RACE_ID}}", HtmlEncode(raceId));
            webView.NavigateToString(html);
            liteCancellation = new CancellationTokenSource();
            _ = PollLiteRoom(liteCancellation.Token);
            Text = "therun.gg Lite - " + raceId;
        }
        catch (Exception ex)
        {
            DebugLog.Error("Could not load the lightweight race room.", ex);
            liteLoaded = false;
            MessageBox.Show(
                "The lightweight race room could not be loaded.\n\n" + ex.Message,
                "therun.gg Races Lite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task PollLiteRoom(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !IsDisposed)
        {
            try
            {
                string json = await api.GetRaceJson(raceId);
                webView.CoreWebView2?.PostWebMessageAsJson(
                    "{\"type\":\"snapshot\",\"payload\":" + json + "}");
            }
            catch (Exception ex)
            {
                DebugLog.Error("Lite race-room refresh failed.", ex);
                PostLiteMessage("error", "Could not refresh the race room: " + ex.Message);
            }

            try
            {
                await Task.Delay(2000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!liteLoaded)
        {
            return;
        }

        LiteCommand command;
        try
        {
            command = serializer.Deserialize<LiteCommand>(e.WebMessageAsJson);
        }
        catch
        {
            return;
        }

        if (command?.type != "action" || string.IsNullOrWhiteSpace(command.action))
        {
            return;
        }

        try
        {
            string sessionId = await GetSessionId();
            await api.PerformRaceAction(raceId, command.action, sessionId, command.password);
            PostLiteMessage("success", "Action completed.");
            string json = await api.GetRaceJson(raceId);
            webView.CoreWebView2?.PostWebMessageAsJson(
                "{\"type\":\"snapshot\",\"payload\":" + json + "}");
        }
        catch (Exception ex)
        {
            DebugLog.Error("Lite race-room action failed: " + command.action + ".", ex);
            PostLiteMessage("error", CleanApiError(ex.Message));
        }
    }

    private void PostLiteMessage(string type, string message)
    {
        string json = serializer.Serialize(new { type, message });
        webView.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private static string CleanApiError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "The action failed.";
        }
        return message.Length > 500 ? message.Substring(0, 500) : message;
    }

    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value ?? "");
#endif

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

            await Task.Delay(250);
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
        webView.Source = new Uri(e.Uri);
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

        e.Cancel = true;
        closeAfterUnjoin = true;
        try
        {
            string sessionId = await GetSessionId();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                await api.PerformRaceAction(raceId, "leave", sessionId, null);
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
#if LITE_ROOM
        liteCancellation?.Cancel();
        liteCancellation?.Dispose();
#endif
        DebugLog.Info("Disposing race-room WebView.");
        api.OnRoomClosed(this);
        webView.Dispose();
    }

#if LITE_ROOM
    private sealed class LiteCommand
    {
        public string type { get; set; }
        public string action { get; set; }
        public string password { get; set; }
    }
#endif

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
            Process.Start(new ProcessStartInfo(
                "https://aka.ms/winui2/webview2download")
            {
                UseShellExecute = true
            });
        }

        Close();
    }
}
