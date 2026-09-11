//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>Reads and writes the server's TNFS and Econet settings.</summary>
/// <remarks>
/// This used to read and write tnfs-config.json and econet-config.json directly on the local
/// disk, which only worked because the server was this process. Worse, it wrote the files behind
/// a running server's back, so the settings on screen and the settings in force could disagree
/// until someone restarted it. The settings endpoints are the server's own view of its
/// configuration, and they work whichever machine it is on.
/// </remarks>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    [ObservableProperty]
    private string _tnfsRootDirectory = string.Empty;

    [ObservableProperty]
    private int _udpPort = 16384;

    [ObservableProperty]
    private int _tcpPort = 16384;

    [ObservableProperty]
    private bool _enableUdp = true;

    [ObservableProperty]
    private bool _enableTcp = true;

    [ObservableProperty]
    private string _econetRootDirectory = string.Empty;

    [ObservableProperty]
    private string _econetDiscName = "RetroFS";

    [ObservableProperty]
    private int _econetUdpPort = 32768;

    [ObservableProperty]
    private int _econetNet = 1;

    [ObservableProperty]
    private int _econetStation = 254;

    [ObservableProperty]
    private bool _econetEnableUdp = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True when this connection mode can restart the server.</summary>
    /// <remarks>Bound by the view so the restart button disables against a remote server.</remarks>
    public bool CanRestartServer => _connection.Lifecycle.SupportsLifecycleControl;

    /// <summary>Creates the view model and loads the current settings.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public SettingsViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadSettingsAsync();
    }

    /// <summary>Reads both settings groups from the server.</summary>
    /// <returns>A task that completes once both have been read.</returns>
    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            StatusMessage = "Not connected.";
            return;
        }

        ApiResponse<TnfsSettingsDto>? tnfs = await _connection.Api.GetTnfsSettingsAsync();

        if (tnfs is { Success: true, Data: not null })
        {
            TnfsRootDirectory = tnfs.Data.RootPath;
            UdpPort = tnfs.Data.UdpPort;
            TcpPort = tnfs.Data.TcpPort;
            EnableUdp = tnfs.Data.UdpEnabled;
            EnableTcp = tnfs.Data.TcpEnabled;
        }
        else
        {
            StatusMessage = tnfs?.Error?.Message ?? "Could not read TNFS settings.";
        }

        ApiResponse<EconetSettingsDto>? econet = await _connection.Api.GetEconetSettingsAsync();

        if (econet is { Success: true, Data: not null })
        {
            EconetRootDirectory = econet.Data.RootPath;
            EconetDiscName = econet.Data.DiscName;
            EconetUdpPort = econet.Data.UdpPort;
            EconetNet = econet.Data.Net;
            EconetStation = econet.Data.Station;
            EconetEnableUdp = econet.Data.UdpEnabled;
        }
        else
        {
            StatusMessage = econet?.Error?.Message ?? "Could not read Econet settings.";
        }
    }

    /// <summary>Writes both settings groups back to the server.</summary>
    /// <returns>A task that completes once both have been written.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            StatusMessage = "Not connected.";
            return;
        }

        ApiResponse<TnfsSettingsDto>? tnfs = await _connection.Api.UpdateTnfsSettingsAsync(
            new UpdateTnfsSettingsRequest
            {
                RootPath = TnfsRootDirectory,
                UdpPort = UdpPort,
                TcpPort = TcpPort,
                UdpEnabled = EnableUdp,
                TcpEnabled = EnableTcp
            });

        if (tnfs is not { Success: true })
        {
            StatusMessage = tnfs?.Error?.Message ?? "Could not save TNFS settings.";
            return;
        }

        ApiResponse<EconetSettingsDto>? econet = await _connection.Api.UpdateEconetSettingsAsync(
            new UpdateEconetSettingsRequest
            {
                RootPath = EconetRootDirectory,
                DiscName = EconetDiscName,
                UdpPort = EconetUdpPort,
                Net = (byte)EconetNet,
                Station = (byte)EconetStation,
                UdpEnabled = EconetEnableUdp
            });

        if (econet is not { Success: true })
        {
            StatusMessage = econet?.Error?.Message ?? "Could not save Econet settings.";
            return;
        }

        StatusMessage = CanRestartServer
            ? "Settings saved. Restart the server to apply them."
            : "Settings saved. They apply when the server is next restarted.";
    }

    /// <summary>Stops and starts the server so new settings take effect.</summary>
    /// <returns>A task that completes once the restart has been attempted.</returns>
    [RelayCommand]
    private async Task RestartServer()
    {
        if (!CanRestartServer)
        {
            // A remote server is not ours to restart. The button should already be disabled;
            // this is the guard behind it.
            StatusMessage = _connection.Lifecycle.Description + " cannot be restarted from here.";
            return;
        }

        StatusMessage = "Stopping server...";

        ServerControlResult stop = await _connection.Lifecycle.StopAsync();

        if (!stop.Succeeded)
        {
            StatusMessage = stop.Message;
            return;
        }

        StatusMessage = "Starting server...";

        ServerControlResult start = await _connection.Lifecycle.StartAsync();
        StatusMessage = start.Message;
    }
}
