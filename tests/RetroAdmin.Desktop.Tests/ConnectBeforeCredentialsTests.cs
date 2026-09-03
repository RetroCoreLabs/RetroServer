//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using RetroAdmin.Desktop.Services.ServerControl;
using RetroAdmin.Desktop.ViewModels;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// The connect-then-sign-in order, and what the user is shown at each step.
/// </summary>
/// <remarks>
/// The first version of this window asked for a user name and password up front and only then
/// tried to reach the server, so "nothing is listening" arrived as a failed sign-in. These tests
/// pin the corrected order: the credential fields do not appear until a server has answered.
/// </remarks>
[TestFixture]
public class ConnectBeforeCredentialsTests
{
    /// <summary>Settings pointing at a port with nothing on it.</summary>
    /// <param name="mode">Connection mode to use.</param>
    /// <returns>The settings.</returns>
    private static ServerConnectionSettings NoServerAt(ServerConnectionMode mode) =>
        new ServerConnectionSettings
        {
            Mode = mode,
            Address = "https://localhost:5098",
            AllowUntrustedCertificate = true
        };

    [Test]
    public async Task NoServer_ReportsNoServer_AndNeverAsksForCredentials()
    {
        await using ManagementConnection connection =
            new ManagementConnection(NoServerAt(ServerConnectionMode.Remote));

        ReachabilityResult result = await connection.ProbeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(Reachability.NoServer));
            Assert.That(result.ServerPresent, Is.False);

            // The message must say the server is missing, not blame the user's credentials.
            Assert.That(result.Message, Does.Contain("5098"));
            Assert.That(result.Message.ToLowerInvariant(), Does.Not.Contain("password"));
            Assert.That(result.Message.ToLowerInvariant(), Does.Not.Contain("username"));
        });
    }

    [Test]
    public void NoServer_HidesTheCredentialFields()
    {
        ManagementConnection connection =
            new ManagementConnection(NoServerAt(ServerConnectionMode.Remote));

        LoginViewModel vm = new LoginViewModel(connection);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ShowCredentials, Is.False,
                "credentials must not be asked for before a server has answered");
            Assert.That(vm.ShowConnect, Is.True);
        });
    }

    [Test]
    public void NoServer_OffersToStartOne_OnlyWhenThisModeCan()
    {
        ManagementConnection local =
            new ManagementConnection(NoServerAt(ServerConnectionMode.LocalSubprocess));
        ManagementConnection remote =
            new ManagementConnection(NoServerAt(ServerConnectionMode.Remote));

        LoginViewModel localVm = new LoginViewModel(local);
        LoginViewModel remoteVm = new LoginViewModel(remote);

        Assert.Multiple(() =>
        {
            Assert.That(localVm.ShowStartServer, Is.True,
                "a local server is ours to start");
            Assert.That(remoteVm.ShowStartServer, Is.False,
                "a remote server runs on another machine; offering a start button that cannot " +
                "work is worse than not offering one");
        });

        (local.Lifecycle as IDisposable)?.Dispose();
        (remote.Lifecycle as IDisposable)?.Dispose();
    }

    [Test]
    public async Task SignIn_BeforeConnecting_IsRefusedWithoutTouchingCredentials()
    {
        await using ManagementConnection connection =
            new ManagementConnection(NoServerAt(ServerConnectionMode.Remote));

        LoginViewModel vm = new LoginViewModel(connection)
        {
            Username = "admin",
            Password = "whatever"
        };

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(connection.IsAuthenticated, Is.False);
            Assert.That(vm.StatusMessage, Does.Contain("Connect"),
                "the guard must say to connect first, not that the sign-in failed");
        });
    }
}
