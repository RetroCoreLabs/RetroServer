//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroFS -- Accessing yesterday's data with today's standards
//

using System.Diagnostics;
using RetroIO.Management.Contracts;
using RetroAdmin.Desktop.Services.ServerControl;

namespace RetroAdmin.Desktop.Tests;

/// <summary>
/// Drives a real RetroServer process through the same connection object the UI uses.
/// </summary>
/// <remarks>
/// This is the test that proves the client half of the architecture actually works: the UI's
/// connection logs in over HTTP, gets a token, and reads real data back. Everything else about
/// "the UI is a client now" is structure; this is the part that would fail if the structure were
/// wrong.
///
/// The spawned server gets its own RETROFS_DATA_PATH, so it creates its own identity store and
/// configuration in a temp directory and never touches the developer's real settings under
/// Documents\RetroFS. It also gets its OWN management ports through --Management:Urls, and its
/// own protocol ports: binding the standard 5000/5001 made this fixture fail intermittently
/// whenever anything else held them -- a developer's running instance, a leftover, or another
/// test project spawning a server of its own.
/// </remarks>
[TestFixture]
[Category("Integration")]
[Category("ProcessSpawn")]
public class ManagementConnectionIntegrationTests
{
    /// <summary>Credentials RetroIdpBootstrap creates on a fresh data directory.</summary>
    private const string AdminUser = "admin";

    /// <summary>Default password for that account on a fresh install.</summary>
    private const string AdminPassword = "retroadmin";

    /// <summary>Line the server logs once the HTTP surface is up.</summary>
    private const string ReadyMarker = "Application started";

    /// <summary>HTTPS address this fixture's server binds, away from the standard ports.</summary>
    private const string TestApiAddress = "https://localhost:5111";

    /// <summary>Both addresses, in the form --Management:Urls expects.</summary>
    private const string TestManagementUrls = "http://localhost:5110;https://localhost:5111";

    /// <summary>TNFS ports for this fixture, away from the defaults.</summary>
    private const int TestTnfsPort = 16390;

    /// <summary>Econet AUN port for this fixture, away from the default.</summary>
    private const int TestEconetPort = 32790;

    /// <summary>DriveWire TCP port for this fixture, away from the default 65504 an emulator or a running server uses.</summary>
    private const int TestDriveWirePort = 65510;

    /// <summary>Sectors in the OS-9 volume served as DriveWire drive 0: a 35-track single-sided floppy.</summary>
    private const int DriveWireVolumeSectors = 630;

    private Process? _server;
    private string _dataPath = null!;
    private byte[] _driveWireImage = null!;
    private int _helloSector;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        _dataPath = Path.Combine(Path.GetTempPath(), "retroserve-conn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataPath);

        string solutionDir = TestContext.CurrentContext.TestDirectory;

        while (!Directory.Exists(Path.Combine(solutionDir, "src")) &&
               Directory.GetParent(solutionDir) != null)
        {
            solutionDir = Directory.GetParent(solutionDir)!.FullName;
        }

        // The service project, since everything moved under src/ (commit 8968a19).
        string project = Path.Combine(solutionDir, "src", "RetroServer.Service", "RetroServer.Service.csproj");
        Assert.That(File.Exists(project), Is.True, $"RetroServer project not found at {project}");

        // A small OS-9 volume served as DriveWire drive 0, inside this fixture's own data directory.
        _driveWireImage = BuildOs9Volume(DriveWireVolumeSectors, out _helloSector);
        string imagePath = Path.Combine(_dataPath, "drive0.dsk");
        File.WriteAllBytes(imagePath, _driveWireImage);

        _server = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{project}\" --no-build -- " +
                            $"--Management:Urls={TestManagementUrls} " +
                            $"--Tnfs:UdpPort={TestTnfsPort} --Tnfs:TcpPort={TestTnfsPort} " +
                            $"--Econet:UdpPort={TestEconetPort} " +
                            "--DriveWire:Enabled=true --Tnfs:Enabled=true --Econet:Enabled=true " + // every protocol starts off (2026-11-23)
                            $"--DriveWire:Port={TestDriveWirePort} " +
                            $"--DriveWire:Drives:0:Path=\"{imagePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        // Its own data directory, set on the CHILD only. Without this the server rewrites the
        // developer's real tnfs-config.json every time the suite runs.
        _server.StartInfo.Environment["RETROFS_DATA_PATH"] = _dataPath;

        TaskCompletionSource ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _server.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            TestContext.Out.WriteLine($"[SERVER] {e.Data}");
            if (e.Data.Contains(ReadyMarker, StringComparison.Ordinal)) ready.TrySetResult();
        };

