//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>
/// RetroServer's start-up file (appsettings.json): log levels, file storage, Azure and the management addresses.
/// Editable here since 2026-11-10 (Ronny's decision); the data folder is shown but set outside any file.
/// </summary>
public partial class StartupSettingsViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    // What the management addresses were when loaded, to warn before a change is saved.
    private string _loadedManagementUrls = string.Empty;
    private bool _warned;

    /// <summary>Where files are stored.</summary>
    public IReadOnlyList<string> Backends { get; } = new[] { "Local", "Azure" };

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _dataFolder = string.Empty;

    /// <summary>One log category per line: <c>Microsoft = Warning</c>.</summary>
    [ObservableProperty]
    private string _logLevels = string.Empty;

    [ObservableProperty]
    private string _fileSystemBackend = "Local";

    [ObservableProperty]
    private string _connectionStringState = string.Empty;

    [ObservableProperty]
    private string _newConnectionString = string.Empty;

    [ObservableProperty]
    private bool _removeConnectionString;

    [ObservableProperty]
    private string _azureContainerName = string.Empty;

    [ObservableProperty]
    private bool _azureUseManagedIdentity;

    [ObservableProperty]
    private string _azureStorageAccountUri = string.Empty;

    [ObservableProperty]
    private bool _azureCreateContainerIfNotExists = true;

    [ObservableProperty]
    private string _managementUrls = string.Empty;

    [ObservableProperty]
    private string _settingsMessage = string.Empty;

    /// <summary>Creates the page and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public StartupSettingsViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadSettingsAsync();
    }

    partial void OnManagementUrlsChanged(string value)
    {
        // A new change needs a new warning.
        _warned = false;
    }

    /// <summary>Reads the settings from the server.</summary>
    /// <returns>A task that completes when the fields are filled.</returns>
    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            SettingsMessage = "Not connected.";
            return;
        }

        ApiResponse<StartupSettingsDto>? response = await _connection.Api.GetStartupSettingsAsync();
        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not read the start-up settings from the server.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = string.Empty;
    }

    /// <summary>Saves the settings into appsettings.json. A changed management address is saved only on the second press,
    /// after a warning.</summary>
    /// <returns>A task that completes when the server has answered.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            SettingsMessage = "Not connected.";
            return;
        }

        if (ManagementUrls.Trim() != _loadedManagementUrls.Trim() && !_warned)
        {
            _warned = true;
            SettingsMessage = "The management address is changed. After a restart RetroServer listens ONLY there: if it is wrong, "
                + "RetroAdmin cannot reach the server, and the file must be fixed by hand. Press Save again to save it.";
            return;
        }

        Dictionary<string, string> levels = new Dictionary<string, string>();
        string? problem = SettingsLines.ParseLogLevels(LogLevels, levels);
        if (problem != null)
        {
            SettingsMessage = problem;
            return;
        }

        ApiResponse<StartupSettingsDto>? response = await _connection.Api.UpdateStartupSettingsAsync(new StartupSettingsDto
        {
            LogLevels = levels,
            FileSystemBackend = FileSystemBackend,
            // The server's rule: null keeps the stored string, empty removes it.
            NewAzureConnectionString = RemoveConnectionString ? string.Empty : (NewConnectionString.Length == 0 ? null : NewConnectionString),
            AzureContainerName = AzureContainerName,
            AzureUseManagedIdentity = AzureUseManagedIdentity,
            AzureStorageAccountUri = AzureStorageAccountUri,
            AzureCreateContainerIfNotExists = AzureCreateContainerIfNotExists,
            ManagementUrls = ManagementUrls,
        });

        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not save the start-up settings.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = "Saved. Restart RetroServer to use them (log levels are picked up while it runs).";
    }

    private void Fill(StartupSettingsDto s)
    {
        FilePath = s.FilePath;
        DataFolder = s.DataFolder;
        LogLevels = SettingsLines.FormatLogLevels(s.LogLevels);
        FileSystemBackend = s.FileSystemBackend;
        ConnectionStringState = s.AzureConnectionStringSet
            ? "A connection string is stored (never shown)."
            : "No connection string is stored.";
        NewConnectionString = string.Empty;
        RemoveConnectionString = false;
        AzureContainerName = s.AzureContainerName;
        AzureUseManagedIdentity = s.AzureUseManagedIdentity;
        AzureStorageAccountUri = s.AzureStorageAccountUri;
        AzureCreateContainerIfNotExists = s.AzureCreateContainerIfNotExists;
        ManagementUrls = s.ManagementUrls;
        _loadedManagementUrls = s.ManagementUrls;
        _warned = false;
    }
}
