using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.UI.Components;
using System;
using System.IO;
using System.Reflection;

[assembly: ComponentFactory(typeof(LiveSplit.TheRun.Races.TheRunRaceFactory))]

namespace LiveSplit.TheRun.Races;

public sealed class TheRunRaceFactory : IRaceProviderFactory
{
    public TheRunRaceFactory()
    {
        string componentDirectory = Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrWhiteSpace(componentDirectory))
        {
            return;
        }

        string legacyLitePath = Path.Combine(
            componentDirectory,
            "LiveSplit.TheRun.Races.Lite.dll");
        if (File.Exists(legacyLitePath))
        {
            DebugLog.Info(
                "Component loading stopped because the legacy Lite DLL is still installed.");
            throw new InvalidOperationException(
                "Remove LiveSplit.TheRun.Races.Lite.dll before installing LiveSplit.TheRun.Races.dll.");
        }
    }

    public RaceProviderAPI Create(ITimerModel model, RaceProviderSettings settings)
    {
        TheRunRaceAPI.Instance.Settings = settings;
        TheRunRaceAPI.Instance.ConfigureLiveSync(model.CurrentState, settings as TheRunRaceSettings);
        return TheRunRaceAPI.Instance;
    }

    public RaceProviderSettings CreateSettings() => new TheRunRaceSettings();

    public string UpdateName => "therun.gg Race Integration";

    public string UpdateURL => "";

    public string XMLURL => "";

    public Version Version => new(0, 4, 0);
}
