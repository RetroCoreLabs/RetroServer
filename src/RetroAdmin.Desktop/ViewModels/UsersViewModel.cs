//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroAdmin.Desktop.Services;
using RetroAdmin.Desktop.Services.ServerControl;
using RetroIO.Management.Contracts;

namespace RetroAdmin.Desktop.ViewModels;

public partial class UsersViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    public ObservableCollection<UserInfo> Users { get; } = new();
    public ObservableCollection<GroupInfo> Groups { get; } = new();

    /// <summary>The groups as tick boxes on the create form.</summary>
    public ObservableCollection<GroupChoice> NewUserGroups { get; } = new();

    /// <summary>The groups as tick boxes on the edit form, ticked for the selected user's groups.</summary>
    public ObservableCollection<GroupChoice> EditUserGroups { get; } = new();

    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    [ObservableProperty]
    private string _newEmail = string.Empty;

    [ObservableProperty]
    private string _editDisplayName = string.Empty;

    [ObservableProperty]
    private string _editEmail = string.Empty;

    [ObservableProperty]
    private bool _editIsActive = true;

    [ObservableProperty]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private bool _newAllowTnfs = true;

    [ObservableProperty]
    private bool _newAllowEconet;

    [ObservableProperty]
    private bool _newAllowDialUp;

    [ObservableProperty]
    private string _newDialUpAddress = string.Empty;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private UserInfo? _selectedUser;

    [ObservableProperty]
    private bool _editAllowTnfs = true;

    [ObservableProperty]
    private bool _editAllowEconet;

    [ObservableProperty]
    private bool _editAllowDialUp;

    [ObservableProperty]
    private string _editDialUpAddress = string.Empty;

    [ObservableProperty]
    private string _editPassword = string.Empty;

    public bool HasSelectedUser => SelectedUser != null;

    /// <summary>Creates the view model and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public UsersViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadUsersAsync();
    }

    partial void OnSelectedUserChanged(UserInfo? value)
    {
        if (value == null)
        {
            EditAllowTnfs = true;
            EditAllowEconet = false;
            EditAllowDialUp = false;
            EditDialUpAddress = string.Empty;
            EditPassword = string.Empty;
            EditDisplayName = string.Empty;
            EditEmail = string.Empty;
            EditIsActive = true;
        }
        else
        {
            EditAllowTnfs = value.AllowTnfs;
            EditAllowEconet = value.AllowEconet;
            EditAllowDialUp = value.AllowDialUp;
            EditDialUpAddress = value.DialUpAddress;
            EditPassword = string.Empty;
            EditDisplayName = value.DisplayName;
            EditEmail = value.Email;
            EditIsActive = value.IsActive;
        }

        TickGroups(EditUserGroups, value?.GroupIds ?? []);

        OnPropertyChanged(nameof(HasSelectedUser));
    }

    [RelayCommand]
    private async Task LoadUsersAsync()
    {
        try
        {
            var usersResponse = await _connection.Api.GetUsersAsync();
            var groupsResponse = await _connection.Api.GetGroupsAsync();

            if (usersResponse == null || !usersResponse.Success || usersResponse.Data == null)
            {
                LogUi("Warning", "Could not load users from management API.");
                return;
            }

            if (groupsResponse == null || !groupsResponse.Success || groupsResponse.Data == null)
            {
                LogUi("Warning", "Could not load groups from management API.");
                return;
            }

            var users = usersResponse.Data.Users;
            var groups = groupsResponse.Data.Groups;
            var loadedUsers = new List<UserInfo>();
            var loadedGroups = new List<GroupInfo>();

            for (int i = 0; i < users.Count; i++)
            {
                var user = users[i];
                var userGroups = new List<string>();
                for (int j = 0; j < user.Groups.Count; j++)
                    userGroups.Add(user.Groups[j]);

                loadedUsers.Add(MapUser(user.UserId, user.Username, userGroups.ToArray(), user));
            }

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                loadedGroups.Add(new GroupInfo
                {
                    GroupId = group.GroupId,
                    GroupName = group.GroupName,
                    MemberCount = group.MemberCount
                });
            }

            ApplyLoadedData(loadedUsers, loadedGroups, null);
        }
        catch (Exception ex)
        {
            LogUi("Error", $"Error loading users: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword))
        {
            LogUi("Warning", "Username and password are required to create a user.");
            return;
        }

        try
        {
            var request = new CreateUserRequest
            {
                Username = NewUsername,
                Password = NewPassword,
                DisplayName = string.IsNullOrWhiteSpace(NewDisplayName) ? NewUsername : NewDisplayName,
                Email = NewEmail,
                GroupIds = GroupChoice.CheckedIds(NewUserGroups),
                // Sent since 2026-11-10; before that the two boxes on the form went nowhere.
                AllowTnfs = NewAllowTnfs,
                AllowEconet = NewAllowEconet,
                AllowDialUp = NewAllowDialUp,
                DialUpAddress = NewDialUpAddress.Trim()
            };

            ApiResponse<UserDto>? created = await _connection.Api.CreateUserAsync(request);
            if (created is not { Success: true })
            {
                LogUi("Warning", created?.Error?.Message ?? "Could not create the user.");
                return;
            }

            NewUsername = string.Empty;
            NewDisplayName = string.Empty;
            NewEmail = string.Empty;
            TickGroups(NewUserGroups, []);
            NewPassword = string.Empty;
            NewAllowTnfs = true;
            NewAllowEconet = false;
            NewAllowDialUp = false;
            NewDialUpAddress = string.Empty;

            LogUi("Info", "User created.");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            LogUi("Error", $"Error creating user: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        if (SelectedUser == null)
            return;

        try
        {
            var request = new UpdateUserRequest
            {
                AllowTnfs = EditAllowTnfs,
                AllowEconet = EditAllowEconet,
                AllowDialUp = EditAllowDialUp,
                // Empty clears a fixed address: the pool is used again.
                DialUpAddress = EditDialUpAddress.Trim(),
                DisplayName = EditDisplayName,
                Email = EditEmail,
                IsActive = EditIsActive,
                GroupIds = GroupChoice.CheckedIds(EditUserGroups),
            };

            ApiResponse<UserDto>? updated = await _connection.Api.UpdateUserAsync(SelectedUser.UserId, request);
            if (updated is not { Success: true })
            {
                LogUi("Warning", updated?.Error?.Message ?? "Could not save the user.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(EditPassword))
                await _connection.Api.ChangePasswordAsync(SelectedUser.UserId, EditPassword);

            EditPassword = string.Empty;
            LogUi("Info", $"User '{SelectedUser.Username}' updated.");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            LogUi("Error", $"Error saving user: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            LogUi("Warning", "Group name is required.");
            return;
        }

        try
        {
            var request = new CreateGroupRequest { GroupName = NewGroupName };
            await _connection.Api.CreateGroupAsync(request);
            NewGroupName = string.Empty;
            LogUi("Info", "Group created.");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            LogUi("Error", $"Error creating group: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteUserAsync(string userId)
    {
        try
        {
            await _connection.Api.DeleteUserAsync(userId);
            LogUi("Info", "User deleted.");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            LogUi("Error", $"Error deleting user: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(string groupId)
    {
        try
        {
            await _connection.Api.DeleteGroupAsync(groupId);
            LogUi("Info", "Group deleted.");
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            LogUi("Error", $"Error deleting group: {ex.Message}");
        }
    }

    private void ApplyLoadedData(List<UserInfo> users, List<GroupInfo> groups, string? selectedUserId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Users.Clear();
            for (int i = 0; i < users.Count; i++)
                Users.Add(users[i]);

            Groups.Clear();
            for (int i = 0; i < groups.Count; i++)
                Groups.Add(groups[i]);

            NewUserGroups.Clear();
            EditUserGroups.Clear();
            for (int i = 0; i < groups.Count; i++)
            {
                NewUserGroups.Add(new GroupChoice { GroupId = groups[i].GroupId, GroupName = groups[i].GroupName });
                EditUserGroups.Add(new GroupChoice { GroupId = groups[i].GroupId, GroupName = groups[i].GroupName });
            }

            TickGroups(EditUserGroups, SelectedUser?.GroupIds ?? []);

            if (selectedUserId != null)
            {
                for (int i = 0; i < Users.Count; i++)
                {
                    if (Users[i].UserId == selectedUserId)
                    {
                        SelectedUser = Users[i];
                        break;
                    }
                }
            }
        });
    }

    /// <summary>Surfaces a message to the user through the connection's status channel.</summary>
    /// <param name="level">Severity, for the caller's benefit.</param>
    /// <param name="message">Text to show.</param>
    /// <remarks>
    /// Both previous implementations were dead ends: one published into the log of the server
    /// hosted in this process, and the REST one wrote to a console a windowed application does
    /// not have.
    /// </remarks>
    private void LogUi(string level, string message) =>
        _connection.RaiseStatus(level == "Info" ? message : $"{level}: {message}");


    /// <summary>Turns a user DTO into the row shape the grid binds to.</summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="username">User name.</param>
    /// <param name="groups">Names of the groups the user belongs to.</param>
    /// <param name="user">The DTO.</param>
    /// <returns>The row.</returns>
    private static UserInfo MapUser(string userId, string username, string[] groups, UserDto user) =>
        new()
        {
            UserId = userId,
            Username = username,
            Groups = groups,
            GroupIds = user.GroupIds.ToArray(),
            DisplayName = user.DisplayName,
            Email = user.Email ?? string.Empty,
            IsActive = user.IsActive,
            AllowTnfs = user.AllowTnfs,
            AllowEconet = user.AllowEconet,
            AllowDialUp = user.AllowDialUp,
            DialUpAddress = user.DialUpAddress ?? string.Empty,
            LastLoginDialUpAt = user.LastLoginDialUpAt,
            LastLoginAt = user.LastLoginAt,
            LastLoginTnfsAt = user.LastLoginTnfsAt,
            LastLoginEconetAt = user.LastLoginEconetAt,
            LastFailedLoginAt = user.LastFailedLoginAt,
        };

    /// <summary>Ticks exactly the groups whose ids are given.</summary>
    /// <param name="choices">The tick boxes.</param>
    /// <param name="ids">The ids to tick.</param>
    public static void TickGroups(System.Collections.Generic.IList<GroupChoice> choices, string[] ids)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            choices[i].IsChecked = Array.IndexOf(ids, choices[i].GroupId) >= 0;
        }
    }
}
