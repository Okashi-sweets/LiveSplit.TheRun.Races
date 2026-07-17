using LiveSplit.Model;
using LiveSplit.Model.RunSavers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Xml;

namespace LiveSplit.TheRun.Races;

internal sealed class TheRunLiveSync : IDisposable
{
    private const string LiveUrl = "https://dspc6ekj2gjkfp44cjaffhjeue0fbswr.lambda-url.eu-west-1.on.aws/";
    private const string UploadUrl = "https://2uxp372ks6nwrjnk6t7lqov4zu0solno.lambda-url.eu-west-1.on.aws/";
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource liveCancellation;
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

    private bool CanSend => !OfficialSenderIsActive()
        && !string.IsNullOrWhiteSpace(State.Run.GameName)
        && !string.IsNullOrWhiteSpace(State.Run.CategoryName)
        && Settings.UploadKey.Length == 36;

    private bool OfficialSenderIsActive() => State.Layout?.Components?.Any(component =>
        component.GetType().FullName == "LiveSplit.UI.Components.CollectorComponent"
        && component.GetType().Assembly.GetName().Name == "LiveSplit.TheRun") == true;

    private async void HandleSplit(object sender, object args)
    {
        if (!CanSend || !Settings.IsLiveTrackingEnabled) return;
        try
        {
            await SendLive();
            if (State.CurrentSplitIndex == State.Run.Count && Settings.IsStatsUploadingEnabled)
                await UploadSplits();
        }
        catch { }
        justResumed = false;
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
        if (!CanSend) return;
        try
        {
            if (Settings.IsLiveTrackingEnabled) await SendLive();
            if (Settings.IsStatsUploadingEnabled && Settings.IsUploadOnResetEnabled) await UploadSplits();
        }
        catch { }
    }

    private async Task SendLive()
    {
        liveCancellation?.Cancel();
        liveCancellation?.Dispose();
        liveCancellation = new CancellationTokenSource();
        string json = new JavaScriptSerializer().Serialize(BuildLiveData());
        using var content = new StringContent(json);
        HttpResponseMessage response = await client.PostAsync(LiveUrl, content, liveCancellation.Token);
        response.EnsureSuccessStatusCode();
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
        string game = State.Run.GameName;
        string category = State.Run.CategoryName;
        string fileName = HttpUtility.UrlEncode(game) + "-" + HttpUtility.UrlEncode(category) + ".lss";
        HttpResponseMessage result = await client.GetAsync(UploadUrl + "?filename=" + fileName + "&uploadKey=" + Settings.UploadKey);
        result.EnsureSuccessStatusCode();
        var response = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(await result.Content.ReadAsStringAsync());
        string url = EncodeUrl(HttpUtility.UrlDecode(response["url"]), game, category);
        using var content = new StringContent(CreateLss());
        content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
        HttpResponseMessage put = await client.PutAsync(url, content);
        put.EnsureSuccessStatusCode();
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
        State.OnStart -= HandleSplit;
        State.OnSplit -= HandleSplit;
        State.OnSkipSplit -= HandleSplit;
        State.OnUndoSplit -= HandleSplit;
        State.OnUndoAllPauses -= HandleSplit;
        State.OnPause -= HandlePause;
        State.OnResume -= HandleResume;
        State.OnReset -= HandleReset;
        liveCancellation?.Cancel();
        liveCancellation?.Dispose();
        client.Dispose();
    }
}
