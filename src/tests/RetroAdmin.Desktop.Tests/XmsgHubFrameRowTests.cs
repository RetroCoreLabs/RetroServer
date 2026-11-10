//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using RetroAdmin.Desktop.ViewModels;
using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// What one row of the XMSG hub page's frame list says about a frame.
/// </summary>
[TestFixture]
public class XmsgHubFrameRowTests
{
    [Test]
    public void AnNdFrame_ShowsItsSystems_AndWhoItWasSentTo()
    {
        XmsgHubFrameRow row = new XmsgHubFrameRow(new XmsgHubFrameInfo
        {
            Sequence = 7,
            IsNdFrame = true,
            SourceSystem = 102,
            DestinationSystem = 100,
            Outcome = "Forwarded",
            CopiesSent = 1,
            Hex = "0800",
            Length = 2,
        });

        Assert.That(row.Route, Is.EqualTo("102 -> 100"));
        Assert.That(row.Outcome, Is.EqualTo("sent to 1"));
    }

    [Test]
    public void ACopyThatWasLost_IsSaid()
    {
        XmsgHubFrameRow row = new XmsgHubFrameRow(new XmsgHubFrameInfo { Outcome = "Forwarded", CopiesSent = 2, CopiesDropped = 1, Hex = "00" });

        Assert.That(row.Outcome, Is.EqualTo("sent to 2, 1 lost"));
        Assert.That(row.Route, Is.EqualTo("not an ND frame"));
    }

    [Test]
    public void ADroppedFrame_ShowsWhy()
    {
        XmsgHubFrameRow row = new XmsgHubFrameRow(new XmsgHubFrameInfo { Outcome = "DroppedLoop", Hex = "00" });

        Assert.That(row.Outcome, Is.EqualTo("DroppedLoop"));
    }

    [Test]
    public void TheDump_Has16BytesALine_WithTheOffset()
    {
        string hex = "000102030405060708090A0B0C0D0E0F10";
        XmsgHubFrameRow row = new XmsgHubFrameRow(new XmsgHubFrameInfo { Hex = hex, Outcome = "Forwarded" });

        Assert.That(row.HexDump, Is.EqualTo(
            "0000  00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F \n"
            + "0010  10 \n"));
    }
}
