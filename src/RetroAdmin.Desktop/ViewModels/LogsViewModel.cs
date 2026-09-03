//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;
    private readonly ObservableCollection<LogEntry> _allLogEntries = new();
    private bool _initialLoadDone;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _selectedLogLevel = "All";

    /// <summary>Creates the view model and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public LogsViewModel(ManagementConnection connection)
    {
        _connection = connection;

        // Live entries arrive over the hub. The old code ALSO subscribed to
        // RetroServerHost.LogReceived, the log of the server running inside this process, and
        // built a second ManagementApiClient of its own pointed at a hardcoded address.
        _connection.Hub.LogsAdded += OnNewLogsReceived;

        _ = LoadExistingLogsAsync();
    }

    private async Task LoadExistingLogsAsync()
    {
        if (_initialLoadDone)
            return;

        _initialLoadDone = true;

        // Backfill over REST, which needs a token but not the hub: the log view is useful even
        // when live updates are unavailable.
        if (_connection.IsAuthenticated)
        {
            ApiResponse<LogListResponse>? response = await _connection.Api.GetLogsAsync(200);

            if (response is { Success: true, Data.Entries: not null })
            {
                List<LogEntryDto> entries = response.Data.Entries;

                Dispatcher.UIThread.Post(() =>
                {
                    // Oldest first, because AddEntry inserts at the top.
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        LogEntryDto entry = entries[i];
                        AddEntry(entry.Level, entry.Message, entry.Timestamp);
                    }
                });

                return;
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_allLogEntries.Count == 0)
            {
                AddEntry("Info", $"Not connected to a server at {_connection.Settings.Address}.",
                    DateTime.Now);
            }
        });
    }

    private void OnNewLogsReceived(LogEntryDto[] newLogs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = 0; i < newLogs.Length; i++)
            {
                LogEntryDto entry = newLogs[i];
                AddEntry(entry.Level, entry.Message, entry.Timestamp);
            }
        });
    }

    private void AddEntry(string level, string message, DateTime timestamp)
    {
        _allLogEntries.Insert(0, new LogEntry
        {
            Timestamp = timestamp,
            Level = level,
            Message = message
        });

        while (_allLogEntries.Count > 1000)
        {
            _allLogEntries.RemoveAt(_allLogEntries.Count - 1);
        }

        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedLogLevelChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        LogEntries.Clear();

        for (int i = 0; i < _allLogEntries.Count; i++)
        {
            LogEntry entry = _allLogEntries[i];

            if (SelectedLogLevel != "All" && entry.Level != SelectedLogLevel)
                continue;

            if (!string.IsNullOrWhiteSpace(FilterText) &&
                !entry.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                continue;

            LogEntries.Add(entry);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _allLogEntries.Clear();
        LogEntries.Clear();
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
