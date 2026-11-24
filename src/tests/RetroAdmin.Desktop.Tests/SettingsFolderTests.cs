//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// RetroAdmin's settings moved from %APPDATA%\RetroServe to %APPDATA%\RetroAdmin (2026-11-24).
/// Every test works in its own temporary folder, never in the real %APPDATA%.
/// </summary>
[TestFixture]
public class SettingsFolderTests
{
    private string _appData = null!;

    [SetUp]
    public void MakeFolder()
    {
        _appData = Path.Combine(Path.GetTempPath(), "retroadmin-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_appData);
    }

    [TearDown]
    public void RemoveFolder()
    {
        Directory.Delete(_appData, true);
    }

    [Test]
    public void AFileOnlyInTheOldFolder_IsCopiedToTheNewOne_AndTheOldOneIsKept()
    {
        string oldFile = Path.Combine(_appData, SettingsFolder.OldName, "server-connection.json");
        Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
        File.WriteAllText(oldFile, "{\"Address\":\"https://example:5001\"}");

        string path = SettingsFolder.PathOf(_appData, "server-connection.json");

        Assert.That(path, Is.EqualTo(Path.Combine(_appData, "RetroAdmin", "server-connection.json")));
        Assert.That(File.ReadAllText(path), Does.Contain("https://example:5001"), "the saved address is kept");
        Assert.That(File.Exists(oldFile), Is.True, "the old folder is left as it was");
    }

    [Test]
    public void AFileInTheNewFolder_IsNeverOverwrittenFromTheOldOne()
    {
        string oldFile = Path.Combine(_appData, SettingsFolder.OldName, "ui-settings.json");
        string newFile = Path.Combine(_appData, SettingsFolder.Name, "ui-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");

        SettingsFolder.PathOf(_appData, "ui-settings.json");

        Assert.That(File.ReadAllText(newFile), Is.EqualTo("new"));
    }

    [Test]
    public void NoFileAnywhere_GivesThePathInTheNewFolder_AndCreatesNothing()
    {
        string path = SettingsFolder.PathOf(_appData, "ui-settings.json");

        Assert.That(path, Is.EqualTo(Path.Combine(_appData, "RetroAdmin", "ui-settings.json")));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void TheDefaultServiceName_IsTheNameTheInstallerRegisters()
    {
        // The installer's constant lives in RetroServer.Service, which this project does not
        // reference (package clash, see the csproj), so the name is read from its source file.
        string installer = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "RetroServer.Service", "WindowsServiceInstaller.cs"));

        Assert.That(installer, Does.Contain("public const string ServiceName = \"" + ServerConnectionSettings.DefaultServiceName + "\";"));
    }
}
