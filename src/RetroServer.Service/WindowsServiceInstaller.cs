//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Diagnostics;

namespace RetroServer.Service;

/// <summary>
/// Registers and removes the RetroServer Windows service.
/// </summary>
/// <remarks>
/// This is a convenience wrapper over <c>sc.exe</c>, nothing more, so that nobody has to remember
/// that <c>binPath=</c> needs a space after the equals sign and that the path must be quoted
/// inside an already-quoted argument. RUNNING as a service needs none of this: the same
/// executable detects the Service Control Manager by itself. Only registration needs a command.
///
/// Both commands need an elevated prompt. Without one, sc.exe returns error 5 (access denied)
/// and we report that rather than pretending it worked.
/// </remarks>
public static class WindowsServiceInstaller
{
    /// <summary>Name the service is registered under.</summary>
    public const string ServiceName = "RetroServer";

    /// <summary>Name shown in the Services console.</summary>
    private const string DisplayName = "RetroFS Server";

    /// <summary>Description shown in the Services console.</summary>
    private const string Description =
        "Serves retro computers over TNFS and Econet, and hosts the RetroFS management API.";

    /// <summary>
    /// Runs an install or uninstall command.
    /// </summary>
    /// <param name="command">Either "install" or "uninstall".</param>
    /// <returns>Process exit code: 0 on success.</returns>
    public static int Run(string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("--install and --uninstall are Windows-only.");
            Console.Error.WriteLine("On Linux or macOS, write a systemd unit or a launchd plist that runs this executable.");
            return 1;
        }

        string exePath = Environment.ProcessPath ?? string.Empty;

        if (string.IsNullOrEmpty(exePath))
        {
            Console.Error.WriteLine("Could not determine this executable's own path.");
            return 1;
        }

        return string.Equals(command, "install", StringComparison.OrdinalIgnoreCase)
            ? Install(exePath)
            : Uninstall();
    }

    /// <summary>
    /// Registers the service and sets it to start automatically.
    /// </summary>
    /// <param name="exePath">Full path to this executable.</param>
    /// <returns>Process exit code.</returns>
    private static int Install(string exePath)
    {
        // sc.exe is picky: the space after binPath= is required, and the path itself has to be
        // quoted because it usually contains spaces.
        int created = RunSc($"create {ServiceName} binPath= \"\\\"{exePath}\\\"\" start= auto DisplayName= \"{DisplayName}\"");

        if (created != 0)
        {
            if (created == 5)
            {
                Console.Error.WriteLine("Access denied. Run this from an elevated command prompt.");
            }

            return created;
        }

        RunSc($"description {ServiceName} \"{Description}\"");

        Console.WriteLine($"Service '{ServiceName}' registered.");
        Console.WriteLine($"  Start it with:  sc start {ServiceName}");
        Console.WriteLine($"  Remove it with: \"{exePath}\" --uninstall");
        return 0;
    }

    /// <summary>
    /// Stops the service if it is running, then removes the registration.
    /// </summary>
    /// <returns>Process exit code.</returns>
    private static int Uninstall()
    {
        // Stopping first is best effort: a service that is already stopped makes sc return a
        // non-zero code, and that is not a failure of the uninstall.
        RunSc($"stop {ServiceName}");

        int deleted = RunSc($"delete {ServiceName}");

        if (deleted != 0)
        {
            if (deleted == 5)
            {
                Console.Error.WriteLine("Access denied. Run this from an elevated command prompt.");
            }

            return deleted;
        }

        Console.WriteLine($"Service '{ServiceName}' removed.");
        return 0;
    }

    /// <summary>
    /// Runs sc.exe with the given arguments and returns its exit code.
    /// </summary>
    /// <param name="arguments">Arguments passed to sc.exe.</param>
    /// <returns>sc.exe's exit code, or 1 if it could not be started.</returns>
    private static int RunSc(string arguments)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false
        };

        using Process? process = Process.Start(startInfo);

        if (process == null)
        {
            Console.Error.WriteLine("Could not start sc.exe.");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }
}
