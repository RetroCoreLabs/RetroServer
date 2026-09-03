//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Reflection;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// Enforces that RetroServeUI stays a management client.
/// </summary>
/// <remarks>
/// The UI used to reference the protocol servers, the identity store and the security engines so
/// it could host a server inside its own process. That made it a third independent copy of the
/// server rather than a client of one, and it is why it could only ever manage a server on the
/// same machine. Adding one of those references back would compile perfectly and quietly undo it,
/// so the rule is asserted here instead of left to reviewers.
/// </remarks>
[TestFixture]
public class ClientDependencyTests
{
    /// <summary>Assemblies a management client has no business referencing.</summary>
    private static readonly string[] ForbiddenReferences =
    {
        "RetroIO.Protocols.Tnfs",
        "RetroIO.Protocols.Econet",
        "RetroIO.Server.Hosting",
        "RetroIO.Server.Core",
        "RetroIO.Identity",
        "RetroIO.Security.Firewall",
        "RetroIO.Security.Implementation",
        "RetroIO.Management.Api"
    };

    [Test]
    public void RetroServeUI_ReferencesNoServerAssemblies()
    {
        Assembly ui = typeof(RetroAdmin.Desktop.App).Assembly;
        AssemblyName[] references = ui.GetReferencedAssemblies();

        for (int i = 0; i < references.Length; i++)
        {
            string name = references[i].Name ?? string.Empty;

            for (int f = 0; f < ForbiddenReferences.Length; f++)
            {
                Assert.That(name, Is.Not.EqualTo(ForbiddenReferences[f]),
                    $"RetroServeUI references {name}. It is a management client: it talks to a " +
                    "server over the management API and must depend only on " +
                    "RetroFS.Management.Contracts and RetroFS.Management.Client.");
            }
        }
    }

    [Test]
    public void RetroServeUI_StillReferencesTheContractAndTheClient()
    {
        // The other half of the rule: it must not shed these either, or it would have no way to
        // reach a server at all and the test above would pass vacuously.
        AssemblyName[] references = typeof(RetroAdmin.Desktop.App).Assembly.GetReferencedAssemblies();

        bool contracts = false;
        bool client = false;

        for (int i = 0; i < references.Length; i++)
        {
            string name = references[i].Name ?? string.Empty;
            if (name == "RetroIO.Management.Contracts") contracts = true;
            if (name == "RetroIO.Management.Client") client = true;
        }

        Assert.Multiple(() =>
        {
            Assert.That(contracts, Is.True, "the UI must reference the wire contract");
            Assert.That(client, Is.True, "the UI must reference the management client");
        });
    }
}
