//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace RetroAdmin.Desktop.Services.ServerControl;

/// <summary>
/// Drives a RetroServer installed as a Windows service through the Service Control Manager.
/// </summary>
/// <remarks>
/// Starting and stopping a service needs administrator rights, which the UI normally does not
/// have. That is reported as <see cref="ServerControlOutcome.RequiresElevation"/> rather than a
/// bare failure, so the caller can offer to relaunch elevated instead of showing an
/// "access denied" the user cannot act on.
///
/// Windows only. <see cref="ServerLifecycleControllerFactory"/> never selects this mode elsewhere.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceLifecycleController : IServerLifecycleController
{
    /// <summary>How long to wait for the service to reach the requested state.</summary>
    private static readonly TimeSpan StateChangeTimeout = TimeSpan.FromSeconds(30);

    private readonly string _serviceName;

    /// <summary>Creates the controller.</summary>
    /// <param name="serviceName">Name the service was registered under.</param>
    public WindowsServiceLifecycleController(string serviceName) => _serviceName = serviceName;

    /// <inheritdoc/>
    public bool SupportsLifecycleControl => true;

    /// <inheritdoc/>
    public string Description => $"Windows service '{_serviceName}'";

    /// <inheritdoc/>
    public Task<ServerControlResult> StartAsync(CancellationToken ct = default) =>
        Task.Run(() => Control(start: true), ct);

    /// <inheritdoc/>
    public Task<ServerControlResult> StopAsync(CancellationToken ct = default) =>
        Task.Run(() => Control(start: false), ct);

    /// <summary>Starts or stops the service and waits for it to settle.</summary>
    /// <param name="start">True to start, false to stop.</param>
    /// <returns>What happened.</returns>
    private ServerControlResult Control(bool start)
    {
        try
        {
            using ServiceController service = new ServiceController(_serviceName);

            ServiceControllerStatus target = start
                ? ServiceControllerStatus.Running
                : ServiceControllerStatus.Stopped;

            if (service.Status == target)
            {
                return ServerControlResult.Ok(
                    $"Service '{_serviceName}' is already {(start ? "running" : "stopped")}.");
            }

            if (start) service.Start();
            else service.Stop();

            service.WaitForStatus(target, StateChangeTimeout);

            return ServerControlResult.Ok(
                $"Service '{_serviceName}' {(start ? "started" : "stopped")}.");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception win32)
        {
            // 5 is ERROR_ACCESS_DENIED; 1060 is ERROR_SERVICE_DOES_NOT_EXIST. Everything else is
            // reported as-is rather than guessed at.
            return win32.NativeErrorCode switch
            {
                5 => ServerControlResult.Fail(ServerControlOutcome.RequiresElevation,
                    $"Controlling service '{_serviceName}' needs administrator rights. " +
                    "Run RetroAdmin as administrator, or start the service from Services.msc."),

                1060 => ServerControlResult.Fail(ServerControlOutcome.Failed,
                    $"No service named '{_serviceName}' is installed. " +
                    "Register it first with: RetroServer.exe --install"),

                _ => ServerControlResult.Fail(ServerControlOutcome.Failed,
                    $"Service '{_serviceName}': {win32.Message}")
            };
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return ServerControlResult.Fail(ServerControlOutcome.Failed,
                $"Service '{_serviceName}' did not {(start ? "start" : "stop")} within " +
                $"{StateChangeTimeout.TotalSeconds:0} seconds.");
        }
        catch (InvalidOperationException ex)
        {
            return ServerControlResult.Fail(ServerControlOutcome.Failed,
                $"Service '{_serviceName}': {ex.Message}");
        }
    }
}
