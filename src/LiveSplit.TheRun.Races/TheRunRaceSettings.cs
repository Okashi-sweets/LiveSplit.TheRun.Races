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

    public string UploadKey
    {
        get
        {
            try
            {
                return File.Exists(UploadKeyFile)
                    ? File.ReadAllText(UploadKeyFile).Trim()
                    : "";
            }
            catch (Exception ex)
            {
                DebugLog.Error("Could not read the shared therun.gg upload key.", ex);
                return "";
            }
        }
    }

    public override string Name
    {
        get => "LiveSplit.TheRun.Races.dll";
        set { }
    }

    public override string DisplayName => "therun.gg";

    public override string WebsiteLink => "https://therun.gg/races";

    public override string RulesLink => "https://therun.gg/races";

    public override Control GetSettingsControl() => new TheRunRaceSettingsControl(this);

    internal void SaveUploadKey(string key)
    {
        string directory = Path.GetDirectoryName(UploadKeyFile);
        Directory.CreateDirectory(directory);
        string temporaryFile = Path.Combine(
            directory,
            Path.GetFileName(UploadKeyFile) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporaryFile, (key ?? "").Trim());
            if (File.Exists(UploadKeyFile))
            {
                File.Replace(temporaryFile, UploadKeyFile, null);
            }
            else
            {
                File.Move(temporaryFile, UploadKeyFile);
            }
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    public override object Clone() => new TheRunRaceSettings
    {
        Enabled = Enabled,
        IsLiveTrackingEnabled = IsLiveTrackingEnabled,
        IsStatsUploadingEnabled = IsStatsUploadingEnabled,
        IsUploadOnResetEnabled = IsUploadOnResetEnabled,
        IsLayoutPathUploadEnabled = IsLayoutPathUploadEnabled
    };

    public override void FromXml(XmlElement element, Version version)
    {
        base.FromXml(element, version);
        IsLiveTrackingEnabled = ReadBool(element, nameof(IsLiveTrackingEnabled), true);
        IsStatsUploadingEnabled = ReadBool(element, nameof(IsStatsUploadingEnabled), true);
        IsUploadOnResetEnabled = ReadBool(element, nameof(IsUploadOnResetEnabled), true);
        IsLayoutPathUploadEnabled = ReadBool(element, nameof(IsLayoutPathUploadEnabled), false);
    }

    public override XmlElement ToXml(XmlDocument document)
    {
        XmlElement element = base.ToXml(document);
        Add(document, element, nameof(IsLiveTrackingEnabled), IsLiveTrackingEnabled);
        Add(document, element, nameof(IsStatsUploadingEnabled), IsStatsUploadingEnabled);
        Add(document, element, nameof(IsUploadOnResetEnabled), IsUploadOnResetEnabled);
        Add(document, element, nameof(IsLayoutPathUploadEnabled), IsLayoutPathUploadEnabled);
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
