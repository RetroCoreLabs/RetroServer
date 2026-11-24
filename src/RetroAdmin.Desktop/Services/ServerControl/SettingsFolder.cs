//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.IO;

namespace RetroAdmin.Desktop.Services.ServerControl;

/// <summary>
/// Where RetroAdmin keeps its own settings files: %APPDATA%\RetroAdmin.
/// </summary>
/// <remarks>
/// The program used to be called RetroServe and kept them in %APPDATA%\RetroServe. Renamed
/// 2026-11-24. A file that exists only in the old folder is copied to the new one the first time it
/// is asked for, so the saved server address and theme are kept; the old folder is left as it was.
/// </remarks>
public static class SettingsFolder
{
    /// <summary>The folder name under %APPDATA% before 2026-11-24.</summary>
    public const string OldName = "RetroServe";

    /// <summary>The folder name under %APPDATA%.</summary>
    public const string Name = "RetroAdmin";

    /// <summary>The full path of one settings file, copied over from the old folder if only that has it.</summary>
    /// <param name="fileName">The file name, for example ui-settings.json.</param>
    /// <returns>The path in %APPDATA%\RetroAdmin.</returns>
    public static string PathOf(string fileName)
    {
        return PathOf(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), fileName);
    }

    /// <summary>The same, under a given folder instead of %APPDATA% (for the tests).</summary>
    /// <param name="appData">The folder that holds the RetroAdmin and RetroServe folders.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The path in appData\RetroAdmin.</returns>
    public static string PathOf(string appData, string fileName)
    {
        string file = Path.Combine(appData, Name, fileName);
        string old = Path.Combine(appData, OldName, fileName);
        try
        {
            if (!File.Exists(file) && File.Exists(old))
            {
                Directory.CreateDirectory(Path.Combine(appData, Name));
                File.Copy(old, file);
            }
        }
        catch (IOException)
        {
            // Not copied: the program starts with defaults, the same as when a file is unreadable.
        }
        catch (UnauthorizedAccessException)
        {
            // The same.
        }

        return file;
    }
}
