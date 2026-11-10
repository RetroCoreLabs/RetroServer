//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroIO - Accessing yesterday's data with today's standards
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// Every field of every settings type the management API sends is used by the RetroAdmin page that shows it
/// (Ronny 2026-11-10: "a test that fails when a setting has no field"). The API half, that every key in a settings
/// file is in the API, is <c>EverySettingHasAnApiFieldTests</c> in RetroIO.
/// </summary>
/// <remarks>
/// The check reads the page's source: each field must be read (<c>.Name</c>) or written (<c>Name =</c>) there. A
/// field added to the API and never touched by its page makes this fail and names it. It cannot tell whether the
/// page draws the field well; that is for a person to look at.
/// </remarks>
[TestFixture]
public class EverySettingHasAPageFieldTests
{
    private static IEnumerable<TestCaseData> Pages()
    {
        yield return Case(typeof(TnfsSettingsDto), "ViewModels/SettingsViewModel.cs");
        yield return Case(typeof(EconetSettingsDto), "ViewModels/SettingsViewModel.cs");
        yield return Case(typeof(EconetDiscDto), "Services/SettingsLines.cs");
        yield return Case(typeof(EconetWireProfileOverrideDto), "Services/SettingsLines.cs");
        yield return Case(typeof(MountDto), "Services/SettingsLines.cs");
        yield return Case(typeof(DriveWireSettingsDto), "ViewModels/DriveWireSettingsViewModel.cs");
        yield return Case(typeof(DriveWireDriveSettingDto), "ViewModels/DriveWireSettingsViewModel.cs");
        yield return Case(typeof(XmsgNodeSettingsDto), "ViewModels/XmsgNodeSettingsViewModel.cs");
        yield return Case(typeof(XmsgHubSettingsDto), "ViewModels/XmsgHubViewModel.cs");
        yield return Case(typeof(SecuritySettingsDto), "ViewModels/SecuritySettingsViewModel.cs");
        yield return Case(typeof(StartupSettingsDto), "ViewModels/StartupSettingsViewModel.cs");
        yield return Case(typeof(FirewallConfigDto), "ViewModels/FirewallViewModel.cs");
    }

    private static TestCaseData Case(Type api, string page)
    {
        return new TestCaseData(api, page).SetName("EveryFieldOf_" + api.Name + "_IsOnItsPage");
    }

    /// <summary>
    /// Every public property of the API type is read or written in the page's source.
    /// </summary>
    /// <param name="api">The type the API sends.</param>
    /// <param name="page">The page source, relative to the RetroAdmin.Desktop folder.</param>
    [TestCaseSource(nameof(Pages))]
    public void EveryField_IsUsedByItsPage(Type api, string page)
    {
        string source = File.ReadAllText(Path.Combine(FindDesktopFolder(), page));
        List<string> missing = new List<string>();
        PropertyInfo[] fields = api.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < fields.Length; i++)
        {
            string name = Regex.Escape(fields[i].Name);
            if (!Regex.IsMatch(source, @"\." + name + @"\b") && !Regex.IsMatch(source, @"\b" + name + @"\s*=[^=]"))
            {
                missing.Add(api.Name + "." + fields[i].Name);
            }
        }

        Assert.That(missing, Is.Empty, "Fields the server sends that " + page + " never uses");
    }

    /// <summary>
    /// The RetroAdmin.Desktop source folder, found by walking up from where the tests run.
    /// </summary>
    /// <returns>The folder.</returns>
    private static string FindDesktopFolder()
    {
        DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "RetroAdmin.Desktop");
            if (File.Exists(Path.Combine(candidate, "RetroAdmin.Desktop.csproj")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("The RetroAdmin.Desktop source folder was not found above " + TestContext.CurrentContext.TestDirectory);
    }
}
