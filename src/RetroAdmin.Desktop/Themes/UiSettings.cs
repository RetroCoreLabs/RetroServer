//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs - https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.IO;
using System.Text.Json;

namespace RetroAdmin.Desktop.Themes
{
    /// <summary>
    /// Persisted UI preferences for RetroAdmin, stored as a single JSON document at
    /// %APPDATA%\RetroAdmin\ui-settings.json (copied from the old %APPDATA%\RetroServe once, see SettingsFolder).
    /// </summary>
    /// <remarks>
    /// Deliberately the same shape and the same load/save behaviour as RetroCommander's, so the
    /// two apps behave alike; only the folder name differs, because a shared file would mean one
    /// app's theme choice silently changed the other's.
    /// </remarks>
    public sealed class UiSettings
    {
        /// <summary>
        /// Selected theme id, matching one of ThemeManager.Themes. Null means use the default.
        /// </summary>
        public string? Theme { get; set; }

        // ------------------------------------------------------------------

        private static UiSettings? _instance;

        /// <summary>The process-wide settings instance, loaded from disk on first access.</summary>
        public static UiSettings Instance => _instance ??= Load();

        private static string GetSettingsPath()
        {
            return RetroAdmin.Desktop.Services.ServerControl.SettingsFolder.PathOf("ui-settings.json");
        }

        private static UiSettings Load()
        {
            try
            {
                string file = GetSettingsPath();
                if (File.Exists(file))
                {
                    string json = File.ReadAllText(file);
                    UiSettings? loaded = JsonSerializer.Deserialize<UiSettings>(json);
                    if (loaded != null)
                        return loaded;
                }
            }
            catch (Exception)
            {
                // A corrupt or unreadable settings file must never break startup.
            }
            return new UiSettings();
        }

        /// <summary>Writes the current settings to disk. Best effort; IO failures are ignored.</summary>
        public void Save()
        {
            try
            {
                string file = GetSettingsPath();
                string? dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(file, json);
            }
            catch (Exception)
            {
                // Persisting a preference is best effort; ignore IO and permission failures.
            }
        }
    }
}
