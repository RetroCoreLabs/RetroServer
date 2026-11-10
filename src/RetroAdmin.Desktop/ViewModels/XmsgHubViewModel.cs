//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>
/// The XMSG Ethernet hub page: its settings, its state and members, its log lines and the frames it carried.
/// </summary>
/// <remarks>
/// <para>
/// The hub is the virtual Ethernet segment ND machines join over TCP. Every frame one machine sends reaches all the
/// others; the server keeps each frame once, as it went from its sender to the hub.
/// </para>
/// <para>
/// Like the Econet page, this one polls (every two seconds). The log and the frames are fetched by number - "everything
/// after the last one I have" - so a poll only carries what is new.
/// </para>
/// </remarks>
public partial class XmsgHubViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// The most frame rows the page keeps; older ones are dropped from the top.
    /// </summary>
    public const int MaxFrameRows = 2000;

    /// <summary>
    /// The most log rows the page keeps.
    /// </summary>
    public const int MaxLogRows = 2000;

    private readonly ManagementConnection _connection;
    private readonly Timer _timer;
    private long _lastLog;
    private long _lastFrame;
    private int _polling;

    // ---- settings (saved in xmsghub-config.json on the server) ----

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _port = "5010";

    [ObservableProperty]
    private string _bindAddress = "127.0.0.1";

    [ObservableProperty]
    private string _uplink = string.Empty;

    [ObservableProperty]
    private string _captureFile = string.Empty;

    [ObservableProperty]
    private string _frameBufferSize = "4096";

    [ObservableProperty]
    private string _logBufferSize = "1000";

    [ObservableProperty]
    private string _statusIntervalSeconds = "10";

    [ObservableProperty]
    private string _settingsMessage = string.Empty;

    // ---- state ----

    [ObservableProperty]
    private string _state = "Not hosted";

    [ObservableProperty]
    private string _listening = string.Empty;

    [ObservableProperty]
    private string _failureReason = string.Empty;

    [ObservableProperty]
    private string _uptime = string.Empty;

    [ObservableProperty]
    private string _counters = string.Empty;

    [ObservableProperty]
    private string _joinHint = string.Empty;

    // ---- frame list ----

    [ObservableProperty]
    private string _systemFilter = string.Empty;

    [ObservableProperty]
    private bool _paused;

    [ObservableProperty]
    private XmsgHubFrameRow? _selectedFrame;

    [ObservableProperty]
    private string _frameMessage = string.Empty;

    /// <summary>
    /// Gets the members joined now.
    /// </summary>
    public ObservableCollection<XmsgHubMemberInfo> Members { get; } = new();

    /// <summary>
    /// Gets the hub's log lines, oldest first.
    /// </summary>
    public ObservableCollection<XmsgHubLogRow> LogLines { get; } = new();

    /// <summary>
    /// Gets the frames, oldest first.
    /// </summary>
    public ObservableCollection<XmsgHubFrameRow> Frames { get; } = new();

    /// <summary>
    /// Initialises the page and starts polling.
    /// </summary>
    /// <param name="connection">
    /// The connection to the server.
    /// </param>
    public XmsgHubViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadSettingsAsync();
        _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Stops polling.
    /// </summary>
    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the pcap file of the frames the hub still keeps (filtered by the system box), for the view to save.
    /// </summary>
    /// <returns>
    /// The file's bytes, or null when it could not be fetched.
    /// </returns>
    public async Task<byte[]?> DownloadPcapAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            return null;
        }

        return await _connection.Api.DownloadXmsgHubPcapAsync(ParseSystem()).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports what happened to a pcap save, under the frame list.
    /// </summary>
    /// <param name="message">
    /// The text to show.
    /// </param>
    public void ReportFrameMessage(string message)
    {
        Dispatcher.UIThread.Post(() => FrameMessage = message);
    }

    /// <summary>
    /// Reloads the settings from the server, dropping unsaved edits.
    /// </summary>
    /// <returns>
    /// Completes when loaded.
    /// </returns>
    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            return;
        }

        ApiResponse<XmsgHubSettingsDto>? response = await _connection.Api.GetXmsgHubSettingsAsync().ConfigureAwait(false);
        if (response is not { Success: true, Data: not null })
        {
            Dispatcher.UIThread.Post(() => SettingsMessage = "Could not read the hub settings from the server.");
            return;
        }

        XmsgHubSettingsDto s = response.Data;
        Dispatcher.UIThread.Post(() =>
        {
            Enabled = s.Enabled;
            Port = s.Port.ToString(CultureInfo.InvariantCulture);
            BindAddress = s.BindAddress;
            Uplink = s.Uplink;
            CaptureFile = s.CaptureFile;
            FrameBufferSize = s.FrameBufferSize.ToString(CultureInfo.InvariantCulture);
            LogBufferSize = s.LogBufferSize.ToString(CultureInfo.InvariantCulture);
            StatusIntervalSeconds = s.StatusIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            SettingsMessage = s.RestartRequired ? "The saved settings are not the ones running. Restart RetroServer to use them." : string.Empty;
        });
    }

    /// <summary>
    /// Saves the settings on the server.
    /// </summary>
    /// <returns>
    /// Completes when saved.
    /// </returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!TryNumber(Port, "Port", out int port)
            || !TryNumber(FrameBufferSize, "Frames kept", out int frames)
            || !TryNumber(LogBufferSize, "Log lines kept", out int lines)
            || !TryNumber(StatusIntervalSeconds, "Status interval", out int interval))
        {
            return;
        }

        XmsgHubSettingsDto request = new XmsgHubSettingsDto
        {
            Enabled = Enabled,
            Port = port,
            BindAddress = BindAddress,
            Uplink = Uplink,
            CaptureFile = CaptureFile,
            FrameBufferSize = frames,
            LogBufferSize = lines,
            StatusIntervalSeconds = interval,
        };

        ApiResponse<XmsgHubSettingsDto>? response = await _connection.Api.UpdateXmsgHubSettingsAsync(request).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            if (response is { Success: true, Data: not null })
            {
                SettingsMessage = response.Data.RestartRequired
                    ? "Saved. Restart RetroServer to use the new settings."
                    : "Saved.";
            }
            else
            {
                SettingsMessage = response?.Error?.Message ?? "The server did not save the settings.";
            }
        });
    }

    /// <summary>
    /// Empties the frame list on this page. The server's buffer is not touched.
    /// </summary>
    [RelayCommand]
    private void ClearFrames()
    {
        Frames.Clear();
        FrameMessage = string.Empty;
    }

    /// <summary>
    /// Starts the frame list again from what the server still keeps, with the current system filter.
    /// </summary>
    [RelayCommand]
    private void ApplyFilter()
    {
        Frames.Clear();
        _lastFrame = 0;
    }

    /// <summary>
    /// Fetches the state, members, new log lines and new frames.
    /// </summary>
    /// <returns>
    /// Completes when done.
    /// </returns>
    private async Task PollAsync()
    {
        if (!_connection.IsAuthenticated || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            ApiResponse<XmsgHubStatusResponse>? status = await _connection.Api.GetXmsgHubStatusAsync().ConfigureAwait(false);
            if (status is { Success: true, Data: not null })
            {
                ShowStatus(status.Data);
                if (!status.Data.IsHosted)
                {
                    return;
                }
            }

            ApiResponse<XmsgHubMembersResponse>? members = await _connection.Api.GetXmsgHubMembersAsync().ConfigureAwait(false);
            if (members is { Success: true, Data: not null })
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Members.Clear();
                    for (int i = 0; i < members.Data.Members.Count; i++)
                    {
                        Members.Add(members.Data.Members[i]);
                    }
                });
            }

            ApiResponse<XmsgHubLogResponse>? log = await _connection.Api.GetXmsgHubLogAsync(_lastLog).ConfigureAwait(false);
            if (log is { Success: true, Data: not null } && log.Data.Lines.Count > 0)
            {
                _lastLog = log.Data.Lines[log.Data.Lines.Count - 1].Sequence;
                Dispatcher.UIThread.Post(() =>
                {
                    for (int i = 0; i < log.Data.Lines.Count; i++)
                    {
                        XmsgHubLogLine line = log.Data.Lines[i];
                        LogLines.Add(new XmsgHubLogRow(line.TimeUtc.ToLocalTime(), line.Message));
                    }

                    while (LogLines.Count > MaxLogRows)
                    {
                        LogLines.RemoveAt(0);
                    }
                });
            }

            if (!Paused)
            {
                await PollFramesAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    /// <summary>
    /// Fetches the frames after the last one shown.
    /// </summary>
    /// <returns>
    /// Completes when done.
    /// </returns>
    private async Task PollFramesAsync()
    {
        ApiResponse<XmsgHubFramesResponse>? frames =
            await _connection.Api.GetXmsgHubFramesAsync(_lastFrame, 500, ParseSystem()).ConfigureAwait(false);
        if (frames is not { Success: true, Data: not null })
        {
            return;
        }

        XmsgHubFramesResponse data = frames.Data;
        bool gap = data.Gap && _lastFrame > 0;

        // With a filter, the newest frame may not match it; move on to the newest the hub has so the next poll does not
        // fetch the same non-matching frames again.
        long next = data.Frames.Count > 0 ? data.Frames[data.Frames.Count - 1].Sequence : _lastFrame;
        if (data.Frames.Count < 500 && data.LastFrame > next)
        {
            next = data.LastFrame;
        }

        _lastFrame = next;
        if (data.Frames.Count == 0 && !gap)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (gap)
            {
                FrameMessage = "Some frames left the server's buffer before this page fetched them. Turn on a capture file to keep every frame.";
            }

            for (int i = 0; i < data.Frames.Count; i++)
            {
                Frames.Add(new XmsgHubFrameRow(data.Frames[i]));
            }

            while (Frames.Count > MaxFrameRows)
            {
                Frames.RemoveAt(0);
            }
        });
    }

    /// <summary>
    /// Shows the hub's state and counters.
    /// </summary>
    /// <param name="s">
    /// The status.
    /// </param>
    private void ShowStatus(XmsgHubStatusResponse s)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!s.IsHosted)
            {
                State = "Not hosted";
                Listening = string.Empty;
                FailureReason = "The hub is switched off on this server. Switch it on below, save, and restart RetroServer.";
                Uptime = string.Empty;
                Counters = string.Empty;
                JoinHint = string.Empty;
                return;
            }

            State = s.State;
            Listening = s.Listening;
            FailureReason = s.FailureReason;
            Uptime = s.Uptime;
            Counters =
                $"members {s.Members} (machines {s.Machines})   in {s.FramesIn}   forwarded {s.FramesForwarded}   "
                + $"dropped: slow {s.FramesDroppedSlow}, loop {s.FramesDroppedLoop}, hop count {s.FramesDroppedTtl}"
                + (string.IsNullOrEmpty(s.CaptureFile) ? string.Empty : $"   captured {s.FramesCaptured} to {s.CaptureFile}");
            JoinHint = string.IsNullOrEmpty(s.Listening)
                ? string.Empty
                : $"RetroCore machines join with:  device add ETH 0 --net=tcp:HOST:{PortOf(s.Listening)}";
        });
    }

    private int ParseSystem()
    {
        return int.TryParse(SystemFilter, NumberStyles.Integer, CultureInfo.InvariantCulture, out int system) && system > 0
            ? system
            : 0;
    }

    private bool TryNumber(string text, string label, out int value)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        SettingsMessage = $"{label} must be a whole number.";
        return false;
    }

    private static string PortOf(string endPoint)
    {
        int colon = endPoint.LastIndexOf(':');
        return colon < 0 ? endPoint : endPoint.Substring(colon + 1);
    }
}

