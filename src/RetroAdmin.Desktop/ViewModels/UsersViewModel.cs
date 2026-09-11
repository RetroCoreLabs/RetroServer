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

    [ObservableProperty]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private bool _newAllowTnfs = true;

    [ObservableProperty]
    private bool _newAllowEconet;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private UserInfo? _selectedUser;

    [ObservableProperty]
    private bool _editAllowTnfs = true;

    [ObservableProperty]
    private bool _editAllowEconet;

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
            EditPassword = string.Empty;
        }
        else
        {
            EditAllowTnfs = value.AllowTnfs;
            EditAllowEconet = value.AllowEconet;
            EditPassword = string.Empty;
        }

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
                DisplayName = NewUsername,
                Email = string.Empty
            };

            await _connection.Api.CreateUserAsync(request);

            NewUsername = string.Empty;
            NewPassword = string.Empty;
            NewAllowTnfs = true;
            NewAllowEconet = false;

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
            };

            await _connection.Api.UpdateUserAsync(SelectedUser.UserId, request);

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
            AllowTnfs = user.AllowTnfs,
            AllowEconet = user.AllowEconet,
            LastLoginAt = user.LastLoginAt,
            LastLoginTnfsAt = user.LastLoginTnfsAt,
            LastLoginEconetAt = user.LastLoginEconetAt,
            LastFailedLoginAt = user.LastFailedLoginAt,
        };
}
