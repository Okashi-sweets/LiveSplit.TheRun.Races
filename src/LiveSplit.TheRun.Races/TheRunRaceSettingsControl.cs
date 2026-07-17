using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LiveSplit.TheRun.Races;

internal sealed class TheRunRaceSettingsControl : UserControl
{
    private readonly TheRunRaceSettings settings;
    private readonly TextBox keyBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button testButton = new() { Text = "Save and test", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public TheRunRaceSettingsControl(TheRunRaceSettings settings)
    {
        this.settings = settings;
        Dock = DockStyle.Fill;

        var live = new CheckBox { Text = "Send live timer updates to therun.gg", AutoSize = true, Checked = settings.IsLiveTrackingEnabled };
        var files = new CheckBox { Text = "Upload splits on completion and reset", AutoSize = true, Checked = settings.IsStatsUploadingEnabled && settings.IsUploadOnResetEnabled };
        var layout = new CheckBox { Text = "Include the LiveSplit layout path in uploads", AutoSize = true, Checked = settings.IsLayoutPathUploadEnabled };
        live.CheckedChanged += (_, _) => settings.IsLiveTrackingEnabled = live.Checked;
        files.CheckedChanged += (_, _) => settings.IsStatsUploadingEnabled = settings.IsUploadOnResetEnabled = files.Checked;
        layout.CheckedChanged += (_, _) => settings.IsLayoutPathUploadEnabled = layout.Checked;

        keyBox.Text = settings.UploadKey;
        status.Text = keyBox.TextLength == 36
            ? "Using the upload key shared with the official therun.gg component."
            : "Enter the upload key from therun.gg/livesplit.";
        testButton.Click += async (_, _) => await SaveAndValidate();

        var keyLink = new LinkLabel { Text = "Get an upload key", AutoSize = true };
        keyLink.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://therun.gg/livesplit") { UseShellExecute = true });

        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = "therun.gg upload key", AutoSize = true }, 0, 0);
        table.SetColumnSpan(keyBox, 2); table.Controls.Add(keyBox, 0, 1);
        table.Controls.Add(keyLink, 0, 2); table.Controls.Add(testButton, 1, 2);
        table.SetColumnSpan(status, 2); table.Controls.Add(status, 0, 3);
        table.SetColumnSpan(live, 2); table.Controls.Add(live, 0, 4);
        table.SetColumnSpan(files, 2); table.Controls.Add(files, 0, 5);
        table.SetColumnSpan(layout, 2); table.Controls.Add(layout, 0, 6);
        Controls.Add(table);
    }

    private async System.Threading.Tasks.Task SaveAndValidate()
    {
        settings.SaveUploadKey(keyBox.Text);
        testButton.Enabled = false;
        status.Text = "Checking the upload key...";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            HttpResponseMessage response = await client.GetAsync("https://api.therun.gg/users/uploadKey/validate/" + Uri.EscapeDataString(settings.UploadKey));
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException();
            var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
            var result = (Dictionary<string, object>)json["result"];
            var data = (Dictionary<string, object>)result["data"];
            status.ForeColor = Color.Green;
            status.Text = "Connected as " + data["username"] + ".";
        }
        catch
        {
            status.ForeColor = Color.Firebrick;
            status.Text = "The upload key could not be validated.";
        }
        finally { testButton.Enabled = true; }
    }
}
