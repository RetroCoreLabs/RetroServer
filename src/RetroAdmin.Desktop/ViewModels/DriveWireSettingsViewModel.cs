//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>
/// The DriveWire settings page: every key of drivewire-config.json. The server reads the file at start, so a
/// change applies after RetroServer restarts.
/// </summary>
public partial class DriveWireSettingsViewModel : ViewModelBase
{
    private const string ReadOnlyMark = ", read-only";

    private readonly ManagementConnection _connection;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private int _port = 65504;

    [ObservableProperty]
    private string _bindAddress = "127.0.0.1";

    [ObservableProperty]
    private int _transactionTimeoutMilliseconds = 250;

    /// <summary>The drives, one per line: <c>number = image file</c>, with <c>, read-only</c> at the end to refuse writes.</summary>
    [ObservableProperty]
    private string _drives = string.Empty;

    [ObservableProperty]
    private string _namedObjectDirectory = string.Empty;

    [ObservableProperty]
    private string _printDirectory = string.Empty;

    [ObservableProperty]
    private string _settingsMessage = string.Empty;

    /// <summary>Creates the page and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public DriveWireSettingsViewModel(ManagementConnection connection)
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

        ApiResponse<DriveWireSettingsDto>? response = await _connection.Api.GetDriveWireSettingsAsync();
        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not read the DriveWire settings from the server.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = string.Empty;
    }

    /// <summary>Saves the settings; the server checks them and writes drivewire-config.json.</summary>
    /// <returns>A task that completes when the server has answered.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            SettingsMessage = "Not connected.";
            return;
        }

        List<DriveWireDriveSettingDto> drives = new List<DriveWireDriveSettingDto>();
        string? error = ParseDrives(Drives, drives);
        if (error != null)
        {
            SettingsMessage = error;
            return;
        }

        ApiResponse<DriveWireSettingsDto>? response = await _connection.Api.UpdateDriveWireSettingsAsync(new DriveWireSettingsDto
        {
            Enabled = Enabled,
            Port = Port,
            BindAddress = BindAddress,
            TransactionTimeoutMilliseconds = TransactionTimeoutMilliseconds,
            Drives = drives,
            NamedObjectDirectory = string.IsNullOrWhiteSpace(NamedObjectDirectory) ? null : NamedObjectDirectory.Trim(),
            PrintDirectory = string.IsNullOrWhiteSpace(PrintDirectory) ? null : PrintDirectory.Trim(),
        });

        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not save the DriveWire settings.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = "Saved. Restart RetroServer to use them.";
    }

    private void Fill(DriveWireSettingsDto s)
    {
        Enabled = s.Enabled;
        Port = s.Port;
        BindAddress = s.BindAddress;
        TransactionTimeoutMilliseconds = s.TransactionTimeoutMilliseconds;
        Drives = FormatDrives(s.Drives);
        NamedObjectDirectory = s.NamedObjectDirectory ?? string.Empty;
        PrintDirectory = s.PrintDirectory ?? string.Empty;
    }

    /// <summary>Writes the drive list as the box shows it, one drive per line.</summary>
    /// <param name="drives">The drives.</param>
    /// <returns>The text.</returns>
    public static string FormatDrives(List<DriveWireDriveSettingDto> drives)
    {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < drives.Count; i++)
        {
            if (i > 0)
            {
                text.Append(Environment.NewLine);
            }

            text.Append(drives[i].Drive.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(drives[i].Path);
            if (drives[i].ReadOnly)
            {
                text.Append(ReadOnlyMark);
            }
        }

        return text.ToString();
    }

    /// <summary>Reads the drive box: <c>number = file</c> per line, <c>, read-only</c> optional; blank lines skipped.</summary>
    /// <param name="text">The box.</param>
    /// <param name="drives">Receives the drives.</param>
    /// <returns>Null when every line was understood, else what is wrong and on which line.</returns>
    /// <remarks>Range and duplicate checks are the server's; this only reads the shape of each line.</remarks>
    public static string? ParseDrives(string text, List<DriveWireDriveSettingDto> drives)
    {
        string[] lines = (text ?? string.Empty).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals < 0 || !int.TryParse(line.AsSpan(0, equals).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                return $"Drive line {i + 1} must look like \"0 = /disks/a.dsk\": {line}";
            }

            string path = line.Substring(equals + 1).Trim();
            bool readOnly = false;
            if (path.EndsWith(ReadOnlyMark, StringComparison.OrdinalIgnoreCase))
            {
                readOnly = true;
                path = path.Substring(0, path.Length - ReadOnlyMark.Length).Trim();
            }

            if (path.Length == 0)
            {
                return $"Drive line {i + 1} has no image file.";
            }

            drives.Add(new DriveWireDriveSettingDto { Drive = number, Path = path, ReadOnly = readOnly });
        }

        return null;
    }
}
