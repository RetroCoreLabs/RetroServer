//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen 
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using RetroAdmin.Desktop.Services;
using RetroAdmin.Desktop.Services.ServerControl;
using SkiaSharp;
using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.ViewModels;

public partial class TnfsMonitorViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    /// <summary>Client-side sample history, the source of the chart series.</summary>
    /// <remarks>
    /// A management client never sees individual TNFS events, so it polls the server's totals and
    /// records them here. The history has to live on the client because the API reports current
    /// counters, not a time series.
    /// </remarks>
    private readonly TnfsStatisticsService _history = new();

    private readonly Timer _updateTimer;

    [ObservableProperty]
    private int _activeConnections = 0;

    [ObservableProperty]
    private long _totalRequests = 0;

    [ObservableProperty]
    private long _bytesTransferred = 0;

    [ObservableProperty]
    private string _selectedTimeRange = "Last 12 Hours";

    public ObservableCollection<ISeries> RequestsSeries { get; set; }
    public ObservableCollection<ISeries> BytesSeries { get; set; }
    public ObservableCollection<ISeries> FilesSeries { get; set; }
    public ObservableCollection<SessionInfo> ActiveSessions { get; } = new();

    /// <summary>Creates the view model and starts polling.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public TnfsMonitorViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _connection.Hub.TnfsStatsUpdated += OnTnfsStatsUpdate;

        // Initialize chart data
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

        // Poll every two seconds. The hub pushes the headline counters as well, but polling is
        // what keeps the charts moving when live updates are unavailable.
        _updateTimer = new Timer(_ => _ = UpdateStatisticsAsync(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(2));

        _history.StatisticsUpdated += OnStatisticsUpdated;
    }

    /// <summary>Polls the server for counters and sessions.</summary>
    /// <returns>A task that completes when the poll has been applied.</returns>
    private async Task UpdateStatisticsAsync()
    {
        if (!_connection.IsAuthenticated) return;

        ApiResponse<TnfsStatisticsResponse>? stats = await _connection.Api.GetTnfsStatisticsAsync();

        if (stats is { Success: true, Data: not null })
        {
            TnfsStatisticsResponse data = stats.Data;

            _history.RecordSample(data.TotalRequests, data.BytesTransferred, data.FilesTransferred);

            Dispatcher.UIThread.Post(() =>
            {
                ActiveConnections = data.ActiveConnections;
                TotalRequests = data.TotalRequests;
                BytesTransferred = data.BytesTransferred;
            });
        }

        ApiResponse<TnfsSessionsResponse>? sessions = await _connection.Api.GetTnfsSessionsAsync();

        if (sessions is not { Success: true, Data: not null }) return;

        List<TnfsSessionInfo> list = sessions.Data.Sessions;

        Dispatcher.UIThread.Post(() =>
        {
            // The in-process version left this list permanently empty: UpdateSessionsList was a
            // stub whose comment said SessionManager would need a GetAllSessions method. The
            // sessions endpoint already returns them, so the view finally has rows.
            ActiveSessions.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                TnfsSessionInfo session = list[i];

                ActiveSessions.Add(new SessionInfo
                {
                    SessionId = session.SessionId,
                    ClientIP = session.ClientIP,
                    Username = session.Username,
                    Transport = session.Transport,
                    LastActivity = session.LastActivity
                });
            }
        });
    }

    private void OnStatisticsUpdated(object? sender, StatisticsSnapshot snapshot)
    {
        // Update charts when new snapshot is available
        UpdateChartData();
    }

    private void OnTnfsStatsUpdate(TnfsStatsUpdate stats)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ActiveConnections = stats.ActiveConnections;
            TotalRequests = stats.TotalRequests;
            BytesTransferred = stats.TotalBytesTransferred;
        });
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        UpdateChartData();
    }

    [RelayCommand]
    private void UpdateChartData()
    {
        var duration = SelectedTimeRange switch
        {
            "Last 12 Hours" => TimeSpan.FromHours(12),
            "Last 1 Week" => TimeSpan.FromDays(7),
            "Last 1 Month" => TimeSpan.FromDays(30),
            _ => TimeSpan.FromHours(12)
        };

        List<StatisticsSnapshot> snapshots = _history.GetSnapshots(duration);

        if (snapshots.Count == 0)
        {
            return;
        }

        // Calculate deltas for requests
        var requestDeltas = new List<double>();
        var byteDeltas = new List<double>();
        var fileDeltas = new List<double>();

        for (int i = 1; i < snapshots.Count; i++)
        {
            requestDeltas.Add((double)(snapshots[i].TotalRequests - snapshots[i - 1].TotalRequests));
            byteDeltas.Add((double)(snapshots[i].TotalBytesTransferred - snapshots[i - 1].TotalBytesTransferred) / (1024 * 1024)); // Convert to MB
            fileDeltas.Add((double)(snapshots[i].TotalFilesTransferred - snapshots[i - 1].TotalFilesTransferred));
        }

        // Update chart series
        ((LineSeries<double>)RequestsSeries[0]).Values = requestDeltas;
        ((LineSeries<double>)BytesSeries[0]).Values = byteDeltas;
        ((LineSeries<double>)FilesSeries[0]).Values = fileDeltas;
    }
}

public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string ClientIP { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Transport { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; }
}
