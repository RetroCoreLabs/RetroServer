//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAdmin.Desktop.Services.ServerControl;
using RetroServer.Service;

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
    public bool IsProblem => IsSignInRefused
                             || State is Reachability.NoServer
                                   or Reachability.NotRetroFs
                                   or Reachability.CertificateRejected;

    /// <summary>True when a RetroFS server answered.</summary>
    public bool IsFound => !IsSignInRefused && (State is Reachability.NeedsCredentials or Reachability.Ready);

    /// <summary>One-line headline for the status strip.</summary>
    public string StateHeadline => IsSignInRefused ? "Sign-in refused" : State switch
    {
        Reachability.NoServer => "No server found",
        Reachability.NotRetroFs => "Not a RetroServer",
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
    [NotifyPropertyChangedFor(nameof(SignInButtonText))]
    private bool _isBusy;

    /// <summary>Label on the primary connect button.</summary>
    /// <remarks>
    /// The button stays enabled and accent-coloured throughout; only its text changes. Disabling
    /// it during the opening probe made the window arrive with a dead grey primary button that
    /// then turned blue a moment later, which reads as something being broken rather than as
    /// work in progress.
    /// </remarks>
    public string ConnectButtonText => IsBusy ? "Connecting..." : "Connect";

    /// <summary>The Sign in button's text: says when it is working, so a press never looks ignored.</summary>
    public string SignInButtonText => IsBusy ? "Signing in..." : "Sign in";

    /// <summary>This program's build, shown from the moment the window opens.</summary>
    public string LocalBuild { get; } = BuildIdentity.Describe("RetroAdmin");

    /// <summary>The server's build once it has answered, else what is known about it.</summary>
    [ObservableProperty]
    private string _serverBuild = "RetroServer: not connected yet";

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
            // The address in the box is the one used. Until 2026-11-24 it was not: the
            // connection kept the address read at start, so typing a new one did nothing.
            string? refused = await _connection.ChangeAddressAsync(Address);
            if (refused != null)
            {
                State = Reachability.NoServer;
                StatusMessage = refused;
                return;
            }

            Address = _connection.Settings.Address;
            ConnectionDescription = _connection.Lifecycle.Description;
            StatusMessage = $"Looking for a server at {Address}...";
            ServerBuild = "RetroServer: looking...";

            ReachabilityResult result = await _connection.ProbeAsync();

            State = result.State;
            StatusMessage = result.Message;
            await ShowServerBuildAsync(result.ServerPresent);

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
                    await ShowServerBuildAsync(true);
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
        IsSignInRefused = false;

        try
        {
            StatusMessage = $"Signing in as {Username} at {Address}...";

            ConnectionResult result = await _connection.ConnectAsync(Username, Password);

            StatusMessage = result.Message;
            if (!result.Connected)
            {
                // The server answered with a refusal: say it in the headline too, not only below.
                IsSignInRefused = true;
            }

            if (result.Connected) Connected?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>True after a sign-in the server refused, until the next attempt or a new address.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProblem))]
    [NotifyPropertyChangedFor(nameof(StateHeadline))]
    [NotifyPropertyChangedFor(nameof(IsFound))]
    private bool _isSignInRefused;

    /// <summary>
    /// Enter in the window: signs in when the name and password are showing, else connects.
    /// </summary>
    /// <returns>The command's work.</returns>
    [RelayCommand]
    private Task EnterAsync()
    {
        return ShowCredentials ? SignIn() : ConnectAsync();
    }

    partial void OnAddressChanged(string value)
    {
        // A new address belongs to a server nobody has asked yet: back to the Connect stage, so
        // the name and password are never sent to an address that was not checked first.
        if (State == Reachability.NeedsCredentials &&
            !string.Equals((value ?? string.Empty).Trim().TrimEnd('/'), _connection.Settings.Address, StringComparison.OrdinalIgnoreCase))
        {
            State = Reachability.NoServer;
            IsSignInRefused = false;
            StatusMessage = "The address changed. Press Connect (or Enter) to look for a server there.";
            ServerBuild = "RetroServer: not connected yet";
        }
    }

    partial void OnPasswordChanged(string value)
    {
        IsSignInRefused = false;
    }

    private async Task ShowServerBuildAsync(bool present)
    {
        if (!present)
        {
            ServerBuild = "RetroServer: no answer";
            return;
        }

        string? build = await _connection.GetServerBuildAsync();
        ServerBuild = build ?? "RetroServer: version unknown (an older server, without /version)";
    }
}
