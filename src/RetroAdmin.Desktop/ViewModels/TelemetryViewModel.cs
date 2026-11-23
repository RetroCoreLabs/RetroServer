//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>
/// One metric reading as the page shows it.
/// </summary>
/// <param name="Name">The metric name, for example <c>retroio.dialup.io</c>.</param>
/// <param name="Labels">The labels of the series, as <c>key=value</c> text; empty for none.</param>
/// <param name="Value">The value now.</param>
/// <param name="Unit">The unit.</param>
/// <param name="Kind">Counter, UpDownCounter or Gauge.</param>
/// <param name="Description">What is counted.</param>
public sealed record TelemetryMetricRow(string Name, string Labels, long Value, string Unit, string Kind, string Description);

/// <summary>
/// One log record as the page shows it.
/// </summary>
/// <param name="Time">Local time of the record.</param>
/// <param name="Level">How serious it is.</param>
/// <param name="Message">The text.</param>
/// <param name="Attributes">The key-value attributes, as <c>key=value</c> text.</param>
public sealed record TelemetryLogRow(DateTime Time, string Level, string Message, string Attributes);

/// <summary>
/// The Telemetry page: every part of RetroServer that reports metrics and a log, and for the chosen
/// part its metrics and its log side by side.
/// </summary>
/// <remarks>
/// It reads <c>api/v1/telemetry</c> and polls every two seconds. The log is fetched by sequence
/// number ("everything after the last one I have"), so a poll carries only what is new.
/// </remarks>
public partial class TelemetryViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// The most log rows the page keeps.
    /// </summary>
    public const int MaxLogRows = 2000;

    private readonly ManagementConnection _connection;
    private readonly Timer _timer;
    private long _lastLog;
    private int _polling;

    [ObservableProperty]
    private string? _selectedSource;

    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>
    /// Initialises the page and starts polling.
    /// </summary>
    /// <param name="connection">The connection to the server.</param>
    public TelemetryViewModel(ManagementConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Gets the names of the parts that report.
    /// </summary>
    public ObservableCollection<string> Sources { get; } = new ObservableCollection<string>();

    /// <summary>
    /// Gets the chosen part's metric readings.
    /// </summary>
    public ObservableCollection<TelemetryMetricRow> Metrics { get; } = new ObservableCollection<TelemetryMetricRow>();

    /// <summary>
    /// Gets the chosen part's log records, oldest first.
    /// </summary>
    public ObservableCollection<TelemetryLogRow> LogRows { get; } = new ObservableCollection<TelemetryLogRow>();

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A new part is chosen: its log starts from the beginning of what the server keeps.
    /// </summary>
    /// <param name="value">The part's name.</param>
    partial void OnSelectedSourceChanged(string? value)
    {
        Interlocked.Exchange(ref _lastLog, 0);
        LogRows.Clear();
        Metrics.Clear();
        _ = PollAsync();
    }

    /// <summary>
    /// Fetches the list of parts, and the chosen part's metrics and new log records.
    /// </summary>
    /// <returns>Completes when done.</returns>
    private async Task PollAsync()
    {
        if (!_connection.IsAuthenticated || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            ApiResponse<List<TelemetrySourceDto>>? sources = await _connection.Api.GetTelemetrySourcesAsync().ConfigureAwait(false);
            if (sources is not { Success: true, Data: not null })
            {
                Dispatcher.UIThread.Post(() => Message = "The server did not answer the telemetry request.");
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                Message = sources.Data.Count == 0 ? "No part is switched on, so nothing reports." : string.Empty;
                SyncSources(sources.Data);
            });

            string? source = SelectedSource;
            if (source == null)
            {
                return;
            }

            ApiResponse<MetricsResponse>? metrics = await _connection.Api.GetTelemetryMetricsAsync(source).ConfigureAwait(false);
            if (metrics is { Success: true, Data: not null })
            {
                List<TelemetryMetricRow> rows = new List<TelemetryMetricRow>(metrics.Data.Metrics.Count);
                for (int i = 0; i < metrics.Data.Metrics.Count; i++)
                {
                    MetricDto m = metrics.Data.Metrics[i];
                    rows.Add(new TelemetryMetricRow(m.Name, Join(m.Tags), m.Value, m.Unit, m.Kind, m.Description));
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (SelectedSource != source)
                    {
                        return;
                    }

                    Metrics.Clear();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        Metrics.Add(rows[i]);
                    }
                });
            }

            long after = Interlocked.Read(ref _lastLog);
            ApiResponse<TelemetryLogResponse>? log = await _connection.Api.GetTelemetryLogAsync(source, after).ConfigureAwait(false);
            if (log is { Success: true, Data: not null } && log.Data.Records.Count > 0)
            {
                List<TelemetryLogRecordDto> records = log.Data.Records;
                Interlocked.Exchange(ref _lastLog, records[records.Count - 1].Sequence);
                Dispatcher.UIThread.Post(() =>
                {
                    if (SelectedSource != source)
                    {
                        return;
                    }

                    for (int i = 0; i < records.Count; i++)
                    {
                        TelemetryLogRecordDto r = records[i];
                        LogRows.Add(new TelemetryLogRow(r.TimestampUtc.ToLocalTime(), r.Level, r.Message, Join(r.Attributes)));
                    }

                    while (LogRows.Count > MaxLogRows)
                    {
                        LogRows.RemoveAt(0);
                    }
                });
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException)
        {
            Dispatcher.UIThread.Post(() => Message = "Lost the server: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    /// <summary>
    /// Makes the part list match the server's, keeping the chosen part when it is still there.
    /// </summary>
    /// <param name="sources">The parts the server lists.</param>
    private void SyncSources(List<TelemetrySourceDto> sources)
    {
        bool same = sources.Count == Sources.Count;
        for (int i = 0; same && i < sources.Count; i++)
        {
            same = sources[i].Name == Sources[i];
        }

        if (!same)
        {
            string? keep = SelectedSource;
            Sources.Clear();
            for (int i = 0; i < sources.Count; i++)
            {
                Sources.Add(sources[i].Name);
            }

            SelectedSource = keep != null && Sources.Contains(keep) ? keep : null;
        }

        if (SelectedSource == null && Sources.Count > 0)
        {
            SelectedSource = Sources[0];
        }
    }

    /// <summary>
    /// Writes key-value pairs as <c>key=value key=value</c>.
    /// </summary>
    /// <param name="pairs">The pairs.</param>
    /// <returns>The text; empty for none.</returns>
    public static string Join(Dictionary<string, string> pairs)
    {
        if (pairs.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder text = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in pairs)
        {
            // Dictionary has no indexed walk; foreach over it does not allocate (struct enumerator).
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(pair.Key).Append('=').Append(pair.Value);
        }

        return text.ToString();
    }
}
