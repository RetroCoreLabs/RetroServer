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
    /// Every switch listed here is one the server really reads. The Tnfs:, Econet:, DriveWire: and
    /// XmsgHub: keys are the exact set honoured by ProtocolConfigurationLoader.ApplyTnfsOverrides,
    /// ApplyEconetOverrides, ApplyDriveWireOverrides and ApplyXmsgHubOverrides, and the XmsgNode: keys
    /// those of ApplyXmsgNodeOverrides -- if a key is added
    /// there, add it here too, or the help lies. No key is written back into an existing file: each
    /// file is written only when it does not exist yet, and then with the keys given included. Until
    /// 2026-11-10 this text said the Tnfs and Econet keys were written back; the loader never did that.
    /// </remarks>
    private static void PrintHelp()
    {
        Console.WriteLine("RetroServer -- TNFS, Econet, DriveWire and XMSG Ethernet hub server for retro computers.");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine("  RetroServer [options]");
        Console.WriteLine();
        Console.WriteLine("  Run with no options to serve on the saved configuration. Settings are read");
        Console.WriteLine("  from the *-config.json files in the data directory (RetroAdmin edits them).");
        Console.WriteLine("  An option below overrides the saved value for this run only. It is written");
        Console.WriteLine("  into a file only when that file does not exist yet and is made now.");
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
        Console.WriteLine("  XMSG Ethernet hub (a virtual Ethernet segment ND machines join over TCP, in RetroCore");
        Console.WriteLine("  with: device add ETH 0 --net=tcp:HOST:PORT). Read from xmsghub-config.json; these");
        Console.WriteLine("  override it for this run only (the file is written only when it does not exist yet).");
        Console.WriteLine("  --XmsgHub:Enabled=<true|false>              Run the hub.                 (default false)");
        Console.WriteLine("  --XmsgHub:Port=<port>                       TCP port machines join on.   (default 5010)");
        Console.WriteLine("  --XmsgHub:BindAddress=<address>             Address to listen on.        (default 127.0.0.1)");
        Console.WriteLine("  --XmsgHub:Uplink=<host:port>                Also join another hub.       (default none)");
        Console.WriteLine("  --XmsgHub:CaptureFile=<file.pcap>           Store every frame in a pcap. (default none)");
        Console.WriteLine("  --XmsgHub:FrameBufferSize=<n>               Recent frames kept for the admin UI. (default 4096)");
        Console.WriteLine("  --XmsgHub:LogBufferSize=<n>                 Recent log lines kept.       (default 1000)");
        Console.WriteLine("  --XmsgHub:StatusIntervalSeconds=<s>         Seconds between status lines. (default 10)");
        Console.WriteLine();
        Console.WriteLine("  Dial-up ISP (an emulated machine dials in with its modem: ATDT host:port. Each call gets");
        Console.WriteLine("  a text login; only users with the dial-up right get in, then SLIP onto one subnet with a");
        Console.WriteLine("  NAT to the internet; .2 on the subnet reaches this computer). Read from dialup-config.json;");
        Console.WriteLine("  these override it for this run only.");
        Console.WriteLine("  --DialUp:Enabled=<true|false>               Run the dial-up ISP.         (default false)");
        Console.WriteLine("  --DialUp:Port=<port>                        TCP port the modem dials.    (default 6400)");
        Console.WriteLine("  --DialUp:BindAddress=<address>              Address to listen on.        (default 127.0.0.1)");
        Console.WriteLine("  --DialUp:MaxCalls=<n>                       Most calls at once.          (default 32)");
        Console.WriteLine("  --DialUp:Network=<a.b.c.d>                  The callers' subnet.         (default 10.42.0.0)");
        Console.WriteLine("  --DialUp:PrefixLength=<n>                   Its prefix length, 8 to 29.  (default 24)");
        Console.WriteLine("  --DialUp:Greeting=<text>                    Shown before the login.      (default RetroServer ISP)");
        Console.WriteLine();
        Console.WriteLine("  XMSG node (RetroServer as an XMSG machine: it joins the hub, and client and server");
        Console.WriteLine("  programs such as retro-chat and retro-chat-server connect to it). Read from");
        Console.WriteLine("  xmsgnode-config.json; these override it for this run only.");
        Console.WriteLine("  --XmsgNode:Enabled=<true|false>             Run the node.                (default false)");
        Console.WriteLine("  --XmsgNode:SystemNumber=<n>                 RetroServer's system number. (required)");
        Console.WriteLine("  --XmsgNode:PeerSystemNumber=<n>             The one machine it talks to. (required)");
        Console.WriteLine("  --XmsgNode:Ethernet=<spec>                  Segment to join.             (default tcp:127.0.0.1:5010)");
        Console.WriteLine("  --XmsgNode:ProgramBindAddress=<address>     Address programs connect to. (default 127.0.0.1)");
        Console.WriteLine("  --XmsgNode:ProgramPort=<port>               Port programs connect to.    (default 5011)");
        Console.WriteLine("  --XmsgNode:ProgramToken=<secret>            Secret programs must send.   (default none = insecure)");
        Console.WriteLine("  --XmsgNode:ProgramLogMinimum=<level>        Lowest program log level kept. (default Information)");
        Console.WriteLine("  --XmsgNode:Motd=<text>                      Middle line of the TAD login banner.");
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
        Console.WriteLine("  RetroServer --XmsgHub:Enabled=true --XmsgHub:CaptureFile=run.pcap");
        Console.WriteLine("  RetroServer --XmsgHub:Enabled=true --XmsgNode:Enabled=true --XmsgNode:SystemNumber=19998 --XmsgNode:PeerSystemNumber=102");
    }
}
