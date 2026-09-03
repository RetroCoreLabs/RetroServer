//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// Covers the layer that decides how the UI starts and stops a server.
/// </summary>
/// <remarks>
/// The interesting case is the remote one. Before this existed, "start the server" meant hosting
/// the protocol servers inside the UI process, so there was no notion of a server the UI cannot
/// control -- and a start button that always worked because it was starting something local.
/// </remarks>
[TestFixture]
public class ServerLifecycleControllerTests
{
    [Test]
    public void RemoteMode_RefusesLifecycleControlRatherThanFailingAtTheButton()
    {
        RemoteServerLifecycleController controller =
            new RemoteServerLifecycleController("https://retrobox.local:5001");

        Assert.That(controller.SupportsLifecycleControl, Is.False,
            "the UI binds button enablement to this; a remote server is not ours to start");
    }

    [Test]
    public async Task RemoteMode_StartAndStop_ReportNotSupportedWithAnActionableMessage()
    {
        RemoteServerLifecycleController controller =
            new RemoteServerLifecycleController("https://retrobox.local:5001");

        ServerControlResult start = await controller.StartAsync();
        ServerControlResult stop = await controller.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(start.Outcome, Is.EqualTo(ServerControlOutcome.NotSupported));
            Assert.That(stop.Outcome, Is.EqualTo(ServerControlOutcome.NotSupported));

            // The address has to appear, or the user cannot tell WHICH machine to go and use.
            Assert.That(start.Message, Does.Contain("retrobox.local"));
            Assert.That(stop.Message, Does.Contain("retrobox.local"));
        });
    }

    [Test]
    public async Task Subprocess_MissingExecutable_SaysWhereItLooked()
    {
        // Deploying the UI without RetroServer.exe beside it is a packaging mistake. A bare
        // "could not start server" would leave nothing to act on, so the path must be in the text.
        string missing = Path.Combine(Path.GetTempPath(),
            "retroserve-absent-" + Guid.NewGuid().ToString("N"), "RetroServer.exe");

        using SubprocessServerLifecycleController controller =
            new SubprocessServerLifecycleController(missing);

        ServerControlResult result = await controller.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(ServerControlOutcome.Failed));
            Assert.That(result.Message, Does.Contain(missing));
        });
    }

    [Test]
    public async Task Subprocess_StoppingWhenNotRunning_IsNotAnError()
    {
        using SubprocessServerLifecycleController controller =
            new SubprocessServerLifecycleController(
                Path.Combine(Path.GetTempPath(), "no-such-RetroServer.exe"));

        ServerControlResult result = await controller.StopAsync();

        Assert.That(result.Succeeded, Is.True,
            "stopping something already stopped is the state the caller asked for");
    }

    [Test]
    public void Subprocess_ReportsItCanControlLifecycle()
    {
        using SubprocessServerLifecycleController controller = new SubprocessServerLifecycleController();

        Assert.Multiple(() =>
        {
            Assert.That(controller.SupportsLifecycleControl, Is.True);
            Assert.That(controller.IsRunning, Is.False, "nothing started yet");
        });
    }

    [Test]
    public void Factory_MapsEachModeToItsController()
    {
        IServerLifecycleController subprocess = ServerLifecycleControllerFactory.Create(
            new ServerConnectionSettings { Mode = ServerConnectionMode.LocalSubprocess });

        IServerLifecycleController remote = ServerLifecycleControllerFactory.Create(
            new ServerConnectionSettings { Mode = ServerConnectionMode.Remote });

        Assert.Multiple(() =>
        {
            Assert.That(subprocess, Is.InstanceOf<SubprocessServerLifecycleController>());
            Assert.That(remote, Is.InstanceOf<RemoteServerLifecycleController>());
        });

        (subprocess as IDisposable)?.Dispose();
    }

    [Test]
    public void Factory_ServiceMode_MatchesThePlatform()
    {
        IServerLifecycleController controller = ServerLifecycleControllerFactory.Create(
            new ServerConnectionSettings { Mode = ServerConnectionMode.LocalService });

        if (OperatingSystem.IsWindows())
        {
            Assert.That(controller, Is.InstanceOf<WindowsServiceLifecycleController>());
        }
        else
        {
            // No SCM to talk to. Degrading to the remote controller means the buttons disable,
            // rather than a PlatformNotSupportedException escaping a click handler.
            Assert.That(controller, Is.InstanceOf<RemoteServerLifecycleController>());
        }
    }

    [Test]
    public void Settings_DefaultToLocalSubprocessAndTrustedTls()
    {
        ServerConnectionSettings settings = new ServerConnectionSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.Mode, Is.EqualTo(ServerConnectionMode.LocalSubprocess),
                "double-clicking the UI should still give you a running server");
            Assert.That(settings.Address, Is.EqualTo("https://localhost:5001"));
            Assert.That(settings.SupportsLifecycleControl, Is.True);

            // The old client trusted any certificate unconditionally, in every configuration.
            // The replacement must be off unless deliberately switched on.
            Assert.That(settings.AllowUntrustedCertificate, Is.False);
        });
    }

    [Test]
    public void Settings_RemoteMode_ReportsNoLifecycleControl()
    {
        ServerConnectionSettings settings = new ServerConnectionSettings
        {
            Mode = ServerConnectionMode.Remote,
            Address = "https://retrobox.local:5001"
        };

        Assert.That(settings.SupportsLifecycleControl, Is.False);
    }
}
