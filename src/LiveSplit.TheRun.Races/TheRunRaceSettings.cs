using LiveSplit.Options;
using System;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.TheRun.Races;

public sealed class TheRunRaceSettings : RaceProviderSettings
{
    internal static readonly string UploadKeyFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Livesplit.TheRun", "uploadkey.txt");

    public bool IsLiveTrackingEnabled { get; set; } = true;
    public bool IsStatsUploadingEnabled { get; set; } = true;
    public bool IsUploadOnResetEnabled { get; set; } = true;
    public bool IsLayoutPathUploadEnabled { get; set; }
    public bool UseLiteRaceRoom { get; set; }

    public string UploadKey => File.Exists(UploadKeyFile)
        ? File.ReadAllText(UploadKeyFile).Trim()
        : "";

    public override string Name { get => "LiveSplit.TheRun.Races.dll"; set { } }

    public override string DisplayName => "therun.gg Races";

    public override string WebsiteLink => "https://therun.gg/races";

    public override string RulesLink => "https://therun.gg/races";

    public override Control GetSettingsControl() => new TheRunRaceSettingsControl(this);

    internal void SaveUploadKey(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UploadKeyFile));
        File.WriteAllText(UploadKeyFile, (key ?? "").Trim());
    }

    public override object Clone() => new TheRunRaceSettings
    {
        Enabled = Enabled,
        IsLiveTrackingEnabled = IsLiveTrackingEnabled,
        IsStatsUploadingEnabled = IsStatsUploadingEnabled,
        IsUploadOnResetEnabled = IsUploadOnResetEnabled,
        IsLayoutPathUploadEnabled = IsLayoutPathUploadEnabled,
        UseLiteRaceRoom = UseLiteRaceRoom
    };

    public override void FromXml(XmlElement element, Version version)
    {
        base.FromXml(element, version);
        IsLiveTrackingEnabled = ReadBool(element, nameof(IsLiveTrackingEnabled), true);
        IsStatsUploadingEnabled = ReadBool(element, nameof(IsStatsUploadingEnabled), true);
        IsUploadOnResetEnabled = ReadBool(element, nameof(IsUploadOnResetEnabled), true);
        IsLayoutPathUploadEnabled = ReadBool(element, nameof(IsLayoutPathUploadEnabled), false);
        UseLiteRaceRoom = ReadBool(element, nameof(UseLiteRaceRoom), false);
    }

    public override XmlElement ToXml(XmlDocument document)
    {
        XmlElement element = base.ToXml(document);
        Add(document, element, nameof(IsLiveTrackingEnabled), IsLiveTrackingEnabled);
        Add(document, element, nameof(IsStatsUploadingEnabled), IsStatsUploadingEnabled);
        Add(document, element, nameof(IsUploadOnResetEnabled), IsUploadOnResetEnabled);
        Add(document, element, nameof(IsLayoutPathUploadEnabled), IsLayoutPathUploadEnabled);
        Add(document, element, nameof(UseLiteRaceRoom), UseLiteRaceRoom);
        return element;
    }

    private static bool ReadBool(XmlElement parent, string name, bool fallback) =>
        bool.TryParse(parent[name]?.InnerText, out bool value) ? value : fallback;

    private static void Add(XmlDocument document, XmlElement parent, string name, bool value)
    {
        XmlElement child = document.CreateElement(name);
        child.InnerText = value.ToString();
        parent.AppendChild(child);
    }
}
