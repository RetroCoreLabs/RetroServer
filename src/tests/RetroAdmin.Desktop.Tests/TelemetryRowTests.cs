//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Collections.Generic;
using RetroAdmin.Desktop.ViewModels;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// How the Telemetry page writes a metric's labels and a log record's attributes.
/// </summary>
[TestFixture]
public class TelemetryRowTests
{
    [Test]
    public void LabelsAndAttributes_AreWrittenAsKeyEqualsValue_SeparatedBySpaces()
    {
        Dictionary<string, string> pairs = new Dictionary<string, string>
        {
            ["direction"] = "in",
            ["reason"] = "lines_full",
        };

        Assert.That(TelemetryViewModel.Join(pairs), Is.EqualTo("direction=in reason=lines_full"));
    }

    [Test]
    public void NoLabels_IsEmptyText()
    {
        Assert.That(TelemetryViewModel.Join(new Dictionary<string, string>()), Is.Empty);
    }
}
