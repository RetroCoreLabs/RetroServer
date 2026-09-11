//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs - https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace RetroAdmin.Desktop.Themes
{
    /// <summary>
    /// Runtime theming for RetroServe, deliberately the SAME engine and the SAME eight themes
    /// as RetroCommander and RetroTerm, so the suite offers one catalogue everywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only difference from RetroCommander is the KEY NAMES. This app's XAML was written
    /// against RetroFSXxxBrush rather than FluentXxxBrush, and there are over two hundred of
    /// those references, so the palette is published under the names the views already use
    /// rather than rewriting every view.
    /// </para>
    /// <para>
    /// The palette carries nineteen colours; the views want twenty-two brushes. The three with
    /// no palette entry are the accent and secondary hover/pressed shades, derived by darkening
    /// rather than invented, so they follow whatever the theme's accent is.
    /// </para>
    /// </remarks>
    public sealed class ThemeManager
    {
        /// <summary>The single shared instance.</summary>
        public static ThemeManager Instance { get; } = new ThemeManager();

        private Application? _app;
        private readonly List<ThemeDefinition> _themes = new();

        /// <summary>All themes the user can choose from, in display order.</summary>
        public IReadOnlyList<ThemeDefinition> Themes => _themes;

        /// <summary>The currently applied theme; never null once Initialize has run.</summary>
        public ThemeDefinition Current { get; private set; } = null!;

        private ThemeManager()
        {
            BuildThemes();
        }

        /// <summary>
        /// Registers one brush dictionary per theme and applies the saved choice.
        /// </summary>
        /// <param name="app">
        /// The running application. Call once, BEFORE the first window is shown.
        /// </param>
        public void Initialize(Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));

            for (int i = 0; i < _themes.Count; i++)
            {
                ThemeDefinition def = _themes[i];
                app.Resources.ThemeDictionaries[def.Variant] = BuildBrushDictionary(def);
            }

            string? savedId = UiSettings.Instance.Theme;
            ThemeDefinition? theme = FindById(savedId);
            if (theme == null)
                theme = _themes.Count > 0 ? _themes[0] : null;

            if (theme != null)
                Apply(theme.Id, persist: false);
        }

        /// <summary>
        /// Applies a theme by id, re-colouring the whole UI live.
        /// </summary>
        /// <param name="themeId">
        /// Id of one of the entries in Themes.
        /// </param>
        /// <param name="persist">
        /// When true the choice is written to disk for the next launch.
        /// </param>
        public void Apply(string themeId, bool persist = true)
        {
            if (_app == null) return;

            ThemeDefinition? theme = FindById(themeId);
            if (theme == null) return;

            Current = theme;
            _app.RequestedThemeVariant = theme.Variant;

            if (persist)
            {
                UiSettings.Instance.Theme = theme.Id;
                UiSettings.Instance.Save();
            }
        }

        private ThemeDefinition? FindById(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _themes.Count; i++)
            {
                if (string.Equals(_themes[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return _themes[i];
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Brush dictionary construction
        // ------------------------------------------------------------------

        // Multiplies a colour's channels, used for the hover and pressed accent shades.
        private static Color Shade(string hex, double factor)
        {
            Color c = Color.Parse(hex);
            byte r = (byte)Math.Clamp((int)(c.R * factor), 0, 255);
            byte g = (byte)Math.Clamp((int)(c.G * factor), 0, 255);
            byte b = (byte)Math.Clamp((int)(c.B * factor), 0, 255);
            return Color.FromArgb(c.A, r, g, b);
        }

        // Maps the nineteen palette colours onto the twenty-two RetroFSXxxBrush keys the views
        // use. Several keys are historical aliases from when the palette was pinned to the logo
        // (NavyBlue, DarkBlue, Green, Orange); they keep working, they just follow the theme now
        // instead of naming one fixed colour.
        private static ResourceDictionary BuildBrushDictionary(ThemeDefinition def)
        {
            Palette p = def.Palette;
            ResourceDictionary rd = new ResourceDictionary();

            void Add(string key, string hex) => rd[key] = new SolidColorBrush(Color.Parse(hex));
            void AddShade(string key, string hex, double f) => rd[key] = new SolidColorBrush(Shade(hex, f));

            // Surfaces
            Add("RetroFSBackgroundBrush", p.BackgroundPrimary);
            Add("RetroFSBackgroundAltBrush", p.BackgroundSecondary);
            Add("RetroFSBackgroundLightBrush", p.SurfaceAlt);
            Add("RetroFSDarkBlueBrush", p.BackgroundPrimary);
            Add("RetroFSNavyBlueBrush", p.BackgroundSecondary);

            // Borders
            Add("RetroFSBorderBrush", p.BorderLight);
            Add("RetroFSBorderActiveBrush", p.AccentBlue);

            // Text
            Add("RetroFSWhiteBrush", p.TextPrimary);
            Add("RetroFSLightGrayBrush", p.TextSecondary);
            Add("RetroFSGrayBrush", p.BorderDark);

            // Accent, with derived hover and pressed shades
            Add("RetroFSAccentBrush", p.AccentBlue);
            AddShade("RetroFSAccentHoverBrush", p.AccentBlue, 0.82);
            AddShade("RetroFSAccentPressedBrush", p.AccentBlue, 0.66);

            // Secondary was the logo orange; it is the theme's warning colour now.
            Add("RetroFSSecondaryBrush", p.WarningBorder);
            AddShade("RetroFSSecondaryHoverBrush", p.WarningBorder, 0.85);
            AddShade("RetroFSSecondaryPressedBrush", p.WarningBorder, 0.70);

            // Status
            Add("RetroFSGreenBrush", p.SuccessBorder);
            Add("RetroFSOrangeBrush", p.WarningBorder);
            Add("RetroFSSuccessBrush", p.SuccessBorder);
            Add("RetroFSWarningBrush", p.WarningBorder);
            Add("RetroFSErrorBrush", p.ErrorBorder);
            Add("RetroFSInfoBrush", p.AccentBlue);

            // The SAME nineteen colours published a second time under RetroCommander's key
            // names. That is what lets this app include RetroCommander's FluentTheme.axaml
            // unchanged and get the identical control vocabulary - panels, buttons, text boxes,
            // combo boxes, data grids, status bars, banners and sidebar items - rather than a
            // second set of styles that drifts.
            Add("FluentBackgroundPrimaryBrush", p.BackgroundPrimary);
            Add("FluentBackgroundSecondaryBrush", p.BackgroundSecondary);
            Add("FluentSurfaceAltBrush", p.SurfaceAlt);
            Add("FluentBorderLightBrush", p.BorderLight);
            Add("FluentBorderDarkBrush", p.BorderDark);
            Add("FluentAccentBlueBrush", p.AccentBlue);
            Add("FluentTextPrimaryBrush", p.TextPrimary);
            Add("FluentTextSecondaryBrush", p.TextSecondary);
            Add("FluentSelectionHighlightBrush", p.SelectionHighlight);
            Add("FluentHoverHighlightBrush", p.HoverHighlight);
            Add("FluentSidebarBackgroundBrush", p.SidebarBackground);
            Add("FluentSidebarSeparatorBrush", p.SidebarSeparator);
            Add("FluentWarningBackgroundBrush", p.WarningBackground);
            Add("FluentWarningBorderBrush", p.WarningBorder);
            Add("FluentSuccessBackgroundBrush", p.SuccessBackground);
            Add("FluentSuccessBorderBrush", p.SuccessBorder);
            Add("FluentErrorBackgroundBrush", p.ErrorBackground);
            Add("FluentErrorBorderBrush", p.ErrorBorder);
            Add("FluentErrorTextBrush", p.ErrorText);

            return rd;
        }

        // ------------------------------------------------------------------
        // Theme catalogue - copied from RetroCommander so the two cannot drift
        // ------------------------------------------------------------------

        private void BuildThemes()
        {
            // ---- Light (the original Fluent palette) ----
            _themes.Add(new ThemeDefinition("Light", "Light", ThemeVariant.Light, new Palette
            {
                BackgroundPrimary = "#F3F3F3",
                BackgroundSecondary = "#FFFFFF",
                SurfaceAlt = "#F8F8F8",
                BorderLight = "#D0D0D0",
                BorderDark = "#3C3C3C",
                AccentBlue = "#0078D7",
                TextPrimary = "#202020",
                TextSecondary = "#666666",
                SelectionHighlight = "#CDE8FF",
                HoverHighlight = "#EAF6FF",
                SidebarBackground = "#F0F0F0",
                SidebarSeparator = "#E0E0E0",
                WarningBackground = "#FFF8DC",
                WarningBorder = "#FFD700",
                SuccessBackground = "#F0F8F0",
                SuccessBorder = "#90EE90",
                ErrorBackground = "#FDE7E9",
                ErrorBorder = "#E81123",
                ErrorText = "#C42B1C",
            }));

            // ---- Dark (neutral dark, VS-Code-ish) ----
            _themes.Add(new ThemeDefinition("Dark", "Dark", ThemeVariant.Dark, new Palette
            {
                BackgroundPrimary = "#1E1E1E",
                BackgroundSecondary = "#252526",
                SurfaceAlt = "#2D2D30",
                BorderLight = "#3F3F46",
                BorderDark = "#555555",
                AccentBlue = "#0A84FF",
                TextPrimary = "#E8E8E8",
                TextSecondary = "#A0A0A0",
                SelectionHighlight = "#094771",
                HoverHighlight = "#2A2D2E",
                SidebarBackground = "#252526",
                SidebarSeparator = "#3F3F46",
                WarningBackground = "#3A3320",
                WarningBorder = "#D7B600",
                SuccessBackground = "#1E3320",
                SuccessBorder = "#4EC94E",
                ErrorBackground = "#3A1D1D",
                ErrorBorder = "#C44C4C",
                ErrorText = "#FF99A4",
            }));

            // ---- Solarized Dark (Ethan Schoonover palette) — inherits Dark base controls ----
            _themes.Add(new ThemeDefinition("SolarizedDark", "Solarized Dark",
                new ThemeVariant("SolarizedDark", ThemeVariant.Dark), new Palette
                {
                    BackgroundPrimary = "#002B36",   // base03
                    BackgroundSecondary = "#073642", // base02
                    SurfaceAlt = "#08404F",
                    BorderLight = "#094B5A",
                    BorderDark = "#586E75",          // base01
                    AccentBlue = "#268BD2",          // blue
                    TextPrimary = "#93A1A1",         // base1
                    TextSecondary = "#657B83",       // base00
                    SelectionHighlight = "#0B4F60",
                    HoverHighlight = "#0B4A59",
                    SidebarBackground = "#073642",
                    SidebarSeparator = "#094B5A",
                    WarningBackground = "#33301A",
                    WarningBorder = "#B58900",       // yellow
                    SuccessBackground = "#14361F",
                    SuccessBorder = "#859900",       // green
                    ErrorBackground = "#3A1F1A",
                    ErrorBorder = "#DC322F",         // solarized red
                    ErrorText = "#E8857F",
                }));

            // ---- Nord (arctic palette) — inherits Dark base controls ----
            _themes.Add(new ThemeDefinition("Nord", "Nord",
                new ThemeVariant("Nord", ThemeVariant.Dark), new Palette
                {
                    BackgroundPrimary = "#2E3440",   // nord0
                    BackgroundSecondary = "#3B4252", // nord1
                    SurfaceAlt = "#434C5E",          // nord2
                    BorderLight = "#4C566A",         // nord3
                    BorderDark = "#5E6877",
                    AccentBlue = "#88C0D0",          // nord8 (frost)
                    TextPrimary = "#ECEFF4",         // nord6
                    TextSecondary = "#9AA4B5",
                    SelectionHighlight = "#434C5E",
                    HoverHighlight = "#3B4252",
                    SidebarBackground = "#3B4252",
                    SidebarSeparator = "#4C566A",
                    WarningBackground = "#4A3F2A",
                    WarningBorder = "#EBCB8B",       // aurora yellow
                    SuccessBackground = "#34402C",
                    SuccessBorder = "#A3BE8C",       // aurora green
                    ErrorBackground = "#43292C",
                    ErrorBorder = "#BF616A",         // aurora red
                    ErrorText = "#D99CA3",
                }));

            // ---- Retro Green (P1 phosphor) — inherits Dark base controls ----
            // The three CRT themes below are RetroTerm's, carried over verbatim so both apps
            // offer the same catalogue. RetroTerm's ThemeDefinition holds 29 colours against
            // this Palette's 19; the mapping is WindowBackground -> BackgroundPrimary,
            // SectionBackground -> BackgroundSecondary, PanelBackground -> SurfaceAlt,
            // FocusBorder -> AccentBlue (it is the vivid signature colour in each), and
            // ErrorPanel* -> Error*. Warning/Success backgrounds have no RetroTerm equivalent
            // and are dark tints picked to sit on this theme's own ground.
            _themes.Add(new ThemeDefinition("RetroGreen", "Retro Green",
                new ThemeVariant("RetroGreen", ThemeVariant.Dark), new Palette
                {
                    BackgroundPrimary = "#001100",
                    BackgroundSecondary = "#002200",
                    SurfaceAlt = "#001A00",
                    BorderLight = "#004400",
                    BorderDark = "#005500",
                    AccentBlue = "#00FF00",
                    TextPrimary = "#00FF00",
                    TextSecondary = "#00AA00",
                    SelectionHighlight = "#004400",
                    HoverHighlight = "#003300",
                    SidebarBackground = "#002200",
                    SidebarSeparator = "#004400",
                    WarningBackground = "#1A1A00",
                    WarningBorder = "#FFFF00",
                    SuccessBackground = "#002200",
                    SuccessBorder = "#00FF00",
                    ErrorBackground = "#220000",
                    ErrorBorder = "#FF3300",
                    ErrorText = "#FF6644",
                }));

            // ---- Amber CRT (P3 phosphor) — inherits Dark base controls ----
            _themes.Add(new ThemeDefinition("AmberCrt", "Amber CRT",
                new ThemeVariant("AmberCrt", ThemeVariant.Dark), new Palette
                {
                    BackgroundPrimary = "#1A0F00",
                    BackgroundSecondary = "#261800",
                    SurfaceAlt = "#221400",
                    BorderLight = "#553300",
                    BorderDark = "#664400",
                    AccentBlue = "#FFB000",
                    TextPrimary = "#FFB000",
                    TextSecondary = "#CC8800",
                    SelectionHighlight = "#443300",
                    HoverHighlight = "#332200",
                    SidebarBackground = "#261800",
                    SidebarSeparator = "#553300",
                    WarningBackground = "#262600",
                    WarningBorder = "#FFFF00",
                    SuccessBackground = "#261800",
                    SuccessBorder = "#FFB000",
                    ErrorBackground = "#220000",
                    ErrorBorder = "#FF3300",
                    ErrorText = "#FF6644",
                }));

            // ---- Synthwave '84 — inherits Dark base controls ----
            _themes.Add(new ThemeDefinition("Synthwave", "Synthwave '84",
                new ThemeVariant("Synthwave", ThemeVariant.Dark), new Palette
                {
                    BackgroundPrimary = "#262335",
                    BackgroundSecondary = "#2A2139",
                    SurfaceAlt = "#241B2F",
                    BorderLight = "#463465",
                    BorderDark = "#614D85",
                    AccentBlue = "#FF7EDB",
                    TextPrimary = "#FFFFFF",
                    TextSecondary = "#B893CE",
                    SelectionHighlight = "#463465",
                    HoverHighlight = "#34294F",
                    SidebarBackground = "#2A2139",
                    SidebarSeparator = "#463465",
                    WarningBackground = "#332B1F",
                    WarningBorder = "#FEDE5D",
                    SuccessBackground = "#12332A",
                    SuccessBorder = "#72F1B8",
                    ErrorBackground = "#351525",
                    ErrorBorder = "#FE4450",
                    ErrorText = "#F97E72",
                }));

            // ---- High Contrast (accessibility: black/white/bright) — inherits Dark base ----
            _themes.Add(new ThemeDefinition("HighContrast", "High Contrast",
                new ThemeVariant("HighContrast", ThemeVariant.Dark), new Palette
                {
                    BackgroundPrimary = "#000000",
                    BackgroundSecondary = "#000000",
                    SurfaceAlt = "#1A1A1A",
                    BorderLight = "#FFFFFF",
                    BorderDark = "#FFFFFF",
                    AccentBlue = "#1F6FEB",          // readable with the forced white button text
                    TextPrimary = "#FFFFFF",
                    TextSecondary = "#E0E0E0",
                    SelectionHighlight = "#1F6FEB",
                    HoverHighlight = "#333333",
                    SidebarBackground = "#000000",
                    SidebarSeparator = "#FFFFFF",
                    WarningBackground = "#000000",
                    WarningBorder = "#FFFF00",
                    SuccessBackground = "#000000",
                    SuccessBorder = "#00FF00",
                    ErrorBackground = "#000000",
                    ErrorBorder = "#FF0000",
                    ErrorText = "#FF6666",
                }));
        }
    }

    /// <summary>One selectable theme: an id, a display name, its Avalonia variant and palette.</summary>
    public sealed class ThemeDefinition
    {
        /// <summary>Stable id used for persistence.</summary>
        public string Id { get; }

        /// <summary>Name shown in the settings dialog.</summary>
        public string DisplayName { get; }

        /// <summary>The Avalonia variant this theme's dictionary is registered under.</summary>
        public ThemeVariant Variant { get; }

        /// <summary>The colours themselves.</summary>
        public Palette Palette { get; }

        /// <summary>Creates a theme definition.</summary>
        /// <param name="id">Stable id used for persistence.</param>
        /// <param name="displayName">Name shown in the settings dialog.</param>
        /// <param name="variant">The Avalonia variant to register under.</param>
        /// <param name="palette">The colours.</param>
        public ThemeDefinition(string id, string displayName, ThemeVariant variant, Palette palette)
        {
            Id = id;
            DisplayName = displayName;
            Variant = variant;
            Palette = palette;
        }
    }

    /// <summary>
    /// A flat colour palette - the same nineteen values RetroCommander and RetroTerm use.
    /// </summary>
    public sealed class Palette
    {
        /// <summary>The window's own background.</summary>
        public string BackgroundPrimary = "#000000";

        /// <summary>Panel and list backgrounds sitting on the primary.</summary>
        public string BackgroundSecondary = "#000000";

        /// <summary>A third surface, for toolbars and headers.</summary>
        public string SurfaceAlt = "#000000";

        /// <summary>Ordinary separators and control outlines.</summary>
        public string BorderLight = "#000000";

        /// <summary>Heavier outlines and disabled text.</summary>
        public string BorderDark = "#000000";

        /// <summary>The theme's signature accent.</summary>
        public string AccentBlue = "#000000";

        /// <summary>Body text.</summary>
        public string TextPrimary = "#FFFFFF";

        /// <summary>Secondary and hint text.</summary>
        public string TextSecondary = "#FFFFFF";

        /// <summary>Selected row background.</summary>
        public string SelectionHighlight = "#000000";

        /// <summary>Pointer-over background.</summary>
        public string HoverHighlight = "#000000";

        /// <summary>Navigation sidebar background.</summary>
        public string SidebarBackground = "#000000";

        /// <summary>Separator inside the sidebar.</summary>
        public string SidebarSeparator = "#000000";

        /// <summary>Background of a warning panel.</summary>
        public string WarningBackground = "#000000";

        /// <summary>Border and text colour of a warning.</summary>
        public string WarningBorder = "#000000";

        /// <summary>Background of a success panel.</summary>
        public string SuccessBackground = "#000000";

        /// <summary>Border and text colour of a success message.</summary>
        public string SuccessBorder = "#000000";

        /// <summary>Background of an error panel.</summary>
        public string ErrorBackground = "#000000";

        /// <summary>Border of an error panel.</summary>
        public string ErrorBorder = "#000000";

        /// <summary>Error text colour.</summary>
        public string ErrorText = "#FFFFFF";
    }
}
