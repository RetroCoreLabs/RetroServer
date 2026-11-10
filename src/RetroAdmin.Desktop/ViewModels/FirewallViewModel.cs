//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>Views and edits the server's firewall rules.</summary>
/// <remarks>
/// Previously reached into the live FirewallEngine and FirewallRuleManager belonging to the
/// server hosted in this process. Now goes through the firewall endpoints, so it works against a
/// server anywhere and the rule changes are subject to the same authorization as every other
/// management call.
/// </remarks>
public partial class FirewallViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    /// <summary>Rules currently configured on the server.</summary>
    public ObservableCollection<FirewallRuleInfo> Rules { get; } = new();

    /// <summary>The two values a rule's action and the default policy can take.</summary>
    public IReadOnlyList<string> Choices { get; } = new[] { "Allow", "Deny" };

    // ---- the firewall's settings (firewall-config.json), loaded from and saved to the server ----
    // Until 2026-11-10 the Enabled box was never loaded or sent, and the other settings had no
    // place on this page at all.

    [ObservableProperty]
    private bool _firewallEnabled = true;

    [ObservableProperty]
    private string _defaultPolicy = "Deny";

    [ObservableProperty]
    private bool _allowLocalhost = true;

    [ObservableProperty]
    private bool _allowPrivateNetworks = true;

    /// <summary>Addresses always let in, one per line.</summary>
    [ObservableProperty]
    private string _allowedAddresses = string.Empty;

    /// <summary>Addresses always refused, one per line.</summary>
    [ObservableProperty]
    private string _blockedAddresses = string.Empty;

    /// <summary>Networks (CIDR) always let in, one per line.</summary>
    [ObservableProperty]
    private string _allowedNetworks = string.Empty;

    [ObservableProperty]
    private int _cacheTtlSeconds = 60;

    [ObservableProperty]
    private int _newRulePriority = 100;

    [ObservableProperty]
    private string _newIpAddress = string.Empty;

    [ObservableProperty]
    private string _newRuleName = string.Empty;

    [ObservableProperty]
    private string _selectedAction = "Allow";

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Creates the view model and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public FirewallViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadRulesAsync();
    }

    /// <summary>Reloads the rule list from the server.</summary>
    /// <returns>A task that completes when the list has been refreshed.</returns>
    [RelayCommand]
    private async Task LoadRulesAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            Status = "Not connected.";
            return;
        }

        // One call gives the settings AND the rules.
        ApiResponse<FirewallConfigDto>? response = await _connection.Api.GetFirewallConfigAsync();

        if (response is not { Success: true, Data: not null })
        {
            Status = response?.Error?.Message ?? "Could not read the firewall settings from the server.";
            return;
        }

        FirewallConfigDto config = response.Data;
        FirewallEnabled = config.Enabled;
        DefaultPolicy = config.DefaultPolicy;
        AllowLocalhost = config.AllowLocalhost;
        AllowPrivateNetworks = config.AllowPrivateNetworks;
        AllowedAddresses = string.Join(System.Environment.NewLine, config.AllowedIpAddresses);
        BlockedAddresses = string.Join(System.Environment.NewLine, config.BlockedIpAddresses);
        AllowedNetworks = string.Join(System.Environment.NewLine, config.AllowedNetworks);
        CacheTtlSeconds = config.CacheTTLSeconds;

        Rules.Clear();

        List<FirewallRuleDto> rules = config.Rules;

        for (int i = 0; i < rules.Count; i++)
        {
            FirewallRuleDto rule = rules[i];

            Rules.Add(new FirewallRuleInfo
            {
                RuleId = rule.RuleId,
                Name = rule.Name,
                IpPattern = rule.IPCondition,
                Action = rule.Action,
                Priority = rule.Priority,
                Enabled = rule.Enabled,
                Description = rule.Description ?? string.Empty
            });
        }

        Status = $"{Rules.Count} rule(s).";
    }

    /// <summary>Creates a rule from the entry fields.</summary>
    /// <returns>A task that completes when the rule has been created and the list refreshed.</returns>
    [RelayCommand]
    private async Task AddRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewIpAddress) || string.IsNullOrWhiteSpace(NewRuleName))
        {
            Status = "A rule needs both a name and an address.";
            return;
        }

        ApiResponse<FirewallRuleDto>? response = await _connection.Api.CreateFirewallRuleAsync(
            new CreateFirewallRuleRequest
            {
                Name = NewRuleName,
                IPCondition = NewIpAddress,
                Action = SelectedAction,
                Priority = NewRulePriority,
                Description = "Created via RetroServe",
                Enabled = true
            });

        if (response is not { Success: true })
        {
            Status = response?.Error?.Message ?? "Could not create the rule.";
            return;
        }

        NewIpAddress = string.Empty;
        NewRuleName = string.Empty;

        await LoadRulesAsync();
    }

    /// <summary>Saves the firewall's settings (not the rules) to the server, which writes firewall-config.json.</summary>
    /// <returns>A task that completes when the settings have been saved and read back.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            Status = "Not connected.";
            return;
        }

        ApiResponse<FirewallConfigDto>? response = await _connection.Api.UpdateFirewallConfigAsync(
            new UpdateFirewallConfigRequest
            {
                Enabled = FirewallEnabled,
                DefaultPolicy = DefaultPolicy,
                AllowLocalhost = AllowLocalhost,
                AllowPrivateNetworks = AllowPrivateNetworks,
                AllowedIpAddresses = Lines(AllowedAddresses),
                BlockedIpAddresses = Lines(BlockedAddresses),
                AllowedNetworks = Lines(AllowedNetworks),
                CacheTTLSeconds = CacheTtlSeconds
            });

        if (response is not { Success: true })
        {
            Status = response?.Error?.Message ?? "Could not save the firewall settings.";
            return;
        }

        await LoadRulesAsync();
        Status = "Firewall settings saved. They are in force now and kept for the next start.";
    }

    /// <summary>Splits a box of one-entry-per-line text into a list, skipping blank lines.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The entries, trimmed.</returns>
    public static List<string> Lines(string text)
    {
        List<string> entries = new List<string>();
        string[] lines = (text ?? string.Empty).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string entry = lines[i].Trim();
            if (entry.Length > 0)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Deletes a rule.</summary>
    /// <param name="ruleId">Identifier of the rule to delete.</param>
    /// <returns>A task that completes when the rule has been deleted and the list refreshed.</returns>
    [RelayCommand]
    private async Task DeleteRule(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return;

        ApiResponse<object>? response = await _connection.Api.DeleteFirewallRuleAsync(ruleId);

        if (response is not { Success: true })
        {
            Status = response?.Error?.Message ?? "Could not delete the rule.";
            return;
        }

        await LoadRulesAsync();
    }
}

/// <summary>One row in the firewall rule list.</summary>
public class FirewallRuleInfo
{
    /// <summary>Rule identifier.</summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Rule name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Address or pattern the rule matches.</summary>
    public string IpPattern { get; set; } = string.Empty;

    /// <summary>Allow or Deny.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Free-text description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Order the rules are tried in; lower first.</summary>
    public int Priority { get; set; }

    /// <summary>Whether the rule is in use.</summary>
    public bool Enabled { get; set; }
}
