//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RetroAdmin.Desktop.Services.ServerControl;

/// <summary>
/// A server on another machine: reachable over the management API, but not ours to start or stop.
/// </summary>
public sealed class RemoteServerLifecycleController : IServerLifecycleController
{
    private readonly string _address;

    /// <summary>Creates the controller.</summary>
    /// <param name="address">Management API address.</param>
    public RemoteServerLifecycleController(string address) => _address = address;

    /// <inheritdoc/>
    public bool SupportsLifecycleControl => false;

    /// <inheritdoc/>
    public string Description => $"Remote server at {_address}";

    /// <inheritdoc/>
    public Task<ServerControlResult> StartAsync(CancellationToken ct = default) =>
        Task.FromResult(ServerControlResult.Fail(ServerControlOutcome.NotSupported,
            $"{_address} runs on another machine. Start it there."));

    /// <inheritdoc/>
    public Task<ServerControlResult> StopAsync(CancellationToken ct = default) =>
        Task.FromResult(ServerControlResult.Fail(ServerControlOutcome.NotSupported,
            $"{_address} runs on another machine. Stop it there."));
}

/// <summary>
/// Runs RetroServer.exe as a child process for as long as the UI is open.
/// </summary>
/// <remarks>
/// This is what keeps the double-click experience after the UI stopped hosting the protocol
/// servers itself. The child is killed when this object is disposed, so closing the UI does not
/// strand a server holding the TNFS and Econet ports.
/// </remarks>
public sealed class SubprocessServerLifecycleController : IServerLifecycleController, IDisposable
{
    /// <summary>Executable started, expected beside the UI.</summary>
    private const string ServerExecutable = "RetroServer.exe";

    private readonly string _exePath;
    private Process? _process;

    /// <summary>Creates the controller, resolving the server executable beside the UI.</summary>
    /// <param name="exePath">
    /// Explicit path to RetroServer.exe, or null to look in the UI's own directory.
    /// </param>
    public SubprocessServerLifecycleController(string? exePath = null)
    {
        _exePath = exePath ?? Path.Combine(AppContext.BaseDirectory, ServerExecutable);
    }

    /// <inheritdoc/>
    public bool SupportsLifecycleControl => true;

    /// <inheritdoc/>
    public string Description => $"Local server ({ServerExecutable})";

    /// <summary>True while the child process exists and has not exited.</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <inheritdoc/>
    public Task<ServerControlResult> StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return Task.FromResult(ServerControlResult.Ok("Server is already running."));

        if (!File.Exists(_exePath))
        {
            // Deploying the UI without the server beside it is a packaging mistake, and the
            // message has to say where it looked or it is unactionable.
            return Task.FromResult(ServerControlResult.Fail(ServerControlOutcome.Failed,
                $"{ServerExecutable} was not found at {_exePath}. It must be deployed beside the UI."));
        }

        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _exePath,
                    WorkingDirectory = Path.GetDirectoryName(_exePath)!,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _process.Start();
            return Task.FromResult(ServerControlResult.Ok($"Started {ServerExecutable} (pid {_process.Id})."));
        }
        catch (Win32Exception ex)
        {
            _process = null;
            return Task.FromResult(ServerControlResult.Fail(ServerControlOutcome.Failed,
                $"Could not start {ServerExecutable}: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<ServerControlResult> StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning)
            return ServerControlResult.Ok("Server is not running.");

        Process process = _process!;

        try
        {
            // Kill the whole tree: the server may have spawned children of its own, and leaving
            // one behind would keep the TNFS and Econet ports bound.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return ServerControlResult.Ok("Server stopped.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // InvalidOperationException means it exited between the check and the kill, which is
            // the outcome we wanted anyway.
            return ServerControlResult.Ok("Server stopped.");
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    /// <summary>Kills the child process so closing the UI does not strand a server.</summary>
    public void Dispose()
    {
        if (!IsRunning) return;

        try
        {
            _process!.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Shutting down; nothing useful left to do about it.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }
}
