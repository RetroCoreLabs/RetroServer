//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using RetroIO.Management.Contracts;
// The view model has its own EconetSessionInfo row type; alias the wire one so the
// two names cannot be confused at a glance.
using WireSession = RetroIO.Management.Contracts.EconetSessionInfo;
using RetroAdmin.Desktop.Services;
using RetroAdmin.Desktop.Services.ServerControl;
using SkiaSharp;

namespace RetroAdmin.Desktop.ViewModels;

public partial class EconetMonitorViewModel : ViewModelBase
{
    private const int ActivityDisplayCount = 30;

    private readonly ManagementConnection _connection;

    /// <summary>Client-side sample history behind the chart series. See TnfsMonitorViewModel.</summary>
    private readonly EconetStatisticsService _history = new();

    private readonly Timer _updateTimer;

    [ObservableProperty]
    private int _activeSessions;

    [ObservableProperty]
    private long _totalRequests;

    [ObservableProperty]
    private long _bytesTransferred;

    [ObservableProperty]
    private string _serverAddress = "1.254";

    [ObservableProperty]
    private int _udpPort = 32768;

    [ObservableProperty]
    private string _selectedTimeRange = "Last 12 Hours";

    public ObservableCollection<ISeries> RequestsSeries { get; set; }
    public ObservableCollection<ISeries> BytesSeries { get; set; }
    public ObservableCollection<ISeries> FilesSeries { get; set; }
    public ObservableCollection<EconetSessionInfo> Sessions { get; } = new();
    public ObservableCollection<EconetCommandRow> RecentCommands { get; } = new();
    public ObservableCollection<EconetTransferRow> RecentTransfers { get; } = new();

    /// <summary>Creates the view model and starts polling.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public EconetMonitorViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadServerInfoAsync();

        RequestsSeries = new ObservableCollection<ISeries>
        {
            new LineSeries<double>
            {
                Values = new List<double>(),
                Name = "Requests",
                Stroke = new SolidColorPaint(SKColors.Lime) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 0
            }
        };

        BytesSeries = new ObservableCollection<ISeries>
        {
            new LineSeries<double>
            {
                Values = new List<double>(),
                Name = "MB Transferred",
                Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 0
            }
        };

        FilesSeries = new ObservableCollection<ISeries>
        {
            new LineSeries<double>
            {
                Values = new List<double>(),
                Name = "Files",
                Stroke = new SolidColorPaint(SKColors.DeepSkyBlue) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 0
            }
        };

        _history.StatisticsUpdated += OnStatisticsUpdated;

