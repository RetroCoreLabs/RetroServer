//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroIO - Accessing yesterday's data with today's standards
//

using System.Collections.Generic;

using RetroAdmin.Desktop.ViewModels;
using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// The DriveWire page's drive box: one drive per line, "number = file", optional ", read-only".
/// </summary>
[TestFixture]
public class DriveWireDriveListTests
{
    /// <summary>
    /// What the page writes into the box reads back as the same drives.
    /// </summary>
    [Test]
    public void FormatThenParse_GivesTheSameDrives()
    {
        List<DriveWireDriveSettingDto> drives = new List<DriveWireDriveSettingDto>
        {
            new DriveWireDriveSettingDto { Drive = 0, Path = "/disks/boot.dsk" },
            new DriveWireDriveSettingDto { Drive = 3, Path = "C:/images/games.dsk", ReadOnly = true },
        };

        List<DriveWireDriveSettingDto> back = new List<DriveWireDriveSettingDto>();
        Assert.That(DriveWireSettingsViewModel.ParseDrives(DriveWireSettingsViewModel.FormatDrives(drives), back), Is.Null);

        Assert.That(back.Count, Is.EqualTo(2));
        Assert.That(back[0].Drive, Is.EqualTo(0));
        Assert.That(back[0].Path, Is.EqualTo("/disks/boot.dsk"));
        Assert.That(back[0].ReadOnly, Is.False);
        Assert.That(back[1].Drive, Is.EqualTo(3));
        Assert.That(back[1].Path, Is.EqualTo("C:/images/games.dsk"));
        Assert.That(back[1].ReadOnly, Is.True);
    }

    /// <summary>
    /// A line without a drive number, or without a file, is reported with its line number; blank lines are skipped.
    /// </summary>
    [Test]
    public void BadLines_AreReported_BlankLinesSkipped()
    {
        List<DriveWireDriveSettingDto> drives = new List<DriveWireDriveSettingDto>();
        Assert.That(DriveWireSettingsViewModel.ParseDrives("\r\n0 = a.dsk\r\n\r\n", drives), Is.Null);
        Assert.That(drives.Count, Is.EqualTo(1));

        Assert.That(DriveWireSettingsViewModel.ParseDrives("0 = a.dsk\nx = b.dsk", new List<DriveWireDriveSettingDto>()), Does.Contain("line 2"));
        Assert.That(DriveWireSettingsViewModel.ParseDrives("1 = ", new List<DriveWireDriveSettingDto>()), Does.Contain("no image file"));
    }
}
