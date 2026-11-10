//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroIO - Accessing yesterday's data with today's standards
//

using System.Collections.Generic;

using RetroAdmin.Desktop.ViewModels;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// The firewall page's address boxes: one entry per line, as typed on Windows or anywhere else.
/// </summary>
[TestFixture]
public class FirewallSettingsTests
{
    /// <summary>
    /// Windows line ends, blank lines and spaces around an entry do not become entries.
    /// </summary>
    [Test]
    public void Lines_SplitsOnePerLine_AndSkipsBlanks()
    {
        List<string> entries = FirewallViewModel.Lines("10.0.0.5\r\n\r\n  192.168.1.0/24  \n203.0.113.9");

        Assert.That(entries, Is.EqualTo(new[] { "10.0.0.5", "192.168.1.0/24", "203.0.113.9" }));
    }

    /// <summary>
    /// An empty box is an empty list, so saving it clears that list on the server.
    /// </summary>
    [Test]
    public void Lines_OfAnEmptyBox_IsEmpty()
    {
        Assert.That(FirewallViewModel.Lines(string.Empty), Is.Empty);
    }
}
