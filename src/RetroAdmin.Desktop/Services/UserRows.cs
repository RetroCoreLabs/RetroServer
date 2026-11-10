//

// SPDX-License-Identifier: MIT

// Copyright (c) 1985-2026 Ronny Hansen

// RetroCore Labs — https://github.com/RetroCoreLabs

// RetroFS -- Accessing yesterday's data with today's standards

//



using System;



namespace RetroAdmin.Desktop.Services;



// What is left of IRetroFSService.cs. The interface itself, its RetroFSRestService

// implementation and the DTOs around them existed to let the UI switch between an in-process

// server and the REST API. There is one mode now, so all of that is gone; these two types stay

// because they are the shapes the user and group grids bind to, with display formatting the wire

// DTOs have no business carrying.



public class UserInfo

{

    public string UserId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string[] Groups { get; set; } = [];
    public string[] GroupIds { get; set; } = [];
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public bool AllowTnfs { get; set; }

    public bool AllowEconet { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? LastLoginTnfsAt { get; set; }

    public DateTime? LastLoginEconetAt { get; set; }

    public DateTime? LastFailedLoginAt { get; set; }



    public string GroupsText => Groups.Length == 0 ? "—" : string.Join(", ", Groups);



    public string IsActiveText => IsActive ? "Yes" : "No";
    public string AllowTnfsText => AllowTnfs ? "Yes" : "No";

    public string AllowEconetText => AllowEconet ? "Yes" : "No";

    public string LastLoginTnfsText => FormatTimestamp(LastLoginTnfsAt);

    public string LastLoginEconetText => FormatTimestamp(LastLoginEconetAt);

    public string LastFailedLoginText => FormatTimestamp(LastFailedLoginAt);



    private static string FormatTimestamp(DateTime? value) =>

        value.HasValue ? value.Value.ToLocalTime().ToString("g") : "—";

}



public class GroupInfo

{

    public string GroupId { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public int MemberCount { get; set; }

}

/// <summary>
/// One group as a tick box on the create and edit forms. The form sends the ids of the ticked ones.
/// </summary>
public partial class GroupChoice : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>The group's id, which is what the server is sent.</summary>
    public string GroupId { get; init; } = string.Empty;

    /// <summary>The group's name, which is what the form shows.</summary>
    public string GroupName { get; init; } = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isChecked;

    /// <summary>The ids of the ticked groups.</summary>
    /// <param name="choices">The tick boxes.</param>
    /// <returns>The ids, in list order.</returns>
    public static System.Collections.Generic.List<string> CheckedIds(System.Collections.Generic.IList<GroupChoice> choices)
    {
        System.Collections.Generic.List<string> ids = new System.Collections.Generic.List<string>();
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i].IsChecked)
            {
                ids.Add(choices[i].GroupId);
            }
        }

        return ids;
    }
}
