//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroAdmin.Desktop.Services.ServerControl;

/// <summary>How the UI reaches, and optionally controls, a RetroFS server.</summary>
public enum ServerConnectionMode
{
    /// <summary>
    /// The UI starts RetroServer.exe as a child process and kills it when the UI exits.
    /// </summary>
    /// <remarks>
    /// The default, because it preserves the behaviour people expect: double-click the UI and a
    /// server is running. It replaces the old arrangement where the UI hosted the protocol servers
    /// in its own process, which meant the UI was a third, independent copy of the server rather
    /// than a client of one.
    /// </remarks>
    LocalSubprocess = 0,

    /// <summary>A RetroServer installed as a Windows service, started and stopped through the SCM.</summary>
    LocalService = 1,

    /// <summary>A server on another machine. Reachable, but its lifetime is not ours to control.</summary>
    Remote = 2
}

/// <summary>
/// Which server the UI talks to and how, persisted next to the other UI preferences.
/// </summary>
public sealed class ServerConnectionSettings
{
    /// <summary>Default management API address, matching the addresses RetroServer binds.</summary>
    public const string DefaultAddress = "https://localhost:5001";

    /// <summary>Service name used by <c>RetroServer.exe --install</c>.</summary>
    public const string DefaultServiceName = "RetroFSServer";

    /// <summary>How the UI reaches the server.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServerConnectionMode Mode { get; set; } = ServerConnectionMode.LocalSubprocess;

    /// <summary>Root address of the management API.</summary>
    public string Address { get; set; } = DefaultAddress;

    /// <summary>Windows service name, used only in <see cref="ServerConnectionMode.LocalService"/>.</summary>
    public string ServiceName { get; set; } = DefaultServiceName;

    /// <summary>
    /// Whether to accept any TLS certificate from the server.
    /// </summary>
    /// <remarks>
    /// Off by default. RetroServer's HTTPS endpoint uses the ASP.NET Core development certificate,
    /// which a machine that has not run <c>dotnet dev-certs https --trust</c> will reject, so a
    /// local install may need this on. Leaving it on for a REMOTE server would defeat the point of
    /// using HTTPS at all, which is why it is a setting and not the built-in behaviour it used to be.
    /// </remarks>
    public bool AllowUntrustedCertificate { get; set; }

    /// <summary>True when this mode can start and stop the server.</summary>
    /// <remarks>
    /// A remote server's lifetime belongs to the machine it runs on. The UI must disable its
    /// start and stop buttons rather than offer them and fail.
    /// </remarks>
    [JsonIgnore]
    public bool SupportsLifecycleControl => Mode != ServerConnectionMode.Remote;

    // ------------------------------------------------------------------

    private static ServerConnectionSettings? _instance;

    /// <summary>The process-wide settings, loaded from disk on first access.</summary>
    public static ServerConnectionSettings Instance => _instance ??= Load();

    /// <summary>Full path of the settings file.</summary>
    /// <returns>Path to server-connection.json.</returns>
    public static string GetSettingsPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RetroServe");
        return Path.Combine(dir, "server-connection.json");
    }

    /// <summary>Reads the settings, falling back to defaults if anything is wrong with the file.</summary>
    /// <returns>The loaded settings, or defaults.</returns>
    private static ServerConnectionSettings Load()
    {
        try
        {
            string file = GetSettingsPath();

            if (File.Exists(file))
            {
                ServerConnectionSettings? loaded =
                    JsonSerializer.Deserialize<ServerConnectionSettings>(File.ReadAllText(file));

                if (loaded != null && !string.IsNullOrWhiteSpace(loaded.Address)) return loaded;
            }
        }
        catch (Exception)
        {
            // Same rule as the theme settings: a corrupt or unreadable file must never stop the
            // application starting. Defaults are always usable.
        }

        return new ServerConnectionSettings();
    }

    /// <summary>Writes the settings to disk.</summary>
    public void Save()
    {
        try
        {
            string file = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            File.WriteAllText(file,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Failing to persist a preference is not worth taking the application down for.
        }
    }
}
