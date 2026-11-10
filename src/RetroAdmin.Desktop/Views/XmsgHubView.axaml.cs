//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RetroAdmin.Desktop.ViewModels;

namespace RetroAdmin.Desktop.Views;

/// <summary>
/// The XMSG Ethernet hub page.
/// </summary>
public partial class XmsgHubView : UserControl
{
    /// <summary>
    /// Initialises the page.
    /// </summary>
    public XmsgHubView()
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

    /// <summary>
    /// Fetches the frames as a pcap file from the server and asks where to save it.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The click.
    /// </param>
    /// <remarks>
    /// The save dialog needs the window, which only the view has; the view model fetches the bytes.
    /// </remarks>
    private async void OnSavePcap(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not XmsgHubViewModel vm)
        {
            return;
        }

        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top == null)
        {
            return;
        }

        byte[]? pcap = await vm.DownloadPcapAsync();
        if (pcap == null)
        {
            vm.ReportFrameMessage("Could not fetch the frames from the server. Is the hub hosted and running?");
            return;
        }

        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the hub's frames",
            SuggestedFileName = $"xmsghub-{DateTime.Now:yyyyMMdd-HHmmss}.pcap",
            DefaultExtension = "pcap",
            FileTypeChoices = new[] { new FilePickerFileType("pcap capture") { Patterns = new[] { "*.pcap" } } },
        });
        if (file == null)
        {
            return;
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            await stream.WriteAsync(pcap);
            vm.ReportFrameMessage($"Saved {pcap.Length} bytes to {file.Name}. Open it in Wireshark.");
        }
        catch (IOException ex)
        {
            vm.ReportFrameMessage($"Could not save the file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            vm.ReportFrameMessage($"Could not save the file: {ex.Message}");
        }
    }
}