        // The activity log used to raise an event straight into this view model, because both
        // lived in one process. A client polls instead; every two seconds is well inside what a
        // human reads.
        _updateTimer = new Timer(_ => _ = UpdateStatisticsAsync(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    /// <summary>Reads the server's Econet address from its settings.</summary>
    /// <returns>A task that completes once the address is known.</returns>
    private async Task LoadServerInfoAsync()
    {
        // Was: deserialise econet-config.json off the local disk, which is only this machine's
        // copy and says nothing about a server running anywhere else.
        if (!_connection.IsAuthenticated) return;

        ApiResponse<EconetSettingsDto>? settings = await _connection.Api.GetEconetSettingsAsync();

        if (settings is not { Success: true, Data: not null }) return;

        Dispatcher.UIThread.Post(() =>
        {
            ServerAddress = $"{settings.Data.Net}.{settings.Data.Station}";
            UdpPort = settings.Data.UdpPort;
        });
    }

    /// <summary>Polls the server for counters, sessions and recent activity.</summary>
    /// <returns>A task that completes when the poll has been applied.</returns>
    private async Task UpdateStatisticsAsync()
    {
        if (!_connection.IsAuthenticated) return;

        ApiResponse<EconetStatisticsResponse>? stats = await _connection.Api.GetEconetStatisticsAsync();

        if (stats is { Success: true, Data: not null })
        {
            _history.RecordSample(stats.Data.TotalRequests, stats.Data.BytesTransferred, 0);
        }

        ApiResponse<EconetSessionsResponse>? sessions = await _connection.Api.GetEconetSessionsAsync();
        ApiResponse<EconetActivityResponse>? activity =
            await _connection.Api.GetEconetActivityAsync(ActivityDisplayCount);

        Dispatcher.UIThread.Post(() =>
        {
            if (stats is { Success: true, Data: not null })
            {
                ActiveSessions = stats.Data.ActiveSessions;
                TotalRequests = stats.Data.TotalRequests;
                BytesTransferred = stats.Data.BytesTransferred;
            }

            if (sessions is { Success: true, Data: not null })
            {
                Sessions.Clear();
                List<WireSession> list = sessions.Data.Sessions;

                for (int i = 0; i < list.Count; i++)
                {
                    WireSession session = list[i];

                    Sessions.Add(new EconetSessionInfo
                    {
                        Station = session.Station,
                        Username = session.Username,
                        CurrentDisc = session.CurrentDisc.ToString(),
                        IsAdmin = session.IsAdmin,
                        CurrentDirectory = session.CurrentDirectory
                    });
                }
            }

            if (activity is { Success: true, Data: not null })
            {
                UpdateActivityLog(activity.Data);
            }
        });
    }

    /// <summary>Fills the command and transfer grids from a polled activity response.</summary>
    /// <param name="activity">Activity as returned by the server.</param>
    private void UpdateActivityLog(EconetActivityResponse activity)
    {
        List<EconetCommandEntryDto> commands = activity.Commands;
        List<EconetFileTransferEntryDto> transfers = activity.Transfers;

        RecentCommands.Clear();
        for (int i = 0; i < commands.Count; i++)
        {
            EconetCommandEntryDto entry = commands[i];
            RecentCommands.Add(new EconetCommandRow
            {
                Time = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"),
                Station = entry.Station,
                User = entry.Username,
                Command = entry.Command,
                Detail = entry.Detail,
                Result = entry.Success ? entry.Result : $"✗ {entry.Result}",
            });
        }

        RecentTransfers.Clear();
        for (int i = 0; i < transfers.Count; i++)
        {
            EconetFileTransferEntryDto entry = transfers[i];
            RecentTransfers.Add(new EconetTransferRow
            {
                Time = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"),
                Station = entry.Station,
                User = entry.Username,
                Direction = entry.Direction,
                File = entry.FilePath,
                Bytes = entry.Bytes.ToString(),
                Status = entry.Status,
            });
        }
    }

    private void OnStatisticsUpdated(object? sender, StatisticsSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(UpdateChartData);
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        UpdateChartData();
    }

    [RelayCommand]
    private void UpdateChartData()
    {
        TimeSpan duration = SelectedTimeRange switch
        {
            "Last 12 Hours" => TimeSpan.FromHours(12),
            "Last 1 Week" => TimeSpan.FromDays(7),
            "Last 1 Month" => TimeSpan.FromDays(30),
            _ => TimeSpan.FromHours(12)
        };

        List<StatisticsSnapshot> snapshots = _history.GetSnapshots(duration);
        if (snapshots.Count == 0)
            return;

        List<double> requestDeltas = new List<double>();
        List<double> byteDeltas = new List<double>();
        List<double> fileDeltas = new List<double>();

        for (int i = 1; i < snapshots.Count; i++)
        {
            requestDeltas.Add((double)(snapshots[i].TotalRequests - snapshots[i - 1].TotalRequests));
            byteDeltas.Add((double)(snapshots[i].TotalBytesTransferred - snapshots[i - 1].TotalBytesTransferred) / (1024 * 1024));
            fileDeltas.Add((double)(snapshots[i].TotalFilesTransferred - snapshots[i - 1].TotalFilesTransferred));
        }

        ((LineSeries<double>)RequestsSeries[0]).Values = requestDeltas;
        ((LineSeries<double>)BytesSeries[0]).Values = byteDeltas;
        ((LineSeries<double>)FilesSeries[0]).Values = fileDeltas;
    }
}

public class EconetSessionInfo
{
    public string Station { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string CurrentDisc { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string CurrentDirectory { get; set; } = string.Empty;
}

public class EconetCommandRow
{
    public string Time { get; set; } = string.Empty;
    public string Station { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
}

public class EconetTransferRow
{
    public string Time { get; set; } = string.Empty;
    public string Station { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Bytes { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
