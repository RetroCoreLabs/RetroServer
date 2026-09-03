//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>Connects to a server, then signs in — in that order.</summary>
/// <remarks>
/// The order is the whole point. An earlier version asked for a user name and password first and
/// only then tried to reach the server, so "nothing is listening on that port" surfaced as a
/// failed sign-in. The credential fields are not even shown until a server has answered.
///
/// Stage one is a probe that sends no credentials. A 401 from it is the good outcome: it proves a
/// RetroFS API is there and wants a login. Anything else is reported as what it actually is —
/// no server, something else on the port, or a refused certificate — each of which needs a
/// different fix from the user.
/// </remarks>
public partial class LoginViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    /// <summary>Address being connected to.</summary>
    [ObservableProperty]
    private string _address = string.Empty;

    /// <summary>How the server is reached, for display.</summary>
    [ObservableProperty]
    private string _connectionDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCredentials))]
    [NotifyPropertyChangedFor(nameof(ShowStartServer))]
    [NotifyPropertyChangedFor(nameof(ShowConnect))]
    [NotifyPropertyChangedFor(nameof(IsProblem))]
    [NotifyPropertyChangedFor(nameof(IsFound))]
    [NotifyPropertyChangedFor(nameof(StateHeadline))]
    private Reachability _state = Reachability.NoServer;

    /// <summary>True when the probe found something wrong the user must act on.</summary>
    /// <remarks>
    /// Drives the status strip's colour through a style class rather than a brush set here.
    /// The view model must not pick colours: the app has eight themes and a hardcoded one would
    /// stop following whichever is live.
    /// </remarks>
    public bool IsProblem => State is Reachability.NoServer
                                   or Reachability.NotRetroFs
                                   or Reachability.CertificateRejected;

    /// <summary>True when a RetroFS server answered.</summary>
    public bool IsFound => State is Reachability.NeedsCredentials or Reachability.Ready;

    /// <summary>One-line headline for the status strip.</summary>
    public string StateHeadline => State switch
    {
        Reachability.NoServer => "No server found",
        Reachability.NotRetroFs => "Not a RetroFS server",
        Reachability.CertificateRejected => "Certificate refused",
        Reachability.NeedsCredentials => "Server found — sign in",
        Reachability.Ready => "Connected",
        _ => string.Empty
    };

    /// <summary>True once a server has answered, which is when credentials become relevant.</summary>
    public bool ShowCredentials => State == Reachability.NeedsCredentials;

    /// <summary>
    /// True when nothing answered and this mode could start one.
    /// </summary>
    /// <remarks>
    /// Against a remote server this stays false: its lifetime is not ours, so offering a start
    /// button that cannot work would be worse than not offering one.
    /// </remarks>
    public bool ShowStartServer =>
        State == Reachability.NoServer && _connection.Lifecycle.SupportsLifecycleControl;

    /// <summary>True while the user still needs to establish that a server is there.</summary>
    public bool ShowConnect => State != Reachability.NeedsCredentials;

    [ObservableProperty]
    private string _username = "admin";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProblem))]
    [NotifyPropertyChangedFor(nameof(IsFound))]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    private bool _isBusy;

    /// <summary>Label on the primary connect button.</summary>
    /// <remarks>
    /// The button stays enabled and accent-coloured throughout; only its text changes. Disabling
    /// it during the opening probe made the window arrive with a dead grey primary button that
    /// then turned blue a moment later, which reads as something being broken rather than as
    /// work in progress.
    /// </remarks>
    public string ConnectButtonText => IsBusy ? "Connecting..." : "Connect";

    /// <summary>Raised once signed in and the shell may open.</summary>
    public event Action? Connected;

    /// <summary>Creates the view model and probes straight away.</summary>
    /// <param name="connection">Connection to use.</param>
    public LoginViewModel(ManagementConnection connection)
    {
        _connection = connection;
        Address = connection.Settings.Address;
        ConnectionDescription = connection.Lifecycle.Description;

        // Probe on open, so the window arrives already knowing whether there is a server. The
        // user should not have to press anything to be told the obvious.
        _ = ConnectAsync();
    }

    /// <summary>Checks whether a server is there. Sends no credentials.</summary>
    /// <returns>A task that completes once the probe has finished.</returns>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy) return;   // the button already reads "Connecting..."; nothing to add

        IsBusy = true;

        try
        {
            StatusMessage = $"Looking for a server at {Address}...";

            ReachabilityResult result = await _connection.ProbeAsync();

            State = result.State;
            StatusMessage = result.Message;

            // Already holding a usable token: nothing to ask for.
            if (result.State == Reachability.Ready) Connected?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Starts a local server, then probes again.</summary>
    /// <returns>A task that completes once the server has been started and re-probed.</returns>
    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (IsBusy)
        {
            StatusMessage = "Still working — one moment.";
            return;
        }

        IsBusy = true;

        try
        {
            StatusMessage = "Starting the server...";

            ServerControlResult start = await _connection.Lifecycle.StartAsync();

            if (!start.Succeeded)
            {
                StatusMessage = start.Message;
                return;
            }

            // Poll rather than sleep a fixed time: a cold start has to build the identity store
            // and both protocol stacks before the HTTP surface answers.
            for (int attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(500);

                ReachabilityResult probe = await _connection.ProbeAsync();

                if (probe.ServerPresent)
                {
                    State = probe.State;
                    StatusMessage = probe.Message;
                    if (probe.State == Reachability.Ready) Connected?.Invoke();
                    return;
                }

                StatusMessage = $"Starting the server... ({attempt / 2 + 1}s)";
            }

            StatusMessage = "The server was started but did not answer within 30 seconds.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Signs in. Only reachable once a server has answered.</summary>
    /// <returns>A task that completes once the attempt has finished.</returns>
    [RelayCommand]
    private async Task SignIn()
    {
        // The state guard comes FIRST, before the busy check. The window probes on open, so
        // IsBusy is true for the first moment it exists; checking busy first meant a sign-in
        // attempt in that window returned silently, saying nothing at all.
        if (State != Reachability.NeedsCredentials)
        {
            StatusMessage = "Connect to a server first.";
            return;
        }

        if (IsBusy)
        {
            StatusMessage = "Still working — one moment.";
            return;
        }

        IsBusy = true;

        try
        {
            StatusMessage = "Signing in...";

            ConnectionResult result = await _connection.ConnectAsync(Username, Password);

            StatusMessage = result.Message;

            if (result.Connected) Connected?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
