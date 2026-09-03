//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;

namespace RetroAdmin.Desktop.Services.ServerControl;

/// <summary>
/// Picks the lifecycle controller matching the configured connection mode.
/// </summary>
/// <remarks>
/// The single place that knows which mode maps to which implementation. Everything else in the UI
/// holds an <see cref="IServerLifecycleController"/> and never asks which one it got.
/// </remarks>
public static class ServerLifecycleControllerFactory
{
    /// <summary>Builds the controller for the given settings.</summary>
    /// <param name="settings">Connection settings.</param>
    /// <returns>A controller for that mode.</returns>
    public static IServerLifecycleController Create(ServerConnectionSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        switch (settings.Mode)
        {
            case ServerConnectionMode.Remote:
                return new RemoteServerLifecycleController(settings.Address);

            case ServerConnectionMode.LocalService:
                // Service control is Windows-only. Elsewhere the honest answer is that this mode
                // cannot work, and saying so beats throwing PlatformNotSupportedException out of
                // a button handler.
                if (!OperatingSystem.IsWindows())
                {
                    return new RemoteServerLifecycleController(settings.Address);
                }

                return new WindowsServiceLifecycleController(settings.ServiceName);

            case ServerConnectionMode.LocalSubprocess:
            default:
                return new SubprocessServerLifecycleController();
        }
    }
}
