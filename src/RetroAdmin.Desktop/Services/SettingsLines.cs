//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.Services;

/// <summary>
/// The list boxes on the settings pages: one entry per line, written and read back here, so every page and its tests
/// agree on the shape. Each reader returns null when every line was understood, else what is wrong and on which line.
/// </summary>
/// <remarks>
/// Shapes:
/// <list type="bullet">
/// <item><description>Econet reply format per machine: <c>1.100 = Classic1983</c></description></item>
/// <item><description>Econet disc: <c>0 | Games | native:/discs/games | read-only, folder=0Games</c>; the storage is
/// <c>native:</c> or <c>tnfs:</c> followed by its address, or <c>mount:</c> followed by a volume id</description></item>
/// <item><description>Mounted volume: <c>work | tnfs | tnfs://host/work | read-only, fs=NDFS, user=bob, password=x</c>;
/// <c>password=(kept)</c> keeps the stored one, no password item removes it</description></item>
/// <item><description>Log level: <c>Microsoft = Warning</c></description></item>
/// </list>
/// </remarks>
public static class SettingsLines
{
    /// <summary>What a stored password is shown as; left in place, the password is kept.</summary>
    public const string KeptPassword = "(kept)";

    private static string[] Lines(string text)
    {
        return (text ?? string.Empty).Split('\n');
    }

    private static string Join(List<string> lines)
    {
        return string.Join(Environment.NewLine, lines);
    }

    // ---------------- Econet reply format per machine ----------------

    /// <summary>Writes the per-machine reply formats, one per line.</summary>
    /// <param name="overrides">The machines.</param>
    /// <returns>The box text.</returns>
    public static string FormatOverrides(List<EconetWireProfileOverrideDto> overrides)
    {
        List<string> lines = new List<string>();
        for (int i = 0; i < overrides.Count; i++)
        {
            lines.Add(overrides[i].Net.ToString(CultureInfo.InvariantCulture) + "." + overrides[i].Station.ToString(CultureInfo.InvariantCulture)
                + " = " + overrides[i].Profile);
        }

        return Join(lines);
    }

    /// <summary>Reads the per-machine reply format box.</summary>
    /// <param name="text">The box.</param>
    /// <param name="overrides">Receives the machines.</param>
    /// <returns>Null when good, else what is wrong.</returns>
    public static string? ParseOverrides(string text, List<EconetWireProfileOverrideDto> overrides)
    {
        string[] lines = Lines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int equals = line.IndexOf('=');
            string machine = equals < 0 ? string.Empty : line.Substring(0, equals).Trim();
            int dot = machine.IndexOf('.');
            if (equals < 0 || dot < 0
                || !byte.TryParse(machine.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out byte net)
                || !byte.TryParse(machine.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out byte station))
            {
                return $"Reply format line {i + 1} must look like \"1.100 = Classic1983\": {line}";
            }

            overrides.Add(new EconetWireProfileOverrideDto { Net = net, Station = station, Profile = line.Substring(equals + 1).Trim() });
        }

