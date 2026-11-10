//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroIO - Accessing yesterday's data with today's standards
//

using RetroAdmin.Desktop.ViewModels;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// The limits page's boxes: empty means no limit, a number is the limit, anything else is refused.
/// </summary>
[TestFixture]
public class SecurityLimitBoxTests
{
    /// <summary>
    /// Empty and blank boxes are "no limit"; numbers are read; text is refused.
    /// </summary>
    [Test]
    public void TryLimit_ReadsTheBox()
    {
        Assert.That(SecuritySettingsViewModel.TryLimit("  ", out int? none), Is.True);
        Assert.That(none, Is.Null);

        Assert.That(SecuritySettingsViewModel.TryLimit(" 250 ", out int? limit), Is.True);
        Assert.That(limit, Is.EqualTo(250));

        Assert.That(SecuritySettingsViewModel.TryLimit("lots", out _), Is.False);
        Assert.That(SecuritySettingsViewModel.TryLimit("-5", out _), Is.False);
    }
}
