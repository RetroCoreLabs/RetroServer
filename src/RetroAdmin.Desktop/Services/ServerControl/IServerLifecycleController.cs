//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace RetroAdmin.Desktop.Services.ServerControl;

/// <summary>Why a start or stop attempt did not happen.</summary>
public enum ServerControlOutcome
{
    /// <summary>The server was started or stopped.</summary>
    Succeeded = 0,

    /// <summary>This mode cannot control the server's lifetime. Remote servers are not ours to start.</summary>
    NotSupported = 1,

    /// <summary>The operation needs administrator rights the UI does not have.</summary>
    RequiresElevation = 2,

    /// <summary>Something else went wrong; see the message.</summary>
    Failed = 3
}

/// <summary>Result of a start or stop attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">Text safe to show the user.</param>
public readonly record struct ServerControlResult(ServerControlOutcome Outcome, string Message)
{
    /// <summary>True when the operation did what was asked.</summary>
    public bool Succeeded => Outcome == ServerControlOutcome.Succeeded;

    /// <summary>Builds a success result.</summary>
    /// <param name="message">Text to show.</param>
    /// <returns>The result.</returns>
    public static ServerControlResult Ok(string message) =>
        new ServerControlResult(ServerControlOutcome.Succeeded, message);

    /// <summary>Builds a failure result.</summary>
    /// <param name="outcome">Why it failed.</param>
    /// <param name="message">Text to show.</param>
    /// <returns>The result.</returns>
    public static ServerControlResult Fail(ServerControlOutcome outcome, string message) =>
        new ServerControlResult(outcome, message);
}

/// <summary>
/// Starts and stops the RetroFS server the UI is pointed at, whichever way it is hosted.
/// </summary>
/// <remarks>
/// The UI asks this to start or stop and never learns which of the three modes it got. That is
/// the point: without it, "start the server" would mean three different things in three places,
/// and the remote case (where the answer is "you cannot") would have to be special-cased at every
/// call site instead of once here.
///
/// Reading state is deliberately NOT on this interface. Whether the server is running, and every
/// statistic about it, comes back over the management API the same way for all three modes, so
/// there is one code path for status and one for lifetime.
/// </remarks>
public interface IServerLifecycleController
{
    /// <summary>Whether this mode can start and stop the server at all.</summary>
    /// <remarks>
    /// Bind the start and stop buttons' enabled state to this. Offering a button that always
    /// fails is worse than not offering it.
    /// </remarks>
    bool SupportsLifecycleControl { get; }

    /// <summary>Human-readable description of what is being controlled, for the status bar.</summary>
    string Description { get; }

    /// <summary>Starts the server.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<ServerControlResult> StartAsync(CancellationToken ct = default);

    /// <summary>Stops the server.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<ServerControlResult> StopAsync(CancellationToken ct = default);
}
