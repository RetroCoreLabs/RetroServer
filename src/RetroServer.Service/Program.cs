//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen 
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.Econet.FileServer.Users;
using RetroIO.Protocols.Econet.Server;
using RetroIO.Protocols.Tnfs.Server;
using RetroIO.Server;
using RetroIO.Server.Hosting;
using RetroIO.Server.Hosting.Configuration;
using RetroIO.Storage.Utilities;

namespace RetroServer.Service;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (WantsHelp(args))
        {
            PrintHelp();
            return 0;
        }

        if (TryGetServiceCommand(args, out string command))
        {
            return WindowsServiceInstaller.Run(command);
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // No-op when launched from a console; wires up the Service Control Manager lifetime when
        // the SCM started us. One binary, both roles, decided at runtime -- there is no flag and
        // no second executable.
        builder.Host.UseWindowsService();

        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        string[] managementUrls = ResolveManagementUrls(builder.Configuration);
        builder.WebHost.UseUrls(managementUrls);

        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        // Banners write to the console, which a service does not have.
        RetroServerOptions options = new RetroServerOptions
        {
            PrintBanners = !WindowsServiceHelpers.IsWindowsService()
        };

        builder.Services.AddRetroServer(builder.Configuration, builder.Logging, options);

        WebApplication app = builder.Build();

        // Authentication and authorization, before anything is dispatched.
        app.UseRetroServer(options);

        app.MapRetroServer(options);

        await RetroIdpBootstrap.InitializeAsync(app.Services);
        await EconetUserBootstrap.EnsureProfilesAsync(app.Services.GetRequiredService<RetroIO.Identity.IRetroIDP>());

        if (options.PrintBanners)
        {
            ConsoleBox.Top();
            ConsoleBox.Title("RetroFS Management API");
            ConsoleBox.Divider();
            for (int i = 0; i < managementUrls.Length; i++)
            {
                ConsoleBox.LabelValue($"  URL {i + 1}:             ", managementUrls[i]);
            }

            ConsoleBox.LabelValue("  Swagger UI:        ", managementUrls[^1] + "/swagger");
            ConsoleBox.Bottom();
        }

        await app.RunAsync();
        return 0;
    }

    /// <summary>Addresses the embedded management API listens on by default.</summary>
    private static readonly string[] DefaultManagementApiUrls =
    {
        "http://localhost:5000",
        "https://localhost:5001"
    };

    /// <summary>Configuration key that overrides those addresses.</summary>
    /// <remarks>
    /// Pass <c>--Management:Urls=http://localhost:5100;https://localhost:5101</c> to move the
    /// management API off the standard ports. Needed because they were fixed constants: a second
    /// RetroServer could not run alongside the first, which made any test that spawns one
    /// intermittently fail against a developer's own running instance.
    /// </remarks>
    private const string ManagementUrlsKey = "Management:Urls";

    /// <summary>Resolves the addresses to bind, honouring the configuration override.</summary>
    /// <param name="configuration">Host configuration.</param>
    /// <returns>Addresses to bind.</returns>
    private static string[] ResolveManagementUrls(IConfiguration configuration)
    {
        string? configured = configuration[ManagementUrlsKey];

        if (string.IsNullOrWhiteSpace(configured)) return DefaultManagementApiUrls;

        return configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Recognises the service registration switches.
    /// </summary>
    /// <param name="args">Raw command line arguments.</param>
    /// <param name="command">Set to "install" or "uninstall" when one was given.</param>
    /// <returns>True when the command line asks to register or remove the service.</returns>
    private static bool TryGetServiceCommand(string[] args, out string command)
    {
        command = string.Empty;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--install", StringComparison.OrdinalIgnoreCase))
            {
                command = "install";
                return true;
            }

            if (string.Equals(args[i], "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                command = "uninstall";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the command line asks for usage text rather than a server run.
    /// </summary>
    /// <param name="args">Raw command line arguments.</param>
    /// <returns>True if usage was requested.</returns>
    /// <remarks>
    /// Accepts the three spellings people actually type. Checked before the host is built so
    /// --help never has to start listeners, touch the config files or create the data directory
    /// just to print text.
    /// </remarks>
    private static bool WantsHelp(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/?", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes the usage text.
    /// </summary>
    /// <remarks>
    /// Every switch listed here is one the server really reads. The Tnfs:, Econet: and DriveWire:
    /// keys are the exact set honoured by ProtocolConfigurationLoader.ApplyTnfsOverrides,
    /// ApplyEconetOverrides and ApplyDriveWireOverrides -- if a key is added there, add it here
    /// too, or the help lies. Unlike the other two, the DriveWire keys are not written back:
    /// drivewire-config.json is written only when it does not exist yet.
    /// </remarks>
    private static void PrintHelp()
    {
        Console.WriteLine("RetroServer -- TNFS, Econet and DriveWire server for retro computers.");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine("  RetroServer [options]");
        Console.WriteLine();
        Console.WriteLine("  Run with no options to serve on the saved configuration. Settings are read");
        Console.WriteLine("  from tnfs-config.json and econet-config.json in the data directory, and any");
        Console.WriteLine("  option below overrides the saved value AND is written back to those files.");
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  --help, -h, /?                  Show this text and exit.");
        Console.WriteLine();
        Console.WriteLine("  --Tnfs:RootDirectory=<path>     Directory served over TNFS.");
        Console.WriteLine("  --Tnfs:UdpPort=<port>           TNFS UDP port.               (default 16384)");
        Console.WriteLine("  --Tnfs:TcpPort=<port>           TNFS TCP port.               (default 16384)");
        Console.WriteLine();
        Console.WriteLine("  --Econet:RootDirectory=<path>   Directory served over Econet.");
        Console.WriteLine("  --Econet:UdpPort=<port>         Econet AUN UDP port.         (default 32768)");
        Console.WriteLine();
        Console.WriteLine("  DriveWire (CoCo and Dragon disks over TCP, as an emulator's Becker port connects).");
        Console.WriteLine("  Read from drivewire-config.json; these override it for this run only (the file");
        Console.WriteLine("  is written only when it does not exist yet).");
        Console.WriteLine("  --DriveWire:Enabled=<true|false>                   Run the DriveWire server. (default true)");
        Console.WriteLine("  --DriveWire:Port=<port>                            TCP port.            (default 65504)");
        Console.WriteLine("  --DriveWire:BindAddress=<address>                  Address to listen on. (default 127.0.0.1)");
        Console.WriteLine("  --DriveWire:TransactionTimeoutMilliseconds=<ms>    Byte time limit inside one request. (default 250)");
        Console.WriteLine("  --DriveWire:Drives:<n>:Path=<image>                Disk image served as drive n (0-255).");
        Console.WriteLine("  --DriveWire:Drives:<n>:ReadOnly=<true|false>       Refuse writes to drive n. (default false)");
        Console.WriteLine();
        Console.WriteLine("ENVIRONMENT");
        Console.WriteLine($"  {PathHelper.DataPathEnvironmentVariable,-30}  Overrides the data directory holding the");
        Console.WriteLine($"  {string.Empty,-30}  configuration, identity store and logs.");
        Console.WriteLine();
        Console.WriteLine("DATA DIRECTORY");
        Console.WriteLine($"  {PathHelper.GetRetroFSPath()}");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES");
        Console.WriteLine("  RetroServer");
        Console.WriteLine("  RetroServer --Tnfs:UdpPort=16385 --Tnfs:RootDirectory=D:\\disks");
        Console.WriteLine("  RetroServer --Econet:UdpPort=32768");
        Console.WriteLine("  RetroServer --DriveWire:Drives:0:Path=D:\\disks\\NOS9_6809_L1_v030300_coco1_becker.dsk");
    }
}
