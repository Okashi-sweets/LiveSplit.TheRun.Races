using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.UI.Components;
using System;

[assembly: ComponentFactory(typeof(LiveSplit.TheRun.Races.TheRunRaceFactory))]

namespace LiveSplit.TheRun.Races;

public sealed class TheRunRaceFactory : IRaceProviderFactory
{
    public RaceProviderAPI Create(ITimerModel model, RaceProviderSettings settings)
    {
        TheRunRaceAPI.Instance.Settings = settings;
        TheRunRaceAPI.Instance.ConfigureLiveSync(model.CurrentState, (TheRunRaceSettings)settings);
        return TheRunRaceAPI.Instance;
    }

    public RaceProviderSettings CreateSettings() => new TheRunRaceSettings();

#if LITE_ROOM
    public string UpdateName => "therun.gg Race Integration Lite";
#else
    public string UpdateName => "therun.gg Race Integration";
#endif

    public string UpdateURL => "";

    public string XMLURL => "";

    public Version Version => new(0, 3, 1);
}
