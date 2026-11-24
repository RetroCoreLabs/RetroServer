//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Threading;
using System.Threading.Tasks;
using RetroIO.Management.Client;
using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.Services.ServerControl;

/// <summary>What a credential-free probe of the configured address found.</summary>
public enum Reachability
{
    /// <summary>Nothing answered. No server is running there.</summary>
    NoServer = 0,

    /// <summary>A RetroFS server answered and wants credentials.</summary>
    NeedsCredentials = 1,

    /// <summary>A RetroFS server answered and already accepts the current token.</summary>
    Ready = 2,

    /// <summary>Something answered that is not a RetroFS server.</summary>
    NotRetroFs = 3,

    /// <summary>A server answered but its TLS certificate was refused.</summary>
    CertificateRejected = 4
}

/// <summary>Result of a reachability probe.</summary>
/// <param name="State">What was found.</param>
/// <param name="Message">Text safe to show the user.</param>
public readonly record struct ReachabilityResult(Reachability State, string Message)
{
    /// <summary>True when a RetroFS server answered, whatever it wants next.</summary>
    public bool ServerPresent =>
        State is Reachability.NeedsCredentials or Reachability.Ready;
}

/// <summary>Why a connection attempt did not succeed.</summary>
public enum ConnectionOutcome
{
    /// <summary>Logged in and, where applicable, subscribed to live updates.</summary>
    Connected = 0,

    /// <summary>The server could not be reached at all.</summary>
    Unreachable = 1,

    /// <summary>The server answered, but rejected the credentials.</summary>
    InvalidCredentials = 2,

    /// <summary>Credentials accepted, but the account may not manage the server.</summary>
    NotAnAdministrator = 3
}

/// <summary>Result of a connection attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">Text safe to show the user.</param>
public readonly record struct ConnectionResult(ConnectionOutcome Outcome, string Message)
{
    /// <summary>True when the UI may start issuing management calls.</summary>
    public bool Connected => Outcome == ConnectionOutcome.Connected;
}

/// <summary>
/// The UI's single seam onto a RetroFS server: credentials, REST client, live updates and
/// lifetime control.
/// </summary>
/// <remarks>
/// This replaces RetroServerHost, which started the protocol servers inside the UI process. That
/// arrangement made RetroServeUI a third independent copy of the server rather than a client of
/// one, and it is why the UI could only ever manage a server running on the same machine, in the
/// same process.
///
/// Connecting is now explicit and ordered: log in, then subscribe. The old code built a hub client
/// in the MainWindowViewModel constructor and connected unconditionally, outside any check, then
/// swallowed the inevitable failure into a Console.WriteLine that a windowed application never
/// shows. With authentication on, that connection would now be refused outright, so the order
/// matters rather than being merely tidier.
/// </remarks>
public sealed class ManagementConnection : IAsyncDisposable
{
    private readonly ServerConnectionSettings _settings;

    private readonly bool _savesSettings;

    /// <summary>REST client for the management API.</summary>
    public ManagementApiClient Api { get; private set; }

    /// <summary>Live push channel. Only connected after a successful login.</summary>
    public ManagementHubClient Hub { get; private set; }

    /// <summary>Starts and stops the server, when the mode allows it.</summary>
    public IServerLifecycleController Lifecycle { get; private set; }

    /// <summary>Settings this connection was built from.</summary>
    public ServerConnectionSettings Settings => _settings;

    /// <summary>True once credentials have been accepted.</summary>
    public bool IsAuthenticated => Api.IsAuthenticated;

    /// <summary>True while live updates are flowing.</summary>
    public bool IsLive => Hub.IsConnected;

    /// <summary>Name of the logged-in user, empty when not logged in.</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>Raised with a human-readable line whenever the connection state changes.</summary>
    /// <remarks>
    /// The host decides where these go. The classes underneath used to write them to the console,
    /// which is invisible in a WinExe.
    /// </remarks>
    public event Action<string>? StatusChanged;

    /// <summary>Surfaces a message through <see cref="StatusChanged"/>.</summary>
    /// <param name="message">Text to show.</param>
    /// <remarks>
    /// Lets a view model report to the same place the connection does, instead of inventing its
    /// own sink. UsersViewModel used to write these to a console that a WinExe does not have.
    /// </remarks>
    public void RaiseStatus(string message) => StatusChanged?.Invoke(message);

