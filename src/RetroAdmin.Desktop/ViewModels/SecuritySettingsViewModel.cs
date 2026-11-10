//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>
/// The traffic limits and audit log page: every key of security-config.json. Read at start, so a change applies
/// after RetroServer restarts. A limit box left empty means no limit.
/// </summary>
public partial class SecuritySettingsViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    // The six limits are text so an empty box can mean "no limit".

    [ObservableProperty]
    private string _perUserRequestsPerSecond = string.Empty;

    [ObservableProperty]
    private string _perUserMegabytesPerHour = string.Empty;

    [ObservableProperty]
    private bool _skipLimitsForAdmins = true;

    [ObservableProperty]
    private string _perIpRequestsPerSecond = string.Empty;

    [ObservableProperty]
    private string _perIpMegabytesPerHour = string.Empty;

    [ObservableProperty]
    private string _serverRequestsPerSecond = string.Empty;

    [ObservableProperty]
    private string _serverMegabytesPerHour = string.Empty;

    [ObservableProperty]
    private int _rateLimitSaveIntervalSeconds = 60;

    [ObservableProperty]
    private bool _restoreRateLimitStateOnStartup = true;

    [ObservableProperty]
    private int _auditBatchSize = 1000;

    [ObservableProperty]
    private int _auditFlushIntervalSeconds = 5;

    [ObservableProperty]
    private string _settingsMessage = string.Empty;

    /// <summary>Creates the page and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public SecuritySettingsViewModel(ManagementConnection connection)
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

        ApiResponse<SecuritySettingsDto>? response = await _connection.Api.GetSecuritySettingsAsync();
        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not read the limit and audit settings from the server.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = string.Empty;
    }

    /// <summary>Saves the settings; the server checks them and writes security-config.json.</summary>
    /// <returns>A task that completes when the server has answered.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            SettingsMessage = "Not connected.";
            return;
        }

        SecuritySettingsDto settings = new SecuritySettingsDto
        {
            SkipLimitsForAdmins = SkipLimitsForAdmins,
            RateLimitSaveIntervalSeconds = RateLimitSaveIntervalSeconds,
            RestoreRateLimitStateOnStartup = RestoreRateLimitStateOnStartup,
            AuditBatchSize = AuditBatchSize,
            AuditFlushIntervalSeconds = AuditFlushIntervalSeconds,
        };

        if (!TryLimit(PerUserRequestsPerSecond, out int? a) || !TryLimit(PerUserMegabytesPerHour, out int? b)
            || !TryLimit(PerIpRequestsPerSecond, out int? c) || !TryLimit(PerIpMegabytesPerHour, out int? d)
            || !TryLimit(ServerRequestsPerSecond, out int? e) || !TryLimit(ServerMegabytesPerHour, out int? f))
        {
            SettingsMessage = "A limit is a whole number, or empty for no limit.";
            return;
        }

        settings.PerUserRequestsPerSecond = a;
        settings.PerUserMegabytesPerHour = b;
        settings.PerIpRequestsPerSecond = c;
        settings.PerIpMegabytesPerHour = d;
        settings.ServerRequestsPerSecond = e;
        settings.ServerMegabytesPerHour = f;

        ApiResponse<SecuritySettingsDto>? response = await _connection.Api.UpdateSecuritySettingsAsync(settings);
        if (response is not { Success: true, Data: not null })
        {
            SettingsMessage = response?.Error?.Message ?? "Could not save the limit and audit settings.";
            return;
        }

        Fill(response.Data);
        SettingsMessage = "Saved. Restart RetroServer to use them.";
    }

    /// <summary>Reads a limit box: empty is no limit (null), otherwise a whole number.</summary>
    /// <param name="text">The box.</param>
    /// <param name="limit">Receives the limit.</param>
    /// <returns>False when the box holds something that is not a whole number.</returns>
    public static bool TryLimit(string text, out int? limit)
    {
        limit = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            limit = value;
            return true;
        }

        return false;
    }

    private static string Show(int? limit)
    {
        return limit.HasValue ? limit.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void Fill(SecuritySettingsDto s)
    {
        PerUserRequestsPerSecond = Show(s.PerUserRequestsPerSecond);
        PerUserMegabytesPerHour = Show(s.PerUserMegabytesPerHour);
        SkipLimitsForAdmins = s.SkipLimitsForAdmins;
        PerIpRequestsPerSecond = Show(s.PerIpRequestsPerSecond);
        PerIpMegabytesPerHour = Show(s.PerIpMegabytesPerHour);
        ServerRequestsPerSecond = Show(s.ServerRequestsPerSecond);
        ServerMegabytesPerHour = Show(s.ServerMegabytesPerHour);
        RateLimitSaveIntervalSeconds = s.RateLimitSaveIntervalSeconds;
        RestoreRateLimitStateOnStartup = s.RestoreRateLimitStateOnStartup;
        AuditBatchSize = s.AuditBatchSize;
        AuditFlushIntervalSeconds = s.AuditFlushIntervalSeconds;
    }
}
