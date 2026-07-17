using LiveSplit.Model;
using System;
using System.Collections.Generic;

namespace LiveSplit.TheRun.Races;

public sealed class TheRunRaceInfo : IRaceInfo
{
    internal string RawStatus { get; set; }
    internal string RawStartTime { get; set; }

    public int Finishes { get; set; }
    public int Forfeits => 0;
    public string GameId => GameName;
    public string GameName { get; set; }
    public string Goal { get; set; }
    public string Id { get; set; }
    public IEnumerable<string> LiveStreams => [];
    public int NumEntrants { get; set; }

    public int Starttime
    {
        get
        {
            if (!DateTime.TryParse(RawStartTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return 0;
            }

            var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (int)(parsed.ToUniversalTime() - unixEpoch).TotalSeconds;
        }
    }

    // LiveSplit displays only state 1 as a joinable race in the provider submenu.
    public int State => RawStatus == "pending" ? 1 : 42;

    public bool IsParticipant(string username) => false;

    internal static TheRunRaceInfo FromDto(TheRunRaceDto race) => new()
    {
        Id = race.raceId,
        RawStatus = race.status,
        RawStartTime = race.startTime,
        GameName = race.displayGame ?? "therun.gg",
        Goal = string.IsNullOrWhiteSpace(race.customName)
            ? race.displayCategory ?? "Race"
            : race.customName,
        NumEntrants = race.participantCount,
        Finishes = race.finishedParticipantCount
    };
}

internal sealed class RaceListResponse
{
    public TheRunRaceDto[] result { get; set; }
}

internal sealed class RaceResponse
{
    public TheRunRaceDto result { get; set; }
}

internal sealed class RaceWebSocketMessage
{
    public string type { get; set; }
    public TheRunRaceDto data { get; set; }
}

internal sealed class TheRunRaceDto
{
    public string raceId { get; set; }
    public string status { get; set; }
    public string displayGame { get; set; }
    public string displayCategory { get; set; }
    public string customName { get; set; }
    public string startTime { get; set; }
    public bool visible { get; set; }
    public int participantCount { get; set; }
    public int finishedParticipantCount { get; set; }
    public int countdownSeconds { get; set; }
}