        _server.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            TestContext.Error.WriteLine($"[SERVER ERROR] {e.Data}");
            if (e.Data.Contains(ReadyMarker, StringComparison.Ordinal)) ready.TrySetResult();
        };

        _server.Start();
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        // Wait on the server's own output, never on a sleep. It has to build the identity store
        // (Argon2id) and both protocol stacks before the HTTP surface is up.
        Task finished = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(90)));

        if (finished != ready.Task)
        {
            throw new TimeoutException(
                $"RetroServer did not log \"{ReadyMarker}\" within 90 seconds. " +
                "See the [SERVER] lines above.");
        }
    }

    [OneTimeTearDown]
    public void StopServer()
    {
        if (_server is { HasExited: false })
        {
            _server.Kill(entireProcessTree: true);
            _server.WaitForExit(10000);
        }

        _server?.Dispose();

        try
        {
            if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, true);
        }
        catch (IOException)
        {
            // The server keeps a log file open in there; a leftover temp directory is harmless.
        }
    }

    /// <summary>
    /// The DriveWire server hosted by RetroServer serves drive 0: sector 0 and the sector holding the file's data come back
    /// over TCP exactly as they are in the image.
    /// </summary>
    [Test]
    public async Task DriveWire_ServesTheImageGivenOnTheCommandLine()
    {
        using System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", TestDriveWirePort);
        System.Net.Sockets.NetworkStream stream = client.GetStream();

        (byte status, byte[] data) = await ReadSectorAsync(stream, 0, 0);
        Assert.That(status, Is.EqualTo(0), "status of the sector 0 read");
        Assert.That(data, Is.EqualTo(_driveWireImage.AsSpan(0, 256).ToArray()), "sector 0");

        (status, data) = await ReadSectorAsync(stream, 0, (uint)_helloSector);
        Assert.That(status, Is.EqualTo(0), "status of the file data read");
        Assert.That(data, Is.EqualTo(_driveWireImage.AsSpan(_helloSector * 256, 256).ToArray()), "the sector holding HELLO");
    }

    /// <summary>
    /// Reads one sector the way the NitrOS-9 driver rbdw.asm does: OP_READEX ($D2), drive, three LSN bytes; 256 bytes come
    /// in; the 16-bit sum of those bytes goes out; one status byte comes in. A status of $F3 (E$CRC) is retried with
    /// OP_REREADEX ($F2), as the driver does.
    /// </summary>
    /// <param name="stream">The connection to the server.</param>
    /// <param name="drive">The drive number.</param>
    /// <param name="lsn">The logical sector number.</param>
    /// <returns>The status byte and the sector bytes.</returns>
    private static async Task<(byte Status, byte[] Data)> ReadSectorAsync(Stream stream, byte drive, uint lsn)
    {
        byte op = 0xD2;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            byte[] request = { op, drive, (byte)(lsn >> 16), (byte)(lsn >> 8), (byte)lsn };
            await stream.WriteAsync(request);
            byte[] data = new byte[256];
            using CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await stream.ReadExactlyAsync(data, limit.Token);
            int sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i];
            }

            await stream.WriteAsync(new byte[] { (byte)(sum >> 8), (byte)sum });
            byte[] status = new byte[1];
            await stream.ReadExactlyAsync(status, limit.Token);
            if (status[0] != 0xF3)
            {
                return (status[0], data);
            }

            op = 0xF2;
        }

        return (0xF3, Array.Empty<byte>());
    }

    /// <summary>
    /// Builds a headerless OS-9 (RBF) volume holding one file, HELLO, from the field layout in NitrOS-9 defs/rbf.d.
    /// </summary>
    /// <param name="totalSectors">The size of the volume in 256-byte sectors.</param>
    /// <param name="helloSector">Set to the sector that holds the file's data.</param>
    /// <returns>The image bytes, the whole volume.</returns>
    private static byte[] BuildOs9Volume(int totalSectors, out int helloSector)
    {
        const int SectorSize = 256;
        int mapBytes = (totalSectors + 7) / 8;
        int mapSectors = (mapBytes + SectorSize - 1) / SectorSize;
        int rootFd = 1 + mapSectors;
        int rootData = rootFd + 1;
        int fileFd = rootFd + 2;
        int fileData = rootFd + 3;
        int usedSectors = fileData + 1;
        byte[] image = new byte[totalSectors * SectorSize];

        // DD.TOT, DD.TKS, DD.MAP, DD.BIT, DD.DIR, DD.FMT, DD.SPT, DD.NAM.
        PutUInt24(image, 0, totalSectors);
        image[3] = 18;
        image[4] = (byte)(mapBytes >> 8);
        image[5] = (byte)mapBytes;
        image[7] = 1;
        PutUInt24(image, 8, rootFd);
        image[16] = 2;
        image[18] = 18;
        PutName(image, 31, "DWTEST");

        // The allocation map: every sector up to the file's data is in use.
        for (int s = 0; s < usedSectors; s++)
        {
            image[SectorSize + (s / 8)] |= (byte)(0x80 >> (s % 8));
        }

        // The root directory descriptor (FD.ATT, FD.SIZ, one segment) and its three entries.
        int at = rootFd * SectorSize;
        image[at] = 0xBF;
        image[at + 12] = 3 * 32;
        PutUInt24(image, at + 16, rootData);
        image[at + 20] = 1;

        at = rootData * SectorSize;
        PutName(image, at, "..");
        PutUInt24(image, at + 29, rootFd);
        PutName(image, at + 32, ".");
        PutUInt24(image, at + 32 + 29, rootFd);
        PutName(image, at + 64, "HELLO");
        PutUInt24(image, at + 64 + 29, fileFd);

        // HELLO's descriptor and its five bytes of data.
        at = fileFd * SectorSize;
        image[at] = 0x0B;
        image[at + 12] = 5;
        PutUInt24(image, at + 16, fileData);
        image[at + 20] = 1;
        byte[] text = { (byte)'H', (byte)'E', (byte)'L', (byte)'L', (byte)'O' };
        text.CopyTo(image, fileData * SectorSize);

        helloSector = fileData;
        return image;
    }

    /// <summary>Writes an OS-9 name: the last character has its top bit set.</summary>
    private static void PutName(byte[] data, int at, string name)
    {
        for (int i = 0; i < name.Length; i++)
        {
            data[at + i] = (byte)name[i];
        }

        data[at + name.Length - 1] |= 0x80;
    }

    /// <summary>Writes a 24-bit big-endian number.</summary>
    private static void PutUInt24(byte[] data, int at, int value)
    {
        data[at] = (byte)(value >> 16);
        data[at + 1] = (byte)(value >> 8);
        data[at + 2] = (byte)value;
    }

    /// <summary>Builds a connection pointed at the spawned server.</summary>
    /// <returns>The connection.</returns>
    private static ManagementConnection Connect() => new ManagementConnection(
        new ServerConnectionSettings
        {
            Mode = ServerConnectionMode.Remote,
            Address = TestApiAddress,

            // The server presents the ASP.NET Core development certificate, which the test host
            // has no reason to trust. This is exactly the case the opt-in flag exists for.
            AllowUntrustedCertificate = true
        });

    [Test]
    public async Task Connect_WithTheAdminAccount_Succeeds()
    {
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);

        Assert.Multiple(() =>
        {
            Assert.That(result.Connected, Is.True, result.Message);
            Assert.That(connection.IsAuthenticated, Is.True);
            Assert.That(connection.Username, Is.EqualTo(AdminUser));
        });
    }

    [Test]
    public async Task Connect_WithAWrongPassword_ReportsInvalidCredentials()
    {
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, "not-the-password");

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ConnectionOutcome.InvalidCredentials));
            Assert.That(connection.IsAuthenticated, Is.False);
        });
    }

    [Test]
    public async Task Connect_ToAnAddressWithNoServer_ReportsUnreachable()
    {
        // Port 5099 has nothing on it. This is the case that must not look like a bad password,
        // because the two need completely different things from the user.
        await using ManagementConnection connection = new ManagementConnection(
            new ServerConnectionSettings
            {
                Mode = ServerConnectionMode.Remote,
                Address = "https://localhost:5099",
                AllowUntrustedCertificate = true
            });

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);

        Assert.That(result.Outcome, Is.EqualTo(ConnectionOutcome.Unreachable), result.Message);
    }

    [Test]
    public async Task AuthenticatedClient_ReadsRealDataBack()
    {
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);
        Assert.That(result.Connected, Is.True, result.Message);

        ApiResponse<UserListResponse>? users = await connection.Api.GetUsersAsync();

        Assert.Multiple(() =>
        {
            Assert.That(users, Is.Not.Null, "the request itself failed");
            Assert.That(users!.Success, Is.True);
            Assert.That(users.Data!.Users, Is.Not.Empty, "the bootstrap admin account should be listed");
        });
    }

    [Test]
    public async Task EconetActivityEndpoint_AnswersTheClient()
    {
        // The endpoint added so the UI's Econet monitor no longer reads an in-process object.
        // Empty is the right answer here: no Econet client has connected to this server.
        await using ManagementConnection connection = Connect();

        ConnectionResult result = await connection.ConnectAsync(AdminUser, AdminPassword);
        Assert.That(result.Connected, Is.True, result.Message);

        ApiResponse<EconetActivityResponse>? activity = await connection.Api.GetEconetActivityAsync(5);

        Assert.Multiple(() =>
        {
            Assert.That(activity, Is.Not.Null);
            Assert.That(activity!.Success, Is.True);
            Assert.That(activity.Data!.Commands, Is.Not.Null);
            Assert.That(activity.Data.Transfers, Is.Not.Null);
        });
    }

    [Test]
    public async Task WithoutLoggingIn_ManagementCallsAreRefused()
    {
        // The API answered anyone who could reach the port until authentication landed. This is
        // the regression guard for that: no token, no data.
        await using ManagementConnection connection = Connect();

        ApiResponse<UserListResponse>? users = await connection.Api.GetUsersAsync();

        Assert.That(users?.Data?.Users, Is.Null.Or.Empty,
            "an unauthenticated caller must not receive the user list");
    }
}
