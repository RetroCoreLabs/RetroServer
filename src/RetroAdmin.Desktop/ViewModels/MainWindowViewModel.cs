//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>Shell view model: connection, navigation and the start/stop button.</summary>
/// <remarks>
/// It used to construct a RetroServerHost and start the protocol servers inside this process.
/// Now it owns a ManagementConnection and asks a server, which may be a child process, a Windows
/// service, or a machine somewhere else entirely.
/// </remarks>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;
    private LogsViewModel? _logsViewModel;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _serverStatus = "Disconnected";

    [ObservableProperty]
    private IBrush _serverStatusBackground = new SolidColorBrush(Color.Parse("#FF4444"));

    [ObservableProperty]
    private string _serverButtonText = "Start Server";

    [ObservableProperty]
    private IBrush _serverButtonBackground = new SolidColorBrush(Color.Parse("#00FF00"));

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True while the server is believed to be running.</summary>
    private bool _isRunning;

    /// <summary>
    /// Whether the start/stop button should be enabled.
    /// </summary>
    /// <remarks>
    /// False against a remote server: its lifetime belongs to the machine it runs on, so the
    /// button disables rather than offering an action that always fails.
    /// </remarks>
    public bool CanControlServer => _connection.Lifecycle.SupportsLifecycleControl;

    /// <summary>Description of what is being managed, for the status bar.</summary>
    public string ConnectionDescription => _connection.Lifecycle.Description;

    /// <summary>Creates the shell.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public MainWindowViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _connection.StatusChanged += message => Dispatcher.UIThread.Post(() => StatusMessage = message);
        _connection.Hub.ServerStatusUpdated += OnServerStatusUpdate;

        CurrentView = new DashboardViewModel(_connection);
    }

    /// <summary>Applies a status push from the server.</summary>
    /// <param name="status">Run state and uptime.</param>
    private void OnServerStatusUpdate(ServerStatusUpdate status)
    {
        Dispatcher.UIThread.Post(() => ApplyRunState(status.IsRunning, status.Uptime));
    }

    /// <summary>Repaints the status pill and the button.</summary>
    /// <param name="running">Whether the server is running.</param>
    /// <param name="uptime">Uptime text, when known.</param>
    private void ApplyRunState(bool running, string uptime = "")
    {
        _isRunning = running;

        if (running)
        {
            ServerStatus = string.IsNullOrEmpty(uptime) ? "Running" : $"Running - {uptime}";
            ServerStatusBackground = new SolidColorBrush(Color.Parse("#00FF00"));
            ServerButtonText = "Stop Server";
            ServerButtonBackground = new SolidColorBrush(Color.Parse("#FF4444"));
        }
        else
        {
            ServerStatus = "Stopped";
            ServerStatusBackground = new SolidColorBrush(Color.Parse("#FF4444"));
            ServerButtonText = "Start Server";
            ServerButtonBackground = new SolidColorBrush(Color.Parse("#00FF00"));
        }
    }

    /// <summary>Starts or stops the server, depending on its current state.</summary>
    /// <returns>A task that completes once the attempt has been made.</returns>
    [RelayCommand]
    private async Task ToggleServer()
    {
        if (!CanControlServer)
        {
            StatusMessage = $"{ConnectionDescription} cannot be started or stopped from here.";
            return;
        }

        ServerControlResult result = _isRunning
            ? await _connection.Lifecycle.StopAsync()
            : await _connection.Lifecycle.StartAsync();

        StatusMessage = result.Message;

        if (result.Succeeded) ApplyRunState(!_isRunning);
    }

    /// <summary>Switches the main pane.</summary>
    /// <param name="destination">Name of the view to show.</param>
    [RelayCommand]
    private void Navigate(string destination)
    {
        CurrentView = destination switch
        {
            "Dashboard" => new DashboardViewModel(_connection),
            "TNFS" => new TnfsMonitorViewModel(_connection),
            "Econet" => new EconetMonitorViewModel(_connection),
            "XmsgHub" => new XmsgHubViewModel(_connection),
            "XmsgNode" => new XmsgNodeSettingsViewModel(_connection),
            "DriveWire" => new DriveWireSettingsViewModel(_connection),
            "Security" => new SecuritySettingsViewModel(_connection),
            "Mounts" => new MountsViewModel(_connection),
            "Startup" => new StartupSettingsViewModel(_connection),
            "Volumes" => new VolumesViewModel(_connection),
            "Users" => new UsersViewModel(_connection),

            // Kept alive across navigations so its buffer is not thrown away every time the user
            // looks at another page.
            "Logs" => _logsViewModel ??= new LogsViewModel(_connection),
            "Firewall" => new FirewallViewModel(_connection),
            "Settings" => new SettingsViewModel(_connection),
            _ => new DashboardViewModel(_connection)
        };
    }
}
