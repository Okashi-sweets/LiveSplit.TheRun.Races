using LiveSplit.Model;
using LiveSplit.Model.Input;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LiveSplit.TheRun.Races;

public sealed class TheRunRaceAPI : RaceProviderAPI
{
    private const string ApiRoot = "https://6nkfyze0o7.execute-api.eu-west-1.amazonaws.com/prod";
    private const string WebSocketRoot = "wss://ws.therun.gg";
    private const string WebsiteRoot = "https://therun.gg/races";
    private const int MaxApiResponseBytes = 1024 * 1024;
    private static readonly TimeSpan WebSocketReceiveTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = MaxApiResponseBytes
    };
    private readonly JavaScriptSerializer serializer = new();
    private readonly object watcherLock = new();
    private const int MaxWebSocketMessageBytes = 1024 * 1024;
    private IReadOnlyList<TheRunRaceInfo> races = [];
    private CancellationTokenSource watcherCancellation;
    private long watcherGeneration;
    private long refreshGeneration;
    private long joinGeneration;
    private RaceRoomForm roomForm;
    internal string LastRefreshError { get; private set; }
    private ITimerModel preparedModel;
    private TimeSpan originalOffset;
    private bool originalOffsetSaved;
    private bool restoreOffsetOnReset;
    private TheRunLiveSync liveSync;

    public static TheRunRaceAPI Instance { get; } = new();

    private TheRunRaceAPI()
    {
        DebugLog.Info("Component initialized. Version 0.4.0.");
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        JoinRace = Join;
        CreateRace = _ => OpenUrl(WebsiteRoot + "/create");
    }

    public override string ProviderName => "therun.gg";

    public override string Username => null;

    public override IEnumerable<IRaceInfo> GetRaces() => races;

    internal void ConfigureLiveSync(LiveSplitState state, TheRunRaceSettings settings)
    {
        if (settings == null || !settings.Enabled)
        {
            liveSync?.Dispose();
            liveSync = null;
            return;
        }

        if (liveSync?.State == state)
        {
            liveSync.Settings = settings;
            return;
        }

        liveSync?.Dispose();
        liveSync = new TheRunLiveSync(state, settings);
    }

    public override void RefreshRacesListAsync()
    {
        long generation = Interlocked.Increment(ref refreshGeneration);
        _ = RefreshRacesList(generation);
    }

    private async Task RefreshRacesList(long generation)
    {
        try
        {
            string json = await GetString(ApiRoot + "/active", CancellationToken.None);
            RaceListResponse response = serializer.Deserialize<RaceListResponse>(json);
            IReadOnlyList<TheRunRaceInfo> refreshedRaces = response?.result?
                .Where(race =>
                    race.status is "pending" or "starting" or "progress" &&
                    race.visible &&
                    !string.IsNullOrWhiteSpace(race.raceId))
                .Select(TheRunRaceInfo.FromDto)
                .ToArray() ?? [];
            if (generation != Interlocked.Read(ref refreshGeneration))
            {
                return;
            }

            races = refreshedRaces;
            LastRefreshError = null;
            DebugLog.Info("Race list refreshed. Active races: " + races.Count + ".");
            RacesRefreshedCallback?.Invoke(this);
        }
        catch (Exception ex)
        {
            if (generation != Interlocked.Read(ref refreshGeneration))
            {
                return;
            }

            DebugLog.Error("Race list refresh failed.", ex);
            LastRefreshError = ex.ToString();
            races = [];
            RacesRefreshedCallback?.Invoke(this);
        }
    }

    private async void Join(ITimerModel model, string raceId)
    {
        if (string.IsNullOrWhiteSpace(raceId))
        {
            return;
        }

        DebugLog.Info("Opening race room. Race ID: " + raceId + ".");
        long generation = Interlocked.Increment(ref joinGeneration);

        TheRunRaceDto race;
        try
        {
            race = await GetRace(raceId);
            if (generation != Interlocked.Read(ref joinGeneration))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            if (generation != Interlocked.Read(ref joinGeneration))
            {
                return;
            }

            DebugLog.Error("Race details could not be loaded. Race ID: " + raceId + ".", ex);
            MessageBox.Show(
                "The race information could not be loaded.\n\n" + ex.Message,
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        bool isJoinable = race?.status == "pending";
        bool isOngoing = race?.status is "starting" or "progress";
        if (!isJoinable && !isOngoing)
        {
            DebugLog.Info("Race is no longer joinable. Race ID: " + raceId + ", status: " + (race?.status ?? "null") + ".");
            MessageBox.Show(
                "This race has already started or is no longer accepting participants.",
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            RefreshRacesListAsync();
            return;
        }

        if (isJoinable)
        {
            PrepareCountdownOffset(model, race.countdownSeconds);
        }
        else
        {
            // An in-progress room is observation-only. Never carry a prepared
            // race offset into it from a previously opened pending room.
            RestoreOriginalOffset();
        }

        roomForm?.Close();
        roomForm = new RaceRoomForm(
            this,
            raceId,
            WebsiteRoot + "/" + Uri.EscapeDataString(raceId),
            model.CurrentState.LayoutSettings.AlwaysOnTop);
        if (isJoinable)
        {
            StartWatcher(model, raceId);
        }
    }

    internal void StartWatcher(ITimerModel model, string raceId)
    {
        DebugLog.Info("Starting race WebSocket watcher. Race ID: " + raceId + ".");
        CancellationTokenSource cancellation = new();
        long generation;
        lock (watcherLock)
        {
            watcherCancellation?.Cancel();
            watcherCancellation?.Dispose();
            watcherCancellation = cancellation;
            generation = ++watcherGeneration;
        }

        SynchronizationContext uiContext = SynchronizationContext.Current;
        _ = WatchRace(model, raceId, uiContext, generation, cancellation.Token);
    }

    internal void CancelWatcher()
    {
        DebugLog.Info("Stopping race WebSocket watcher.");
        lock (watcherLock)
        {
            watcherGeneration++;
            watcherCancellation?.Cancel();
            watcherCancellation?.Dispose();
            watcherCancellation = null;
        }
    }

    internal void OnRoomClosed(RaceRoomForm form)
    {
        if (!ReferenceEquals(roomForm, form))
        {
            return;
        }

        CancelWatcher();
        DebugLog.Info("Race room closed.");
        RestoreOriginalOffset();
        roomForm = null;
    }

    private async Task WatchRace(
        ITimerModel model,
        string raceId,
        SynchronizationContext uiContext,
        long generation,
        CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        while (IsWatcherCurrent(generation, cancellationToken))
        {
            try
            {
                TheRunRaceDto snapshot = await GetRace(raceId, cancellationToken);
                if (!IsWatcherCurrent(generation, cancellationToken)
                    || HandleRaceState(model, snapshot, uiContext, generation, cancellationToken))
                {
                    return;
                }

                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                var socketUri = new Uri(WebSocketRoot + "?race=" + Uri.EscapeDataString(raceId));
                await socket.ConnectAsync(socketUri, cancellationToken);
                DebugLog.Info("Race WebSocket connected. Race ID: " + raceId + ".");
                consecutiveFailures = 0;

                // The socket does not send an initial snapshot, so close the
                // race-between-check-and-connect gap after connecting as well.
                snapshot = await GetRace(raceId, cancellationToken);
                if (!IsWatcherCurrent(generation, cancellationToken)
                    || HandleRaceState(model, snapshot, uiContext, generation, cancellationToken))
                {
                    return;
                }

                byte[] buffer = new byte[16 * 1024];
                while (socket.State == WebSocketState.Open
                    && IsWatcherCurrent(generation, cancellationToken))
                {
                    string message;
                    using (var receiveCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        receiveCancellation.CancelAfter(WebSocketReceiveTimeout);
                        try
                        {
                            message = await ReceiveMessage(
                                socket,
                                buffer,
                                receiveCancellation.Token);
                        }
                        catch (OperationCanceledException)
                            when (!cancellationToken.IsCancellationRequested)
                        {
                            DebugLog.Info(
                                "Race WebSocket was idle for "
                                + WebSocketReceiveTimeout.TotalSeconds.ToString("F0")
                                + " seconds; refreshing via HTTP.");
                            message = null;
                        }
                    }
                    if (message == null)
                    {
                        break;
                    }

                    RaceWebSocketMessage update = serializer.Deserialize<RaceWebSocketMessage>(message);
                    if (update?.type == "raceUpdate"
                        && update.data?.raceId == raceId
                        && HandleRaceState(model, update.data, uiContext, generation, cancellationToken))
                    {
                        return;
                    }
                }

                DebugLog.Info("Race WebSocket closed; reconnecting. Race ID: " + raceId + ".");
            }
            catch (OperationCanceledException)
            {
                DebugLog.Info("Race WebSocket watcher cancelled. Race ID: " + raceId + ".");
                return;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                DebugLog.Error(
                    "Race watcher connection failed; retrying. Race ID: " + raceId
                    + ", consecutive failures: " + consecutiveFailures + ".",
                    ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, 1 << Math.Min(3, consecutiveFailures))), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private bool HandleRaceState(
        ITimerModel model,
        TheRunRaceDto race,
        SynchronizationContext uiContext,
        long generation,
        CancellationToken cancellationToken)
    {
        if (race == null)
        {
            return false;
        }

        if (TryScheduleStart(model, race, uiContext, generation, cancellationToken))
        {
            return true;
        }

        if (race.status is "progress" or "finished" or "aborted")
        {
            PostIfWatcherCurrent(
                uiContext,
                generation,
                cancellationToken,
                RestoreOriginalOffset);
            return true;
        }

        return false;
    }

    private static async Task<string> ReceiveMessage(
        ClientWebSocket socket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException("The race WebSocket returned a non-text message.");
            }

            if (builder.Length + result.Count > MaxWebSocketMessageBytes)
            {
                throw new InvalidOperationException("The race WebSocket message exceeded the 1 MB limit.");
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return builder.ToString();
    }

    private bool TryScheduleStart(
        ITimerModel model,
        TheRunRaceDto race,
        SynchronizationContext uiContext,
        long generation,
        CancellationToken cancellationToken)
    {
        if (race?.status != "starting" || string.IsNullOrWhiteSpace(race.startTime))
        {
            return false;
        }

        if (!DateTime.TryParse(
            race.startTime,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTime startTime))
        {
            return false;
        }

        PostIfWatcherCurrent(
            uiContext,
            generation,
            cancellationToken,
            () => StartTimer(model, startTime.ToUniversalTime()));
        DebugLog.Info("Race start scheduled for " + startTime.ToUniversalTime().ToString("O") + ".");
        return true;
    }

    private void StartTimer(ITimerModel model, DateTime startTimeUtc)
    {
        ITimerModel timerModel = model is DoubleTapPrevention prevention
            ? prevention.InternalModel
            : model;

        if (timerModel.CurrentState.CurrentPhase != TimerPhase.NotRunning)
        {
            DebugLog.Info("Timer start skipped because LiveSplit is already running.");
            MessageBox.Show(
                "The therun.gg countdown started, but the LiveSplit timer was already running.",
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        TimeSpan remaining = startTimeUtc - TimeStamp.CurrentDateTime.Time.ToUniversalTime();
        if (remaining < TimeSpan.Zero)
        {
            // Joining after the countdown is deliberately unsupported.
            return;
        }

        timerModel.CurrentState.Run.Offset = remaining.Negate();
        timerModel.CurrentState.AdjustedStartTime = TimeStamp.Now - timerModel.CurrentState.Run.Offset;
        timerModel.Start();
        DebugLog.Info("LiveSplit timer started. Remaining countdown ms: " + remaining.TotalMilliseconds.ToString("F0") + ".");
    }

    private void PrepareCountdownOffset(ITimerModel model, int countdownSeconds)
    {
        ITimerModel timerModel = model is DoubleTapPrevention prevention
            ? prevention.InternalModel
            : model;

        if (timerModel.CurrentState.CurrentPhase != TimerPhase.NotRunning || countdownSeconds <= 0)
        {
            return;
        }

        if (!originalOffsetSaved)
        {
            preparedModel = timerModel;
            originalOffset = timerModel.CurrentState.Run.Offset;
            originalOffsetSaved = true;
            restoreOffsetOnReset = false;
            timerModel.CurrentState.OnReset += OnTimerReset;
        }

        timerModel.CurrentState.Run.Offset = TimeSpan.FromSeconds(-countdownSeconds);
        DebugLog.Info("Countdown offset prepared. Seconds: " + countdownSeconds + ".");
        timerModel.CurrentState.AdjustedStartTime =
            TimeStamp.Now - timerModel.CurrentState.Run.Offset;
    }

    private void RestoreOriginalOffset()
    {
        if (!originalOffsetSaved || preparedModel == null)
        {
            return;
        }

        if (preparedModel.CurrentState.CurrentPhase != TimerPhase.NotRunning)
        {
            // Changing Run.Offset during a run would move the active timer.
            restoreOffsetOnReset = true;
            DebugLog.Info("Offset restoration deferred until reset.");
            return;
        }

        RestoreOriginalOffsetNow();
    }

    private void OnTimerReset(object sender, TimerPhase phase)
    {
        if (restoreOffsetOnReset)
        {
            RestoreOriginalOffsetNow();
        }
    }

    private void RestoreOriginalOffsetNow()
    {
        if (!originalOffsetSaved || preparedModel == null)
        {
            return;
        }

        ITimerModel timerModel = preparedModel;
        timerModel.CurrentState.Run.Offset = originalOffset;
        DebugLog.Info("Original offset restored. Milliseconds: " + originalOffset.TotalMilliseconds.ToString("F0") + ".");
        timerModel.CurrentState.AdjustedStartTime =
            TimeStamp.Now - timerModel.CurrentState.Run.Offset;
        timerModel.CurrentState.OnReset -= OnTimerReset;

        preparedModel = null;
        originalOffset = TimeSpan.Zero;
        originalOffsetSaved = false;
        restoreOffsetOnReset = false;
    }

    private async Task<TheRunRaceDto> GetRace(
        string raceId,
        CancellationToken cancellationToken = default)
    {
        string json = await GetString(
            ApiRoot + "/" + Uri.EscapeDataString(raceId),
            cancellationToken);
        return serializer.Deserialize<RaceResponse>(json)?.result;
    }

    internal async Task LeaveRace(
        string raceId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Please log in to therun.gg first.");
        }

        TheRunRaceDto race = await GetRace(raceId, cancellationToken);
        if (race?.status != "pending")
        {
            throw new InvalidOperationException(
                "The race can no longer be left without forfeiting.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            ApiRoot + "/" + Uri.EscapeDataString(raceId) + "/participants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(responseBody)
                    ? "therun.gg rejected the action (HTTP " + (int)response.StatusCode + ")."
                    : responseBody);
        }

    }

    private async Task<string> GetString(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private bool IsWatcherCurrent(long generation, CancellationToken cancellationToken)
    {
        lock (watcherLock)
        {
            return !cancellationToken.IsCancellationRequested
                && watcherGeneration == generation
                && watcherCancellation != null
                && watcherCancellation.Token == cancellationToken;
        }
    }

    private void PostIfWatcherCurrent(
        SynchronizationContext context,
        long generation,
        CancellationToken cancellationToken,
        Action action)
    {
        void InvokeIfCurrent()
        {
            if (IsWatcherCurrent(generation, cancellationToken))
            {
                action();
            }
        }

        if (context == null)
        {
            InvokeIfCurrent();
        }
        else
        {
            context.Post(_ => InvokeIfCurrent(), null);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Error("Could not open the therun.gg race page.", ex);
            MessageBox.Show(
                "The therun.gg page could not be opened.\n\n" + ex.Message,
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