    /// <summary>Builds a connection from settings, without contacting anything yet.</summary>
    /// <param name="settings">Connection settings, or null for the persisted ones.</param>
    public ManagementConnection(ServerConnectionSettings? settings = null)
    {
        _settings = settings ?? ServerConnectionSettings.Instance;

        // Only the program's own saved settings are written back. A connection given its own
        // settings (every test) must never overwrite the user's saved address.
        _savesSettings = settings == null;
        (Api, Hub, Lifecycle) = Build(_settings);
    }

    // Controllers replaced by ChangeAddressAsync. Kept, not disposed, until this connection is:
    // disposing a subprocess controller kills the server it started, and pointing the window at
    // another address must not stop a server the user started a moment ago.
    private readonly System.Collections.Generic.List<IServerLifecycleController> _retiredLifecycles =
        new System.Collections.Generic.List<IServerLifecycleController>();

    private (ManagementApiClient, ManagementHubClient, IServerLifecycleController) Build(ServerConnectionSettings settings)
    {
        ManagementApiClient api = new ManagementApiClient(settings.Address, settings.AllowUntrustedCertificate);

        // The token is read through a delegate, not captured, so a reconnect after the token was
        // renewed presents the current one rather than the one that existed at construction.
        ManagementHubClient hub = new ManagementHubClient(
            settings.Address,
            settings.AllowUntrustedCertificate,
            () => Api.AccessToken);

        IServerLifecycleController lifecycle = ServerLifecycleControllerFactory.Create(settings);

        api.RequestFailed += (operation, ex) => StatusChanged?.Invoke($"{operation} failed: {ex.Message}");
        hub.ConnectionStateChanged += (state, ex) =>
            StatusChanged?.Invoke(ex == null ? $"Live updates {state}." : $"Live updates {state}: {ex.Message}");
        return (api, hub, lifecycle);
    }

