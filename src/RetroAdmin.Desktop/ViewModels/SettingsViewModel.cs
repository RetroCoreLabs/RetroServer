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

    /// <summary>The two Econet reply formats.</summary>
    public IReadOnlyList<string> WireProfiles { get; } = new[] { "LaterFS", "Classic1983" };

    // The on/off switches in tnfs-config.json and econet-config.json (2026-11-10).

    [ObservableProperty]
    private bool _tnfsEnabled = true;

    [ObservableProperty]
    private bool _econetEnabled = true;

    /// <summary>The server's version text the BBC sees; empty for none.</summary>
    [ObservableProperty]
    private string _econetVersionString = string.Empty;

    [ObservableProperty]
    private int _econetStationByte = 3;

    [ObservableProperty]
    private string _econetWireProfile = "LaterFS";

    /// <summary>Machines with another reply format, one per line (see <see cref="SettingsLines"/>).</summary>
    [ObservableProperty]
    private string _econetWireOverrides = string.Empty;

    /// <summary>Discs from their own storage, one per line (see <see cref="SettingsLines"/>).</summary>
    [ObservableProperty]
    private string _econetDiscs = string.Empty;

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

    // TNFS limits, in tnfs-config.json since 2026-11-10 (defaults are the reference daemon's).

    [ObservableProperty]
    private int _tnfsMaxTcpConnections = 4096;

    [ObservableProperty]
    private int _tnfsMaxSessions = 4096;

    [ObservableProperty]
    private int _tnfsMaxSessionsPerIp = 4096;

    [ObservableProperty]
    private int _tnfsSessionTimeoutSeconds = 600;

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
            TnfsEnabled = tnfs.Data.Enabled;
            TnfsRootDirectory = tnfs.Data.RootPath;
            UdpPort = tnfs.Data.UdpPort;
            TcpPort = tnfs.Data.TcpPort;
            EnableUdp = tnfs.Data.UdpEnabled;
            EnableTcp = tnfs.Data.TcpEnabled;
            TnfsMaxTcpConnections = tnfs.Data.MaxConnections;
            TnfsMaxSessions = tnfs.Data.MaxSessions;
            TnfsMaxSessionsPerIp = tnfs.Data.MaxSessionsPerIP;
            TnfsSessionTimeoutSeconds = tnfs.Data.SessionTimeoutSeconds;
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
            EconetEnabled = econet.Data.Enabled;
            EconetVersionString = econet.Data.VersionString ?? string.Empty;
            EconetStationByte = econet.Data.AunStationOctetIndex;
            EconetWireProfile = econet.Data.WireProfile;
            EconetWireOverrides = SettingsLines.FormatOverrides(econet.Data.WireProfileOverrides);
            EconetDiscs = SettingsLines.FormatDiscs(econet.Data.Discs);
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

        // Read the two list boxes before anything is sent, so a typing mistake saves nothing.
        List<EconetWireProfileOverrideDto> overrides = new List<EconetWireProfileOverrideDto>();
        List<EconetDiscDto> discs = new List<EconetDiscDto>();
        string? problem = SettingsLines.ParseOverrides(EconetWireOverrides, overrides) ?? SettingsLines.ParseDiscs(EconetDiscs, discs);
        if (problem != null)
        {
            StatusMessage = problem;
            return;
        }

        ApiResponse<TnfsSettingsDto>? tnfs = await _connection.Api.UpdateTnfsSettingsAsync(
            new UpdateTnfsSettingsRequest
            {
                Enabled = TnfsEnabled,
                RootPath = TnfsRootDirectory,
                UdpPort = UdpPort,
                TcpPort = TcpPort,
                UdpEnabled = EnableUdp,
                TcpEnabled = EnableTcp,
                MaxConnections = TnfsMaxTcpConnections,
                MaxSessions = TnfsMaxSessions,
                MaxSessionsPerIP = TnfsMaxSessionsPerIp,
                SessionTimeoutSeconds = TnfsSessionTimeoutSeconds
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
                UdpEnabled = EconetEnableUdp,
                Enabled = EconetEnabled,
                // Empty text is sent as empty (not null, which would mean "keep").
                VersionString = EconetVersionString,
                AunStationOctetIndex = (byte)EconetStationByte,
                WireProfile = EconetWireProfile,
                WireProfileOverrides = overrides,
                Discs = discs
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
