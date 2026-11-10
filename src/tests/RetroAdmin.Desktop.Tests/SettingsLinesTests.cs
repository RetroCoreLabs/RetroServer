//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroIO - Accessing yesterday's data with today's standards
//

using System.Collections.Generic;

using RetroAdmin.Desktop.Services;
using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// The list boxes on the settings pages: what a page writes reads back the same, and a bad line is named.
/// </summary>
[TestFixture]
public class SettingsLinesTests
{
    /// <summary>
    /// Discs of both kinds, with options, round-trip.
    /// </summary>
    [Test]
    public void Discs_RoundTrip()
    {
        List<EconetDiscDto> discs = new List<EconetDiscDto>
        {
            new EconetDiscDto { Index = 0, AcornName = "Games", Provider = "native", Uri = "C:/discs/games" },
            new EconetDiscDto { Index = 1, AcornName = "Work", MountRef = "work", ReadOnly = true, DirName = "1Work" },
        };

        List<EconetDiscDto> back = new List<EconetDiscDto>();
        Assert.That(SettingsLines.ParseDiscs(SettingsLines.FormatDiscs(discs), back), Is.Null);
        Assert.That(back.Count, Is.EqualTo(2));
        Assert.That(back[0].Provider, Is.EqualTo("native"));
        Assert.That(back[0].Uri, Is.EqualTo("C:/discs/games"), "only the first colon splits, so a drive letter survives");
        Assert.That(back[1].MountRef, Is.EqualTo("work"));
        Assert.That(back[1].ReadOnly, Is.True);
        Assert.That(back[1].DirName, Is.EqualTo("1Work"));

        Assert.That(SettingsLines.ParseDiscs("0 | X | /no/kind", new List<EconetDiscDto>()), Does.Contain("line 1"));
        Assert.That(SettingsLines.ParseDiscs("0 | X | native:/x | fast", new List<EconetDiscDto>()), Does.Contain("fast"));
    }

    /// <summary>
    /// Per-machine reply formats round-trip; a line without a machine is named.
    /// </summary>
    [Test]
    public void Overrides_RoundTrip()
    {
        List<EconetWireProfileOverrideDto> list = new List<EconetWireProfileOverrideDto>
        {
            new EconetWireProfileOverrideDto { Net = 1, Station = 100, Profile = "Classic1983" },
        };

        List<EconetWireProfileOverrideDto> back = new List<EconetWireProfileOverrideDto>();
        Assert.That(SettingsLines.ParseOverrides(SettingsLines.FormatOverrides(list), back), Is.Null);
        Assert.That(back[0].Station, Is.EqualTo(100));
        Assert.That(back[0].Profile, Is.EqualTo("Classic1983"));
        Assert.That(SettingsLines.ParseOverrides("100 = LaterFS", new List<EconetWireProfileOverrideDto>()), Does.Contain("line 1"));
    }

    /// <summary>
    /// A stored password shows as "(kept)" and reads back as "keep"; a new one is sent; none at all removes it.
    /// </summary>
    [Test]
    public void Mounts_PasswordRules()
    {
        List<MountDto> stored = new List<MountDto>
        {
            new MountDto { VolumeId = "work", ProviderType = "tnfs", SourceUri = "tnfs://h/w", Username = "bob", PasswordSet = true, IsReadOnly = true },
        };

        List<MountDto> back = new List<MountDto>();
        Assert.That(SettingsLines.ParseMounts(SettingsLines.FormatMounts(stored), back), Is.Null);
        Assert.That(back[0].NewPassword, Is.Null, "(kept) keeps the stored password");
        Assert.That(back[0].Username, Is.EqualTo("bob"));
        Assert.That(back[0].IsReadOnly, Is.True);

        back.Clear();
        SettingsLines.ParseMounts("work | tnfs | tnfs://h/w | password=new", back);
        Assert.That(back[0].NewPassword, Is.EqualTo("new"));

        back.Clear();
        SettingsLines.ParseMounts("work | tnfs | tnfs://h/w", back);
        Assert.That(back[0].NewPassword, Is.EqualTo(string.Empty), "no password item removes it");
    }

    /// <summary>
    /// Log levels round-trip.
    /// </summary>
    [Test]
    public void LogLevels_RoundTrip()
    {
        Dictionary<string, string> levels = new Dictionary<string, string> { ["Default"] = "Information", ["Microsoft"] = "Warning" };
        Dictionary<string, string> back = new Dictionary<string, string>();
        Assert.That(SettingsLines.ParseLogLevels(SettingsLines.FormatLogLevels(levels), back), Is.Null);
        Assert.That(back, Is.EqualTo(levels));
        Assert.That(SettingsLines.ParseLogLevels("Microsoft", new Dictionary<string, string>()), Does.Contain("line 1"));
    }
}
