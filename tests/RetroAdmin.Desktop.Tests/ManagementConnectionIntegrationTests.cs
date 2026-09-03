//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Diagnostics;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// Drives a real RetroServer process through the same connection object the UI uses.
/// </summary>
/// <remarks>
/// This is the test that proves the client half of the architecture actually works: the UI's
/// connection logs in over HTTP, gets a token, and reads real data back. Everything else about
/// "the UI is a client now" is structure; this is the part that would fail if the structure were
/// wrong.
///
/// The spawned server gets its own RETROFS_DATA_PATH, so it creates its own identity store and
/// configuration in a temp directory and never touches the developer's real settings under
/// Documents\RetroFS. It also gets its OWN management ports through --Management:Urls, and its
/// own protocol ports: binding the standard 5000/5001 made this fixture fail intermittently
/// whenever anything else held them -- a developer's running instance, a leftover, or another
/// test project spawning a server of its own.
/// </remarks>
[TestFixture]
[Category("Integration")]
[Category("ProcessSpawn")]
public class ManagementConnectionIntegrationTests
{
    /// <summary>Credentials RetroIdpBootstrap creates on a fresh data directory.</summary>
    private const string AdminUser = "admin";

    /// <summary>Default password for that account on a fresh install.</summary>
    private const string AdminPassword = "retroadmin";

    /// <summary>Line the server logs once the HTTP surface is up.</summary>
    private const string ReadyMarker = "Application started";

    /// <summary>HTTPS address this fixture's server binds, away from the standard ports.</summary>
    private const string TestApiAddress = "https://localhost:5111";

    /// <summary>Both addresses, in the form --Management:Urls expects.</summary>
    private const string TestManagementUrls = "http://localhost:5110;https://localhost:5111";

    /// <summary>TNFS ports for this fixture, away from the defaults.</summary>
    private const int TestTnfsPort = 16390;

    /// <summary>Econet AUN port for this fixture, away from the default.</summary>
    private const int TestEconetPort = 32790;

    private Process? _server;
    private string _dataPath = null!;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        _dataPath = Path.Combine(Path.GetTempPath(), "retroserve-conn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataPath);

        string solutionDir = TestContext.CurrentContext.TestDirectory;

        while (!Directory.Exists(Path.Combine(solutionDir, "src")) &&
               Directory.GetParent(solutionDir) != null)
        {
            solutionDir = Directory.GetParent(solutionDir)!.FullName;
        }

        string project = Path.Combine(solutionDir, "src", "RetroServer", "RetroServer.csproj");
        Assert.That(File.Exists(project), Is.True, $"RetroServer project not found at {project}");

        _server = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{project}\" --no-build -- " +
                            $"--Management:Urls={TestManagementUrls} " +
                            $"--Tnfs:UdpPort={TestTnfsPort} --Tnfs:TcpPort={TestTnfsPort} " +
                            $"--Econet:UdpPort={TestEconetPort}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        // Its own data directory, set on the CHILD only. Without this the server rewrites the
        // developer's real tnfs-config.json every time the suite runs.
        _server.StartInfo.Environment["RETROFS_DATA_PATH"] = _dataPath;

        TaskCompletionSource ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _server.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            TestContext.Out.WriteLine($"[SERVER] {e.Data}");
            if (e.Data.Contains(ReadyMarker, StringComparison.Ordinal)) ready.TrySetResult();
        };

