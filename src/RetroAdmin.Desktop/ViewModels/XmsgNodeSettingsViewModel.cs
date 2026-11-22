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
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>
/// RetroServer's own XMSG node: every key of xmsgnode-config.json. Read at start, so a change applies after
/// RetroServer restarts. The program token is never shown; the page only says whether one is set.
/// </summary>
public partial class XmsgNodeSettingsViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    /// <summary>The kinds of storage the file server can serve from (the same list the server checks against).</summary>
    public IReadOnlyList<string> FileServerProviders { get; } = new[] { "native", "tnfs" };

    /// <summary>The levels a program log line can be kept from (the same list the server checks against).</summary>
    public IReadOnlyList<string> LogLevels { get; } = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" };

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private int _systemNumber;

    [ObservableProperty]
    private int _peerSystemNumber;

    [ObservableProperty]
    private string _ethernet = string.Empty;

    [ObservableProperty]
    private string _programBindAddress = "127.0.0.1";

    [ObservableProperty]
    private int _programPort = 5011;

    [ObservableProperty]
    private string _tokenState = string.Empty;

    /// <summary>A new token typed in; left empty, the stored one is kept.</summary>
    [ObservableProperty]
    private string _newToken = string.Empty;

    /// <summary>Ticked: the stored token is removed on save.</summary>
    [ObservableProperty]
    private bool _removeToken;

    [ObservableProperty]
    private string _programLogMinimum = "Information";

    [ObservableProperty]
    private string _motd = string.Empty;

    /// <summary>Ticked: the node serves files as the COSMOS file server (*FA-SERVER).</summary>
    [ObservableProperty]
    private bool _fileServerEnabled;

    /// <summary>The kind of storage: native (a folder on the server machine) or tnfs.</summary>
    [ObservableProperty]
    private string _fileServerProvider = "native";

    /// <summary>Where the files are: a folder, or tnfs://host:port/path.</summary>
    [ObservableProperty]
    private string _fileServerUri = string.Empty;

    /// <summary>A mounts.json volume to serve instead; filled in, the kind and place above are not sent.</summary>
    [ObservableProperty]
    private string _fileServerMountRef = string.Empty;

    [ObservableProperty]
    private string _settingsMessage = string.Empty;

    /// <summary>Creates the page and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public XmsgNodeSettingsViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadSettingsAsync();
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

        ApiResponse<XmsgNodeSettingsDto>? response = await _connection.Api.GetXmsgNodeSettingsAsync();
        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not read the XMSG node settings from the server.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = string.Empty;
    }

    /// <summary>Saves the settings; the server checks them and writes xmsgnode-config.json.</summary>
    /// <returns>A task that completes when the server has answered.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            SettingsMessage = "Not connected.";
            return;
        }

        ApiResponse<XmsgNodeSettingsDto>? response = await _connection.Api.UpdateXmsgNodeSettingsAsync(new XmsgNodeSettingsDto
        {
            Enabled = Enabled,
            SystemNumber = SystemNumber,
            PeerSystemNumber = PeerSystemNumber,
            Ethernet = Ethernet,
            ProgramBindAddress = ProgramBindAddress,
            ProgramPort = ProgramPort,
            // The server's rule: null keeps the stored token, empty removes it.
            NewProgramToken = RemoveToken ? string.Empty : (NewToken.Length == 0 ? null : NewToken),
            ProgramLogMinimum = ProgramLogMinimum,
            Motd = string.IsNullOrWhiteSpace(Motd) ? null : Motd,
            FileServerEnabled = FileServerEnabled,
            // A mount reference replaces the inline kind and place; the server refuses both at once.
            FileServerMountRef = string.IsNullOrWhiteSpace(FileServerMountRef) ? null : FileServerMountRef.Trim(),
            FileServerProvider = string.IsNullOrWhiteSpace(FileServerMountRef) && !string.IsNullOrWhiteSpace(FileServerUri) ? FileServerProvider : null,
            FileServerUri = string.IsNullOrWhiteSpace(FileServerMountRef) && !string.IsNullOrWhiteSpace(FileServerUri) ? FileServerUri.Trim() : null,
        });

        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not save the XMSG node settings.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = "Saved. Restart RetroServer to use them.";
    }

    private void Fill(XmsgNodeSettingsDto s)
    {
        Enabled = s.Enabled;
        SystemNumber = s.SystemNumber;
        PeerSystemNumber = s.PeerSystemNumber;
        Ethernet = s.Ethernet;
        ProgramBindAddress = s.ProgramBindAddress;
        ProgramPort = s.ProgramPort;
        TokenState = s.ProgramTokenSet
            ? "A token is set: programs must give it to connect."
            : "No token is set: any program that can reach the port may connect.";
        NewToken = string.Empty;
        RemoveToken = false;
        ProgramLogMinimum = s.ProgramLogMinimum;
        Motd = s.Motd ?? string.Empty;
        FileServerEnabled = s.FileServerEnabled;
        FileServerProvider = s.FileServerProvider ?? "native";
        FileServerUri = s.FileServerUri ?? string.Empty;
        FileServerMountRef = s.FileServerMountRef ?? string.Empty;
    }
}
