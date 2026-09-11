//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using CommunityToolkit.Mvvm.ComponentModel;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>Landing view: which server the UI is pointed at, and how.</summary>
/// <remarks>
/// It used to show the RetroFS data path of the server hosted inside this process. There is no
/// such server any more, and a remote one's data path is on another machine and means nothing
/// here, so it shows the connection instead.
/// </remarks>
public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _serverInfo = "Server Information";

    /// <summary>Creates the view model.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public DashboardViewModel(ManagementConnection connection)
    {
        ServerInfo = $"{connection.Lifecycle.Description} — {connection.Settings.Address}";
    }
}
