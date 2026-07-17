using LiveSplit.Model;
using LiveSplit.Model.Input;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net;
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

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly JavaScriptSerializer serializer = new();
    private readonly object watcherLock = new();
    private IReadOnlyList<TheRunRaceInfo> races = [];
    private CancellationTokenSource watcherCancellation;
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
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        JoinRace = Join;
        CreateRace = _ => OpenUrl(WebsiteRoot + "/create");
    }

    public override string ProviderName => "therun.gg";

    public override string Username => null;

    public override IEnumerable<IRaceInfo> GetRaces() => races;

    internal void ConfigureLiveSync(LiveSplitState state, TheRunRaceSettings settings)
    {
        if (liveSync?.State == state)
        {
            liveSync.Settings = settings;
            return;
        }

        liveSync?.Dispose();
        liveSync = new TheRunLiveSync(state, settings);
    }

    public override void RefreshRacesListAsync() => _ = RefreshRacesList();

    private async Task RefreshRacesList()
    {
        try
        {
            string json = await httpClient.GetStringAsync(ApiRoot + "/active");
            RaceListResponse response = serializer.Deserialize<RaceListResponse>(json);
            races = response?.result?
                .Where(race => race.status == "pending" && race.visible && !string.IsNullOrWhiteSpace(race.raceId))
                .Select(TheRunRaceInfo.FromDto)
                .ToArray() ?? [];
            LastRefreshError = null;
            RacesRefreshedCallback?.Invoke(this);
        }
        catch (Exception ex)
        {
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

        TheRunRaceDto race;
        try
        {
            race = await GetRace(raceId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The race information could not be loaded.\n\n" + ex.Message,
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (race?.status != "pending")
        {
            MessageBox.Show(
                "This race has already started or is no longer accepting participants.",
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            RefreshRacesListAsync();
            return;
        }

        PrepareCountdownOffset(model, race.countdownSeconds);

        roomForm?.Close();
        roomForm = new RaceRoomForm(
            this,
            raceId,
            WebsiteRoot + "/" + Uri.EscapeDataString(raceId),
            model.CurrentState.LayoutSettings.AlwaysOnTop);
        StartWatcher(model, raceId);
    }

    internal void StartWatcher(ITimerModel model, string raceId)
    {
        CancellationTokenSource cancellation = new();
        lock (watcherLock)
        {
            watcherCancellation?.Cancel();
            watcherCancellation?.Dispose();
            watcherCancellation = cancellation;
        }

        SynchronizationContext uiContext = SynchronizationContext.Current;
        _ = WatchRace(model, raceId, uiContext, cancellation.Token);
    }

    internal void CancelWatcher()
    {
        lock (watcherLock)
        {
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
        RestoreOriginalOffset();
        roomForm = null;
    }

    private async Task WatchRace(
        ITimerModel model,
        string raceId,
        SynchronizationContext uiContext,
        CancellationToken cancellationToken)
    {
        try
        {
            using var socket = new ClientWebSocket();
            var socketUri = new Uri(WebSocketRoot + "?race=" + Uri.EscapeDataString(raceId));
            await socket.ConnectAsync(socketUri, cancellationToken);

            // The socket does not send an initial snapshot, so close the race-between-check-and-connect gap.
            TheRunRaceDto snapshot = await GetRace(raceId);
            if (TryScheduleStart(model, snapshot, uiContext))
            {
                return;
            }

            byte[] buffer = new byte[16 * 1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                string message = await ReceiveMessage(socket, buffer, cancellationToken);
                if (message == null)
                {
                    return;
                }

                RaceWebSocketMessage update = serializer.Deserialize<RaceWebSocketMessage>(message);
                if (update?.type == "raceUpdate" && update.data?.raceId == raceId)
                {
                    if (TryScheduleStart(model, update.data, uiContext))
                    {
                        return;
                    }

                    if (update.data.status is "progress" or "finished" or "aborted")
                    {
                        Post(uiContext, RestoreOriginalOffset);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Post(uiContext, () => MessageBox.Show(
                "The connection to the therun.gg race room was lost before the countdown started.\n\n" + ex.Message,
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning));
        }
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

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return builder.ToString();
    }

    private bool TryScheduleStart(
        ITimerModel model,
        TheRunRaceDto race,
        SynchronizationContext uiContext)
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

        Post(uiContext, () => StartTimer(model, startTime.ToUniversalTime()));
        return true;
    }

    private void StartTimer(ITimerModel model, DateTime startTimeUtc)
    {
        ITimerModel timerModel = model is DoubleTapPrevention prevention
            ? prevention.InternalModel
            : model;

        if (timerModel.CurrentState.CurrentPhase != TimerPhase.NotRunning)
        {
            MessageBox.Show(
                "The therun.gg countdown started, but the LiveSplit timer was already running.",
                "therun.gg Races",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        TimeSpan remaining = startTimeUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            // Joining after the countdown is deliberately unsupported.
            return;
        }

        timerModel.CurrentState.Run.Offset = remaining.Negate();
        timerModel.CurrentState.AdjustedStartTime = TimeStamp.Now - timerModel.CurrentState.Run.Offset;
        timerModel.Start();
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
        timerModel.CurrentState.AdjustedStartTime =
            TimeStamp.Now - timerModel.CurrentState.Run.Offset;
        timerModel.CurrentState.OnReset -= OnTimerReset;

        preparedModel = null;
        originalOffset = TimeSpan.Zero;
        originalOffsetSaved = false;
        restoreOffsetOnReset = false;
    }

    private async Task<TheRunRaceDto> GetRace(string raceId)
    {
        string json = await httpClient.GetStringAsync(
            ApiRoot + "/" + Uri.EscapeDataString(raceId));
        return serializer.Deserialize<RaceResponse>(json)?.result;
    }

    private static void Post(SynchronizationContext context, Action action)
    {
        if (context == null)
        {
            action();
        }
        else
        {
            context.Post(_ => action(), null);
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
