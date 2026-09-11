//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.ViewModels;

/// <summary>Lists what the server is serving.</summary>
/// <remarks>
/// This used to resolve IFileSystemBackend out of the in-process host's service provider, read
/// tnfs-config.json off the local disk to find the root, and enumerate that directory directly.
/// All three only worked because the server was inside this process. It now asks the server,
/// which is the only version that can work against another machine.
/// </remarks>
public partial class VolumesViewModel : ViewModelBase
{
    private readonly ManagementConnection _connection;

    /// <summary>Volumes reported by the server.</summary>
    public ObservableCollection<VolumeInfo> Volumes { get; } = new();

    [ObservableProperty]
    private string _tnfsRootPath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Creates the view model and starts the first load.</summary>
    /// <param name="connection">Connection to the server being managed.</param>
    public VolumesViewModel(ManagementConnection connection)
    {
        _connection = connection;
        _ = LoadVolumesAsync();
    }

    /// <summary>Reloads the volume list from the server.</summary>
    /// <returns>A task that completes when the list has been refreshed.</returns>
    [RelayCommand]
    private async Task LoadVolumesAsync()
    {
        if (!_connection.IsAuthenticated)
        {
            Status = "Not connected.";
            return;
        }

        ApiResponse<VolumeListResponse>? response = await _connection.Api.GetVolumesAsync();

        if (response is not { Success: true, Data: not null })
        {
            Status = response?.Error?.Message ?? "Could not read volumes from the server.";
            return;
        }

        Volumes.Clear();

        List<VolumeDto> volumes = response.Data.Volumes;

        for (int i = 0; i < volumes.Count; i++)
        {
            VolumeDto volume = volumes[i];

            Volumes.Add(new VolumeInfo
            {
                Name = volume.Name,
                Path = volume.Path,

                // The server already classifies each entry, so the UI no longer keeps its own
                // copy of the extension-to-format table that had to be edited in two places.
                Type = volume.IsDirectory ? "Directory" : volume.Type,
                IsMounted = true
            });
        }

        Status = $"{Volumes.Count} volume(s).";
    }

    /// <summary>Sets the root path shown in the header.</summary>
    /// <param name="rootPath">Root directory reported by the server's TNFS settings.</param>
    public void SetRootPath(string rootPath) => TnfsRootPath = rootPath;
}

/// <summary>One row in the volumes list.</summary>
public class VolumeInfo
{
    /// <summary>File or directory name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path on the server.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Human-readable type, as classified by the server.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Whether the volume is currently mounted.</summary>
    public bool IsMounted { get; set; }
}
