//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
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

    [ObservableProperty]
    private bool _firewallEnabled = true;

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

        ApiResponse<FirewallRulesResponse>? response = await _connection.Api.GetFirewallRulesAsync();

        if (response is not { Success: true, Data: not null })
        {
            Status = response?.Error?.Message ?? "Could not read firewall rules from the server.";
            return;
        }

        Rules.Clear();

        List<FirewallRuleDto> rules = response.Data.Rules;

        for (int i = 0; i < rules.Count; i++)
        {
            FirewallRuleDto rule = rules[i];

            Rules.Add(new FirewallRuleInfo
            {
                RuleId = rule.RuleId,
                Name = rule.Name,
                IpPattern = rule.IPCondition,
                Action = rule.Action,
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
}
