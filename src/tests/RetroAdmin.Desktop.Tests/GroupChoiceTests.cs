//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroIO - Accessing yesterday's data with today's standards
//

using System.Collections.Generic;

using RetroAdmin.Desktop.Services;
using RetroAdmin.Desktop.ViewModels;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// The group tick boxes on the user forms: ticked from a user's group ids, and sent back as ids.
/// </summary>
[TestFixture]
public class GroupChoiceTests
{
    /// <summary>
    /// Ticking from a user's ids ticks exactly those groups, and the ticked ids are what is sent.
    /// </summary>
    [Test]
    public void TickGroups_ThenCheckedIds_GivesTheSameIds()
    {
        List<GroupChoice> choices = new List<GroupChoice>
        {
            new GroupChoice { GroupId = "a", GroupName = "admins" },
            new GroupChoice { GroupId = "b", GroupName = "beebs" },
            new GroupChoice { GroupId = "c", GroupName = "cosmos" },
        };

        UsersViewModel.TickGroups(choices, new[] { "c", "a" });

        Assert.That(choices[0].IsChecked, Is.True);
        Assert.That(choices[1].IsChecked, Is.False);
        Assert.That(GroupChoice.CheckedIds(choices), Is.EqualTo(new[] { "a", "c" }));

        UsersViewModel.TickGroups(choices, new string[0]);
        Assert.That(GroupChoice.CheckedIds(choices), Is.Empty);
    }
}
