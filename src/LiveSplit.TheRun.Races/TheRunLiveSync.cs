using LiveSplit.Model;
using LiveSplit.Model.RunSavers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Xml;

namespace LiveSplit.TheRun.Races;

internal sealed class TheRunLiveSync : IDisposable
{
    private const int MaxApiResponseBytes = 1024 * 1024;
    private const string UploadHost = "splits-bucket-main.s3.eu-west-1.amazonaws.com";
    private const string LiveUrl = "https://dspc6ekj2gjkfp44cjaffhjeue0fbswr.lambda-url.eu-west-1.on.aws/";
    private const string UploadUrl = "https://2uxp372ks6nwrjnk6t7lqov4zu0solno.lambda-url.eu-west-1.on.aws/";
    private readonly HttpClient client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = MaxApiResponseBytes
    };
    private readonly object liveCancellationLock = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim uploadGate = new(1, 1);
    private CancellationTokenSource liveCancellation;
    private bool disposed;
    private bool paused;
    private bool justResumed;
    private TimeSpan pausedAtLastResume;
    private TimeSpan currentPausedTime;

    internal LiveSplitState State { get; }
    internal TheRunRaceSettings Settings { get; set; }

    internal TheRunLiveSync(LiveSplitState state, TheRunRaceSettings settings)
    {
        State = state;
        Settings = settings;
        client.DefaultRequestHeaders.Add("Accept", "*/*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Disposition", "attachment");
        State.OnStart += HandleSplit;
        State.OnSplit += HandleSplit;
        State.OnSkipSplit += HandleSplit;
        State.OnUndoSplit += HandleSplit;
        State.OnUndoAllPauses += HandleSplit;
        State.OnPause += HandlePause;
        State.OnResume += HandleResume;
        State.OnReset += HandleReset;
    }

    private bool OfficialSenderIsActive() => State.Layout?.Components?.Any(component =>
        component.GetType().FullName == "LiveSplit.UI.Components.CollectorComponent"
        && component.GetType().Assembly.GetName().Name == "LiveSplit.TheRun") == true;

    private string GetSendBlockReason()
    {
        if (Settings == null || !Settings.Enabled)
            return "the therun.gg race provider is disabled";
        if (OfficialSenderIsActive())
            return "the official LiveSplit.TheRun sender is active in the current layout";
        if (string.IsNullOrWhiteSpace(State.Run.GameName))
            return "the run has no game name";
        if (string.IsNullOrWhiteSpace(State.Run.CategoryName))
            return "the run has no category name";

        int uploadKeyLength = Settings?.UploadKey?.Length ?? 0;
        if (uploadKeyLength != 36)
            return "the upload key length is " + uploadKeyLength + " instead of 36";

        return null;
    }

    private bool CanSend(string operation)
    {
        string reason = GetSendBlockReason();
        if (reason == null) return true;

        DebugLog.Info(operation + " skipped because " + reason + ".");
        return false;
    }

    private async void HandleSplit(object sender, object args)
    {
        if (Settings == null || !Settings.Enabled)
        {
            DebugLog.Info("Timer synchronization skipped because the race provider is disabled.");
            return;
        }
        if (!Settings.IsLiveTrackingEnabled && !Settings.IsStatsUploadingEnabled)
        {
            DebugLog.Info("Timer synchronization skipped because live tracking and stats uploading are disabled.");
            return;
        }
        if (!CanSend("Timer synchronization")) return;

        bool shouldUpload = State.CurrentSplitIndex == State.Run.Count
            && Settings.IsStatsUploadingEnabled;
        Task liveTask = Settings.IsLiveTrackingEnabled ? SendLive() : null;
        Task uploadTask = shouldUpload ? UploadSplits() : null;
        // SendLive captures the resume flag synchronously before its first await.
        justResumed = false;

        if (liveTask != null)
        {
            try
            {
                await liveTask;
            }
            catch (OperationCanceledException)
            {
                DebugLog.Info("Superseded live timer update cancelled.");
            }
            catch (Exception ex)
            {
                DebugLog.Error("Live timer update failed.", ex);
            }
        }

        if (uploadTask != null)
        {
            try
            {
                await uploadTask;
            }
            catch (OperationCanceledException)
            {
                DebugLog.Info("Completion upload cancelled.");
            }
            catch (Exception ex)
            {
                DebugLog.Error("Completion upload failed.", ex);
            }
        }
    }

    private void HandlePause(object sender, object args)
    {
        paused = true;
        HandleSplit(sender, args);
    }

    private void HandleResume(object sender, object args)
    {
        pausedAtLastResume = (TimeSpan)(State.PauseTime - currentPausedTime);
        currentPausedTime = (TimeSpan)State.PauseTime;
        paused = false;
        justResumed = true;
        HandleSplit(sender, args);
    }

    private async void HandleReset(object sender, TimerPhase phase)
    {
        if (Settings == null || !Settings.Enabled)
        {
            ResetPauseState();
            DebugLog.Info("Reset synchronization skipped because the race provider is disabled.");
            return;
        }
        if (!Settings.IsLiveTrackingEnabled
            && !(Settings.IsStatsUploadingEnabled && Settings.IsUploadOnResetEnabled))
        {
            ResetPauseState();
            DebugLog.Info("Reset synchronization skipped because live tracking and reset uploads are disabled.");
            return;
        }
        if (!CanSend("Reset synchronization"))
        {
            ResetPauseState();
            return;
        }
        Task liveTask = Settings.IsLiveTrackingEnabled ? SendLive() : null;
        Task uploadTask = Settings.IsStatsUploadingEnabled && Settings.IsUploadOnResetEnabled
            ? UploadSplits()
            : null;
        ResetPauseState();

        if (liveTask != null)
        {
            try
            {
                await liveTask;
            }
            catch (OperationCanceledException)
            {
                DebugLog.Info("Reset live update cancelled.");
            }
            catch (Exception ex)
            {
                DebugLog.Error("Reset live update failed.", ex);
            }
        }

        if (uploadTask != null)
        {
            try
            {
                await uploadTask;
            }
            catch (OperationCanceledException)
            {
                DebugLog.Info("Reset LSS upload cancelled.");
            }
            catch (Exception ex)
            {
                DebugLog.Error("Reset LSS upload failed.", ex);
            }
        }
    }

    private void ResetPauseState()
    {
        paused = false;
        justResumed = false;
        pausedAtLastResume = TimeSpan.Zero;
        currentPausedTime = TimeSpan.Zero;
    }

    private async Task SendLive()
    {
        CancellationTokenSource requestCancellation;
        lock (liveCancellationLock)
        {
            if (disposed)
            {
                return;
            }

            liveCancellation?.Cancel();
            liveCancellation?.Dispose();
            requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation.Token);
            liveCancellation = requestCancellation;
        }

        string json = new JavaScriptSerializer().Serialize(BuildLiveData());
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(
                LiveUrl,
                content,
                requestCancellation.Token);
            response.EnsureSuccessStatusCode();
            DebugLog.Info("Live timer state sent. Split index: " + State.CurrentSplitIndex + ".");
        }
        finally
        {
            lock (liveCancellationLock)
            {
                if (ReferenceEquals(liveCancellation, requestCancellation))
                {
                    liveCancellation = null;
                }
            }
            requestCancellation.Dispose();
        }
    }

    private object BuildLiveData()
    {
        var runData = new List<object>();
        foreach (ISegment segment in State.Run)
        {
            var comparisons = segment.Comparisons.Keys.Select(name => new
            {
                name,
                time = ConvertTime(segment.Comparisons[name])
            }).Cast<object>().ToList();
            runData.Add(new
            {
                name = segment.Name,
                splitTime = ConvertTime(segment.SplitTime),
                pbSplitTime = ConvertTime(segment.PersonalBestSplitTime),
                bestPossible = ConvertTime(segment.BestSegmentTime),
                comparisons
            });
        }

        return new
        {
            metadata = new
            {
                game = State.Run.GameName,
                category = State.Run.CategoryName,
                platform = State.Run.Metadata.PlatformName,
                region = State.Run.Metadata.RegionName,
                emulator = State.Run.Metadata.UsesEmulator,
                variables = State.Run.Metadata.VariableValueNames
            },
            currentTime = ConvertTime(State.CurrentTime),
            currentSplitName = State.CurrentSplit?.Name ?? "",
            currentSplitIndex = State.CurrentSplitIndex,
            timingMethod = State.CurrentTimingMethod,
            currentDuration = State.CurrentAttemptDuration.TotalMilliseconds,
            startTime = State.AttemptStarted.Time.ToUniversalTime(),
            endTime = State.AttemptEnded.Time.ToUniversalTime(),
            uploadKey = Settings.UploadKey,
            isPaused = paused,
            isGameTimePaused = State.IsGameTimePaused,
            gameTimePauseTime = State.GameTimePauseTime,
            totalPauseTime = State.PauseTime,
            currentPauseTime = pausedAtLastResume,
            timePausedAt = State.TimePausedAt.TotalMilliseconds,
            wasJustResumed = justResumed,
            currentComparison = State.CurrentComparison,
            runData
        };
    }

    private double? ConvertTime(Time time) =>
        time[State.CurrentTimingMethod]?.TotalMilliseconds;

    private async Task UploadSplits()
    {
        // Capture the completed attempt before waiting. A reset or a new run can
        // otherwise mutate LiveSplitState while another upload is in progress.
        string game = State.Run.GameName;
        string category = State.Run.CategoryName;
        string uploadKey = Settings.UploadKey;
        string lss = CreateLss();
        CancellationToken cancellationToken = lifetimeCancellation.Token;
        await uploadGate.WaitAsync(cancellationToken);
        try
        {
            string fileName = HttpUtility.UrlEncode(game) + "-" + HttpUtility.UrlEncode(category) + ".lss";
            using HttpResponseMessage result = await client.GetAsync(
                UploadUrl + "?filename=" + fileName + "&uploadKey="
                + Uri.EscapeDataString(uploadKey),
                cancellationToken);
            result.EnsureSuccessStatusCode();
            var response = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(
                await result.Content.ReadAsStringAsync());
            string url = EncodeUrl(HttpUtility.UrlDecode(response["url"]), game, category);
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uploadUri)
                || uploadUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uploadUri.Host, UploadHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The upload service returned an unexpected destination.");
            }

            using var content = new StringContent(lss);
            content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
            using HttpResponseMessage put = await client.PutAsync(
                uploadUri,
                content,
                cancellationToken);
            put.EnsureSuccessStatusCode();
            DebugLog.Info("LSS upload completed. Game: " + game
                + ", category: " + category + ".");
        }
        finally
        {
            uploadGate.Release();
        }
    }

    private string CreateLss()
    {
        using var stream = new MemoryStream();
        new XMLRunSaver().Save(State.Run, stream);
        stream.Position = 0;
        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(stream);
        XmlElement run = document.DocumentElement;
        if (run?["GameIcon"] != null) run["GameIcon"].InnerText = "";
        if (!Settings.IsLayoutPathUploadEnabled && run?["LayoutPath"] != null) run["LayoutPath"].InnerText = "";
        XmlElement segments = run?["Segments"];
        if (segments != null)
            foreach (XmlElement segment in segments.GetElementsByTagName("Segment"))
                if (segment["Icon"] != null) segment["Icon"].InnerText = "";
        return document.OuterXml;
    }

    private static string EncodeUrl(string url, string game, string category)
    {
        string[] parts = url.Split('&').Select(part =>
            part.StartsWith("X-Amz-Credential") || part.StartsWith("X-Amz-Security-Token") || part.StartsWith("X-Amz-SignedHeaders")
                ? HttpUtility.UrlEncode(part).Replace("%3d", "=") : part).ToArray();
        string encoded = string.Join("&", parts).Replace(game, HttpUtility.UrlEncode(game)).Replace(category, HttpUtility.UrlEncode(category));
        string username = encoded.Replace("https://splits-bucket-main.s3.eu-west-1.amazonaws.com/", "").Split('/')[0];
        return encoded.Replace(username, HttpUtility.UrlEncode(username));
    }

    public void Dispose()
    {
        lock (liveCancellationLock)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            lifetimeCancellation.Cancel();
            liveCancellation?.Cancel();
        }

        State.OnStart -= HandleSplit;
        State.OnSplit -= HandleSplit;
        State.OnSkipSplit -= HandleSplit;
        State.OnUndoSplit -= HandleSplit;
        State.OnUndoAllPauses -= HandleSplit;
        State.OnPause -= HandlePause;
        State.OnResume -= HandleResume;
        State.OnReset -= HandleReset;
        client.Dispose();
    }
}