    /// <summary>
    /// Points the connection at another server address and, for the program's own settings, saves it for the next start.
    /// </summary>
    /// <param name="address">The new address, for example https://localhost:5001.</param>
    /// <returns>Null when the address was taken, else why it was refused, in words for the user.</returns>
    /// <remarks>
    /// Until 2026-11-24 the sign-in window let the user type an address and then went on using the
    /// one read at start: the client was built once in the constructor, so editing the box did
    /// nothing. Any token and live connection belong to the old server and are dropped.
    /// </remarks>
    public async Task<string?> ChangeAddressAsync(string address)
    {
        string trimmed = (address ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"\"{address}\" is not a server address. Write it like https://localhost:5001.";
        }

        if (string.Equals(trimmed, _settings.Address, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await Hub.DisposeAsync().ConfigureAwait(false);
        Api.Dispose();
        _retiredLifecycles.Add(Lifecycle);
        Username = string.Empty;

        _settings.Address = trimmed;
        if (_savesSettings)
        {
            _settings.Save();
        }
        (Api, Hub, Lifecycle) = Build(_settings);
        StatusChanged?.Invoke($"Server address changed to {trimmed}.");
        return null;
    }

    /// <summary>
    /// Asks the server which build it is (GET /version, no login needed).
    /// </summary>
    /// <returns>The server's own one-line description, or null when it did not say.</returns>
    /// <remarks>
    /// Null also for a RetroServer older than 2026-11-24, which has no /version. The caller shows
    /// "unknown" then, never a guess.
    /// </remarks>
    public async Task<string?> GetServerBuildAsync()
    {
        try
        {
            using System.Net.Http.HttpClientHandler handler = new System.Net.Http.HttpClientHandler();
            if (_settings.AllowUntrustedCertificate)
            {
                handler.ServerCertificateCustomValidationCallback =
                    System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            using System.Net.Http.HttpClient client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using System.Net.Http.HttpResponseMessage response =
                await client.GetAsync(_settings.Address + "/version").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string text = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            return text.StartsWith("RetroServer ", StringComparison.Ordinal) ? text : null;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether a server is actually there, without sending any credentials.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was found, with a message safe to show the user.</returns>
    /// <remarks>
    /// Step one of connecting. Nothing should ask the user for a name and password until this has
    /// said a server answered: prompting first and failing afterwards reports a credential
    /// problem when the real problem is that nothing was listening.
    /// </remarks>
    public async Task<ReachabilityResult> ProbeAsync(CancellationToken ct = default)
    {
        ManagementApiClient.ProbeResult probe = await Api.ProbeAsync().ConfigureAwait(false);

        switch (probe)
        {
            case ManagementApiClient.ProbeResult.NeedsCredentials:
                return new ReachabilityResult(Reachability.NeedsCredentials,
                    $"Connected to {_settings.Address}. Sign in to continue.");

            case ManagementApiClient.ProbeResult.Ready:
                return new ReachabilityResult(Reachability.Ready,
                    $"Connected to {_settings.Address}.");

            case ManagementApiClient.ProbeResult.CertificateRejected:
                return new ReachabilityResult(Reachability.CertificateRejected,
                    $"A server answered at {_settings.Address}, but its TLS certificate was refused. " +
                    "If this is a development server on this machine, allow untrusted certificates.");

            case ManagementApiClient.ProbeResult.NotRetroFs:
                return new ReachabilityResult(Reachability.NotRetroFs,
                    $"Something is answering at {_settings.Address}, but it is not a RetroServer.");

            default:
                return new ReachabilityResult(Reachability.NoServer,
                    _settings.SupportsLifecycleControl
                        ? $"No server is running at {_settings.Address}."
                        : $"No server answered at {_settings.Address}. Start it on that machine, or change the address.");
        }
    }

    /// <summary>Logs in and, on success, subscribes to live updates.</summary>
    /// <param name="username">RetroIDP user name.</param>
    /// <param name="password">Password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened, with a message safe to show the user.</returns>
    public async Task<ConnectionResult> ConnectAsync(string username, string password,
                                                     CancellationToken ct = default)
    {
        ApiResponse<LoginResponse>? response = await Api.LoginAsync(username, password).ConfigureAwait(false);

        if (response == null)
        {
            return new ConnectionResult(ConnectionOutcome.Unreachable,
                $"Could not reach a RetroServer at {_settings.Address}.");
        }

        if (!response.Success || response.Data == null)
        {
            return new ConnectionResult(ConnectionOutcome.InvalidCredentials,
                response.Error?.Message ?? "Invalid username or password.");
        }

        Username = response.Data.Username;

        if (!response.Data.IsAdmin)
        {
            // The token is valid and every management call would return 403. Saying so once here
            // beats letting the user walk into a wall of refusals with no explanation.
            return new ConnectionResult(ConnectionOutcome.NotAnAdministrator,
                $"{response.Data.Username} is not an administrator and cannot manage this server.");
        }

        StatusChanged?.Invoke($"Signed in as {Username}.");

        try
        {
            await Hub.ConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Live updates are a convenience: without them the UI polls instead. Refusing the
            // whole connection over it would be worse than saying so and carrying on.
            StatusChanged?.Invoke($"Live updates unavailable: {ex.Message}");
        }

        return new ConnectionResult(ConnectionOutcome.Connected, $"Connected to {_settings.Address}.");
    }

    /// <summary>Drops the token and the live connection.</summary>
    /// <returns>A task that completes once disconnected.</returns>
    public async Task DisconnectAsync()
    {
        await Hub.DisconnectAsync().ConfigureAwait(false);
        Api.SetAccessToken(null);
        Username = string.Empty;
        StatusChanged?.Invoke("Signed out.");
    }

    /// <summary>Closes the connection and, in subprocess mode, the server it started.</summary>
    /// <returns>A task that completes once everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
        await Hub.DisposeAsync().ConfigureAwait(false);
        Api.Dispose();

        // Disposing the subprocess controller kills the server it started, so closing the UI does
        // not leave one behind holding the TNFS and Econet ports.
        (Lifecycle as IDisposable)?.Dispose();
        for (int i = 0; i < _retiredLifecycles.Count; i++)
        {
            (_retiredLifecycles[i] as IDisposable)?.Dispose();
        }
    }
}
