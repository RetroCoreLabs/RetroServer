//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using RetroAdmin.Desktop.Services.ServerControl;
using RetroAdmin.Desktop.ViewModels;
using RetroAdmin.Desktop.Views;

namespace RetroAdmin.Desktop;

/// <summary>Application entry point and composition.</summary>
/// <remarks>
/// There is one mode now. The USE_REST_API conditional that used to pick between an in-process
/// server and the REST API is gone, along with the in-process branch: RetroServeUI is a
/// management client, and the server it manages is a separate process.
/// </remarks>
public partial class App : Application
{
    /// <summary>The connection every view model is built against.</summary>
    public static ManagementConnection? Connection { get; private set; }

    /// <inheritdoc/>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        // Register every theme's brush dictionary and apply the saved choice. This must run
        // BEFORE the first window is shown, or the window paints once with no brushes at all.
        Themes.ThemeManager.Instance.Initialize(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // https://docs.avaloniaui.net/docs/guides/development-guides/data-validation
            DisableAvaloniaDataAnnotationValidation();

            ManagementConnection connection = new ManagementConnection();
            Connection = connection;

            // Sign in first: with authentication on the API, there is nothing the shell can show
            // until a token exists.
            LoginViewModel login = new LoginViewModel(connection);
            LoginWindow loginWindow = new LoginWindow { DataContext = login };

            login.Connected += () =>
            {
                MainWindow shell = new MainWindow
                {
                    DataContext = new MainWindowViewModel(connection)
                };

                desktop.MainWindow = shell;
                shell.Show();
                loginWindow.Close();
            };

            desktop.MainWindow = loginWindow;

            // Disposing the connection kills a server this UI started as a child process, so
            // closing the window cannot strand one holding the TNFS and Econet ports.
            desktop.ShutdownRequested += (_, _) => connection.DisposeAsync().AsTask().Wait(5000);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Removes the data-annotation validators Avalonia adds by default.</summary>
    private void DisableAvaloniaDataAnnotationValidation()
    {
        DataAnnotationsValidationPlugin[] toRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        for (int i = 0; i < toRemove.Length; i++)
        {
            BindingPlugins.DataValidators.Remove(toRemove[i]);
        }
    }
}