/// <summary>
/// One hub log line on the page.
/// </summary>
/// <param name="Time">
/// When it was logged, local time.
/// </param>
/// <param name="Message">
/// The text.
/// </param>
public sealed record XmsgHubLogRow(DateTime Time, string Message);

/// <summary>
/// One frame on the page.
/// </summary>
public sealed class XmsgHubFrameRow
{
    /// <summary>
    /// Initialises a row from the server's frame.
    /// </summary>
    /// <param name="frame">
    /// The frame.
    /// </param>
    public XmsgHubFrameRow(XmsgHubFrameInfo frame)
    {
        Sequence = frame.Sequence;
        Time = frame.TimeUtc.ToLocalTime();
        Member = frame.Member;
        Route = frame.IsNdFrame ? $"{frame.SourceSystem} -> {frame.DestinationSystem}" : "not an ND frame";
        Length = frame.Length;
        Outcome = frame.Outcome == "Forwarded" ? $"sent to {frame.CopiesSent}" + (frame.CopiesDropped > 0 ? $", {frame.CopiesDropped} lost" : string.Empty) : frame.Outcome;
        Hex = frame.Hex;
        HexDump = FormatHex(frame.Hex);
    }

    /// <summary>
    /// Gets the frame's number on the server.
    /// </summary>
    public long Sequence { get; }

    /// <summary>
    /// Gets when it arrived, local time.
    /// </summary>
    public DateTime Time { get; }

    /// <summary>
    /// Gets the member it came from.
    /// </summary>
    public string Member { get; }

    /// <summary>
    /// Gets "source -> destination" system numbers.
    /// </summary>
    public string Route { get; }

    /// <summary>
    /// Gets the length in bytes.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets what the hub did with it.
    /// </summary>
    public string Outcome { get; }

    /// <summary>
    /// Gets the bytes as one hexadecimal string.
    /// </summary>
    public string Hex { get; }

    /// <summary>
    /// Gets the bytes as a dump of 16 per line with offsets, for the detail box.
    /// </summary>
    public string HexDump { get; }

    private static string FormatHex(string hex)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder(hex.Length * 2);
        int bytes = hex.Length / 2;
        for (int offset = 0; offset < bytes; offset += 16)
        {
            text.Append(offset.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");
            int end = Math.Min(bytes, offset + 16);
            for (int i = offset; i < end; i++)
            {
                text.Append(hex, i * 2, 2).Append(' ');
            }

            text.Append('\n');
        }

        return text.ToString();
    }
}
