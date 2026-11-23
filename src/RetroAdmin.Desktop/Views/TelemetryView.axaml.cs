//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using Avalonia.Controls;

namespace RetroAdmin.Desktop.Views;

/// <summary>
/// The Telemetry page: every part's metrics and log.
/// </summary>
public partial class TelemetryView : UserControl
{
    /// <summary>
    /// Initialises the page.
    /// </summary>
    public TelemetryView()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Leaving the page stops its polling; the main window makes a new page each visit.
        (DataContext as IDisposable)?.Dispose();
        base.OnDetachedFromVisualTree(e);
    }
}