        return null;
    }

    // ---------------- Econet discs ----------------

    /// <summary>Writes the disc list, one disc per line.</summary>
    /// <param name="discs">The discs.</param>
    /// <returns>The box text.</returns>
    public static string FormatDiscs(List<EconetDiscDto> discs)
    {
        List<string> lines = new List<string>();
        for (int i = 0; i < discs.Count; i++)
        {
            EconetDiscDto d = discs[i];
            string where = string.IsNullOrEmpty(d.MountRef) ? d.Provider + ":" + d.Uri : "mount:" + d.MountRef;
            List<string> options = new List<string>();
            if (d.ReadOnly)
            {
                options.Add("read-only");
            }

            if (!string.IsNullOrEmpty(d.DirName))
            {
                options.Add("folder=" + d.DirName);
            }

            string line = d.Index.ToString(CultureInfo.InvariantCulture) + " | " + d.AcornName + " | " + where;
            if (options.Count > 0)
            {
                line += " | " + string.Join(", ", options);
            }

            lines.Add(line);
        }

        return Join(lines);
    }

    /// <summary>Reads the disc box.</summary>
    /// <param name="text">The box.</param>
    /// <param name="discs">Receives the discs.</param>
    /// <returns>Null when good, else what is wrong.</returns>
    public static string? ParseDiscs(string text, List<EconetDiscDto> discs)
    {
        string[] lines = Lines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split('|');
            if (parts.Length < 3 || parts.Length > 4
                || !int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                return $"Disc line {i + 1} must look like \"0 | Games | native:/discs/games | read-only\": {line}";
            }

            string where = parts[2].Trim();
            int colon = where.IndexOf(':');
            if (colon <= 0)
            {
                return $"Disc line {i + 1}: the storage must start with native:, tnfs: or mount: - {where}";
            }

            EconetDiscDto disc = new EconetDiscDto { Index = index, AcornName = parts[1].Trim() };
            string kind = where.Substring(0, colon).Trim().ToLowerInvariant();
            string address = where.Substring(colon + 1).Trim();
            if (kind == "mount")
            {
                disc.MountRef = address;
            }
            else
            {
                disc.Provider = kind;
                disc.Uri = address;
            }

            if (parts.Length == 4)
            {
                string[] options = parts[3].Split(',');
                for (int o = 0; o < options.Length; o++)
                {
                    string option = options[o].Trim();
                    if (option.Length == 0)
                    {
                        continue;
                    }

                    if (string.Equals(option, "read-only", StringComparison.OrdinalIgnoreCase))
                    {
                        disc.ReadOnly = true;
                    }
                    else if (option.StartsWith("folder=", StringComparison.OrdinalIgnoreCase))
                    {
                        disc.DirName = option.Substring("folder=".Length).Trim();
                    }
                    else
                    {
                        return $"Disc line {i + 1}: \"{option}\" is not an option (read-only, folder=NAME).";
                    }
                }
            }

            discs.Add(disc);
        }

        return null;
    }

    // ---------------- Mounted volumes ----------------

    /// <summary>Writes the mounted volumes, one per line. A stored password shows as <see cref="KeptPassword"/>.</summary>
    /// <param name="mounts">The volumes.</param>
    /// <returns>The box text.</returns>
    public static string FormatMounts(List<MountDto> mounts)
    {
        List<string> lines = new List<string>();
        for (int i = 0; i < mounts.Count; i++)
        {
            MountDto m = mounts[i];
            List<string> options = new List<string>();
            if (m.IsReadOnly)
            {
                options.Add("read-only");
            }

            if (!string.IsNullOrEmpty(m.FileSystemType))
            {
                options.Add("fs=" + m.FileSystemType);
            }

            if (!string.IsNullOrEmpty(m.Username))
            {
                options.Add("user=" + m.Username);
            }

            if (m.PasswordSet)
            {
                options.Add("password=" + KeptPassword);
            }

            string line = m.VolumeId + " | " + m.ProviderType + " | " + m.SourceUri;
            if (options.Count > 0)
            {
                line += " | " + string.Join(", ", options);
            }

            lines.Add(line);
        }

        return Join(lines);
    }

    /// <summary>Reads the mounted volumes box.</summary>
    /// <param name="text">The box.</param>
    /// <param name="mounts">Receives the volumes, with <see cref="MountDto.NewPassword"/> set for the server's rule.</param>
    /// <returns>Null when good, else what is wrong.</returns>
    /// <remarks>A password may not contain a comma or a '|', because those separate the line.</remarks>
    public static string? ParseMounts(string text, List<MountDto> mounts)
    {
        string[] lines = Lines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split('|');
            if (parts.Length < 3 || parts.Length > 4)
            {
                return $"Volume line {i + 1} must look like \"work | tnfs | tnfs://host/work | read-only\": {line}";
            }

            // No password item: remove any stored one.
            MountDto mount = new MountDto
            {
                VolumeId = parts[0].Trim(),
                ProviderType = parts[1].Trim(),
                SourceUri = parts[2].Trim(),
                NewPassword = string.Empty,
            };

            if (parts.Length == 4)
            {
                string[] options = parts[3].Split(',');
                for (int o = 0; o < options.Length; o++)
                {
                    string option = options[o].Trim();
                    if (option.Length == 0)
                    {
                        continue;
                    }

                    if (string.Equals(option, "read-only", StringComparison.OrdinalIgnoreCase))
                    {
                        mount.IsReadOnly = true;
                    }
                    else if (option.StartsWith("fs=", StringComparison.OrdinalIgnoreCase))
                    {
                        mount.FileSystemType = option.Substring(3).Trim();
                    }
                    else if (option.StartsWith("user=", StringComparison.OrdinalIgnoreCase))
                    {
                        mount.Username = option.Substring(5).Trim();
                    }
                    else if (option.StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                    {
                        string password = option.Substring(9);
                        mount.NewPassword = password == KeptPassword ? null : password;
                    }
                    else
                    {
                        return $"Volume line {i + 1}: \"{option}\" is not an option (read-only, fs=, user=, password=).";
                    }
                }
            }

            mounts.Add(mount);
        }

        return null;
    }

    // ---------------- Log levels ----------------

    /// <summary>Writes the log levels, one category per line.</summary>
    /// <param name="levels">Category and level.</param>
    /// <returns>The box text.</returns>
    public static string FormatLogLevels(Dictionary<string, string> levels)
    {
        StringBuilder text = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in levels)
        {
            if (text.Length > 0)
            {
                text.Append(Environment.NewLine);
            }

            text.Append(pair.Key).Append(" = ").Append(pair.Value);
        }

        return text.ToString();
    }

    /// <summary>Reads the log level box. The levels themselves are checked by the server.</summary>
    /// <param name="text">The box.</param>
    /// <param name="levels">Receives category and level.</param>
    /// <returns>Null when good, else what is wrong.</returns>
    public static string? ParseLogLevels(string text, Dictionary<string, string> levels)
    {
        string[] lines = Lines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                return $"Log level line {i + 1} must look like \"Microsoft = Warning\": {line}";
            }

            levels[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
        }

        return null;
    }
}