        _server.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            TestContext.Error.WriteLine($"[SERVER ERROR] {e.Data}");
            if (e.Data.Contains(ReadyMarker, StringComparison.Ordinal)) ready.TrySetResult();
        };

        _server.Start();
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        // Wait on the server's own output, never on a sleep. It has to build the identity store
        // (Argon2id) and both protocol stacks before the HTTP surface is up.
        Task finished = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(90)));

        if (finished != ready.Task)
        {
            throw new TimeoutException(
                $"RetroServer did not log \"{ReadyMarker}\" within 90 seconds. " +
                "See the [SERVER] lines above.");
        }
    }

    [OneTimeTearDown]
    public void StopServer()
    {
        if (_server is { HasExited: false })
        {
            _server.Kill(entireProcessTree: true);
            _server.WaitForExit(10000);
        }

        _server?.Dispose();

        try
        {
            if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, true);
        }
        catch (IOException)
        {
            // The server keeps a log file open in there; a leftover temp directory is harmless.
        }
    }

    /// <summary>Builds a connection pointed at the spawned server.</summary>
    /// <returns>The connection.</returns>
    private static ManagementConnection Connect() => new ManagementConnection(
        new ServerConnectionSettings
        {
            Mode = ServerConnectionMode.Remote,
            Address = TestApiAddress,

            // The server presents the ASP.NET Core development certificate, which the test host
            // has no reason to trust. This is exactly the case the opt-in flag exists for.
            AllowUntrustedCertificate = true
        });

    [Test]
    public async Task Connect_WithTheAdminAccount_Succeeds()
    {
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);

        Assert.Multiple(() =>
        {
            Assert.That(result.Connected, Is.True, result.Message);
            Assert.That(connection.IsAuthenticated, Is.True);
            Assert.That(connection.Username, Is.EqualTo(AdminUser));
        });
    }

    [Test]
    public async Task Connect_WithAWrongPassword_ReportsInvalidCredentials()
    {
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, "not-the-password");

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ConnectionOutcome.InvalidCredentials));
            Assert.That(connection.IsAuthenticated, Is.False);
        });
    }

    [Test]
    public async Task Connect_ToAnAddressWithNoServer_ReportsUnreachable()
    {
        // Port 5099 has nothing on it. This is the case that must not look like a bad password,
        // because the two need completely different things from the user.
        await using ManagementConnection connection = new ManagementConnection(
            new ServerConnectionSettings
            {
                Mode = ServerConnectionMode.Remote,
                Address = "https://localhost:5099",
                AllowUntrustedCertificate = true
            });

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);

        Assert.That(result.Outcome, Is.EqualTo(ConnectionOutcome.Unreachable), result.Message);
    }

    [Test]
    public async Task AuthenticatedClient_ReadsRealDataBack()
    {
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);
        Assert.That(result.Connected, Is.True, result.Message);

        ApiResponse<UserListResponse>? users = await connection.Api.GetUsersAsync();

        Assert.Multiple(() =>
        {
            Assert.That(users, Is.Not.Null, "the request itself failed");
            Assert.That(users!.Success, Is.True);
            Assert.That(users.Data!.Users, Is.Not.Empty, "the bootstrap admin account should be listed");
        });
    }

    [Test]
    public async Task EconetActivityEndpoint_AnswersTheClient()
    {
        // The endpoint added so the UI's Econet monitor no longer reads an in-process object.
        // Empty is the right answer here: no Econet client has connected to this server.
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);
        Assert.That(result.Connected, Is.True, result.Message);

        ApiResponse<EconetActivityResponse>? activity = await connection.Api.GetEconetActivityAsync(5);

        Assert.Multiple(() =>
        {
            Assert.That(activity, Is.Not.Null);
            Assert.That(activity!.Success, Is.True);
            Assert.That(activity.Data!.Commands, Is.Not.Null);
            Assert.That(activity.Data.Transfers, Is.Not.Null);
        });
    }

    [Test]
    public async Task WithoutLoggingIn_ManagementCallsAreRefused()
    {
        // The API answered anyone who could reach the port until authentication landed. This is
        // the regression guard for that: no token, no data.
        await using ManagementConnection connection = Connect();

        ApiResponse<UserListResponse>? users = await connection.Api.GetUsersAsync();

        Assert.That(users?.Data?.Users, Is.Null.Or.Empty,
            "an unauthenticated caller must not receive the user list");
    }
}
