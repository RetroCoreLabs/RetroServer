//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Diagnostics;
using System.Text;
using RetroServer.Service;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// The build stamp RetroServer reports in --version, the help text and the start-up log
/// (<see cref="BuildIdentity"/>, filled in by the CaptureGitIdentity target in src/Directory.Build.props).
/// </summary>
/// <remarks>
/// Copied from RetroTerm's BuildIdentityTests (2026-11-24). BuildIdentity.cs is compiled into this test
/// assembly (a linked file), which the same build target stamps, so these read a real stamp.
/// </remarks>
[TestFixture]
public class BuildIdentityTests
{
    [Test]
    public void TheCommitIsRecordedAndIsNotTheFallback()
    {
        // "unknown" is what this reports when the MSBuild target did not run or was misspelled.
        // Passing on that value would be the exact failure the stamp exists to prevent.
        Assert.That(BuildIdentity.Commit, Is.Not.EqualTo(BuildIdentity.Unknown));

        // Twelve hex characters, as asked for by rev-parse --short=12.
        Assert.That(BuildIdentity.Commit.Length, Is.EqualTo(12));
        for (int i = 0; i < BuildIdentity.Commit.Length; i++)
        {
            char c = BuildIdentity.Commit[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            Assert.That(hex, Is.True, "the commit contains '" + c + "', which is not a hex digit");
        }
    }

    [Test]
    public void TheStampedCommitIsARealCommitInThisRepository()
    {
        // Asks git whether the stamped hash names a commit. IT DELIBERATELY DOES NOT COMPARE AGAINST
        // HEAD: the order is build, test, then commit, so right after a commit the binary carries the
        // parent's hash and a HEAD comparison would go red until the next build. Existence still
        // catches "unknown", a mistyped property, a cut-off hash or a value typed in by hand.
        string kind = RunGit("cat-file -t " + BuildIdentity.Commit);

        if (string.IsNullOrEmpty(kind))
        {
            // No git, or not a checkout (source unzipped rather than cloned): nothing to compare with.
            return;
        }

        Assert.That(kind, Is.EqualTo("commit"));
    }

    [Test]
    public void TheBranchAndBuildTimeAreRecorded()
    {
        Assert.That(BuildIdentity.Branch, Is.Not.EqualTo(BuildIdentity.Unknown));
        Assert.That(BuildIdentity.BuildDate, Is.Not.EqualTo(BuildIdentity.Unknown));
        Assert.That(BuildIdentity.BuildTime, Is.Not.EqualTo(BuildIdentity.Unknown));
    }

    [Test]
    public void TheDescriptionNamesEverythingAReaderNeeds()
    {
        // This line is what --version, the help and the start-up log print, so it has to carry the
        // whole answer: a version with no commit, or a commit with no date, leaves the reader guessing.
        string description = BuildIdentity.Describe();

        Assert.That(description, Does.StartWith("RetroServer "));
        Assert.That(description, Does.Contain(BuildIdentity.Commit));
        Assert.That(description, Does.Contain(BuildIdentity.Branch));
        Assert.That(description, Does.Contain(BuildIdentity.BuildDate));
        Assert.That(description, Does.Contain(BuildIdentity.BuildTime));
    }

    [Test]
    public void AnUncommittedTreeSaysSoInWords()
    {
        // A hash on its own claims the build IS that commit. During ordinary work it is not, and a
        // reader comparing two results has to be able to see that.
        if (!BuildIdentity.IsDirty)
        {
            return;
        }

        Assert.That(BuildIdentity.Describe(), Does.Contain("uncommitted changes"));
        Assert.That(BuildIdentity.Short, Does.Contain("+"));
    }

    private static string RunGit(string arguments)
    {
        try
        {
            string here = Path.GetDirectoryName(typeof(BuildIdentityTests).Assembly.Location) ?? ".";
            ProcessStartInfo start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = here,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process? process = Process.Start(start);
            if (process == null)
            {
                return string.Empty;
            }

            StringBuilder output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());
            if (!process.WaitForExit(10_000) || process.ExitCode != 0)
            {
                return string.Empty;
            }

            return output.ToString().Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git is not installed.
            return string.Empty;
        }
    }
}
