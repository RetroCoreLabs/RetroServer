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
/// The mounted volumes list (mounts.json), which Econet discs name with <c>mount:ID</c>. Read at start.
/// </summary>
public partial class MountsViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    /// <summary>One volume per line (see <see cref="SettingsLines"/>).</summary>
    [ObservableProperty]
    private string _mounts = string.Empty;

    [ObservableProperty]
    private string _settingsMessage = string.Empty;

    /// <summary>Creates the page and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public MountsViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadSettingsAsync();
    }

    /// <summary>Reads the list from the server.</summary>
    /// <returns>A task that completes when the box is filled.</returns>
    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            SettingsMessage = "Not connected.";
            return;
        }

        ApiResponse<List<MountDto>>? response = await _connection.Api.GetMountsAsync();
        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not read the mounted volumes from the server.";
            return;
        }

        Mounts = SettingsLines.FormatMounts(response.Data);
        SettingsMessage = string.Empty;
    }

    /// <summary>Saves the whole list; the server checks it and writes mounts.json.</summary>
    /// <returns>A task that completes when the server has answered.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            SettingsMessage = "Not connected.";
            return;
        }

        List<MountDto> mounts = new List<MountDto>();
        string? problem = SettingsLines.ParseMounts(Mounts, mounts);
        if (problem != null)
        {
            SettingsMessage = problem;
            return;
        }

        ApiResponse<List<MountDto>>? response = await _connection.Api.UpdateMountsAsync(mounts);
        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not save the mounted volumes.";
            return;
        }

        Mounts = SettingsLines.FormatMounts(response.Data);
        SettingsMessage = "Saved. Restart RetroServer to use them.";
    }
}
