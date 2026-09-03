//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
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
    public bool AllowTnfs { get; set; }
    public bool AllowEconet { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastLoginTnfsAt { get; set; }
    public DateTime? LastLoginEconetAt { get; set; }
    public DateTime? LastFailedLoginAt { get; set; }

    public string GroupsText => Groups.Length == 0 ? "—" : string.Join(", ", Groups);

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
