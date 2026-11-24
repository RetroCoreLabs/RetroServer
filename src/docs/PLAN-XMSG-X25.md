# Plan: XMSG in RetroServer (file server, connect-to gateway, sync), then protocol switches and users

**Next:** Phase 1, first todo - the restart test over HDLC: restart our node on an HDLC line to D100 and read `LIST-SYSTEMS`, to see whether D100 starts counting from 0 again as it does on Ethernet. That needs D100 running; until then the next item that needs no machine is Phase 14, the load and memory tests.

## Goal

RetroServer becomes one XMSG machine (one system number) and replaces xmsgrunner (`RetroXmsg/src/RetroXmsg.Runner`):
- a COSMOS file server (`*FA-SERVER`) with a folder per SINTRAN user, and Sync as a part of it;
- a login console (telnet, SSH later) from which a user can `connect-to` a SINTRAN machine, later also X.25 calls;
- a terminal a SINTRAN user can `CONNECT-TO`, with statistics commands (later `ssh` out to modern machines);
- answers to `LIST-ROUTE` / `LIST-SYSTEMS`, so it shows up on the ND like any other machine;
- many SINTRAN emulators at once, over HDLC and Ethernet;
- every protocol switched on and off, all off by default; users, groups and rights; every setting in RetroAdmin;
- then X.25: routing, XOT (X.25 over TCP, RFC 1613), X.25 calls from the same console.

Already built: the shared packages `RetroIO.Protocols.Xmsg.Hosting` (node building, topology, sync, shutdown),
`RetroIO.Protocols.Xmsg.Hub` (the Ethernet hub, hosted by RetroServer, admin page "XMSG Hub") and
`RetroIO.Protocols.Xmsg.Programs` (the command framework); xmsgrunner and the hub use them. Restart on Ethernet
without saved counters (`NdLinkLayer.Reopen`; measured live against D100 on 2026-11-21: D100 went back to 0 / 0).

## Decisions (Ronny)

- Shared code lives in RetroIO packages; xmsgrunner is removed once RetroServer does what it does (2026-11-11).
- One machine number for all of RetroServer. Name: "COSMOS File Server", with "Sync" as a section with its own switch.
- The old message stack (the runner's `legacy` mode: `RunLegacyAsync`, `LiveNode`) is deleted, not moved.
- Batch append becomes a command in a new tool `retroserver-cli`; the chat load test moves to the chat samples.
- Console: telnet first (localhost by default), SSH later behind the same stream interface.
- File server root = any storage location (local folder, TNFS endpoint or a `mounts.json` volume).
- Protocols off by default; an existing install gets its old on/off value written into its config.
- Plug-in servers are separate programs that dial RetroServer over gRPC; an emulator is never a plug-in.
- No login on 127.0.0.1; TLS and login only for connections from other machines.
- Management commands live in the server; a manager only sends the typed text and prints what comes back.
- Chat: complete C# server, trunks, CHAT-MON-style management, Terminal.Gui v2, then Avalonia.
- Requirements for ALL of RetroServer, every protocol (Ronny, 2026-11-22):
  - good statistics of what goes on in every protocol: live and shown in RetroAdmin;
  - every protocol has an on/off switch, default OFF; an admin user turns it on;
  - one user name and password across protocols wherever the protocol allows it; extend the user
    profile with per-protocol details where needed;
  - many clients at once, fast, high quality: no bugs, no memory leaks, modern C# with as few
    allocations as possible (Span, ArrayPool, stackalloc, no LINQ);
  - at the end, a deep security review of the whole server, with every hole found fixed.
- RetroServer can also run as a Docker container, as an option beside the normal install (Ronny, 2026-11-22).

## Architecture

```
                 RetroServer (one XMSG node, system number N)
  ┌─────────────────────────────────────────────────────────────────────┐
  │ XmsgNodeService (one pump thread, 20 ms tick, owns ALL node state)  │
  │                                                                     │
  │   shared servers:  *FA-SERVER (per-user roots)   *TADADM   chat     │
  │   gateway sessions (TadAskerSession per console user)               │
  │   sync jobs (SyncDaemon per mapping)                                │
  │                                                                     │
  │   link table:  one XmsgNodeHost per peer link                       │
  │     HDLC  : TCP listener, accepts MANY emulators (each = own link)  │
  │     HDLC  : outgoing dials to emulators that listen                 │
  │     Ether : hub (in-process HubServer) or multicast; link per peer  │
  │   per-peer state:  <data>\xmsg\peers\<system>\*.state               │
  └─────────────────────────────────────────────────────────────────────┘
        ▲ telnet console (later SSH): login -> menu -> connect-to <name|id>
```

Rules that hold everywhere:
- **One thread touches node state.** Receive threads only queue bytes/packets; the pump thread drains them.
  This fixes the Ethernet hazard for both hosts.
- **One node number, many links.** All links share the server set and the session port/number allocator, so
  two conversations on two links never get the same port.
- **Per-peer state, never shared between peers.** Sequence and seed files are keyed by the remote system
  number, under the data dir, never beside the exe.
- **A peer is identified by what it says, not by which socket it came from.** An HDLC connection is unnamed until
  the first frame tells us the remote system number; only then is its state file opened.

## Phases and todos

### Phase 1 - restart without saved message counters

Measured 2026-11-21 (capture RetroIO `src/DOC/captures/2026-11-21-xmsg-restart-d102-over-ethernet-hub.pcap`):
a restarting SINTRAN closes its Ethernet connection (`60` / `7F`), opens a new one (`0F` / `1F`) and sends the
XMSG INIT first; the other machine then counts from 0. Our node now does the same, and D100 accepted it.
- [ ] Restart test over HDLC, where there is no such connection layer: does D100 reset? If not, capture a D102
      XMSG restart on an HDLC line and read what SINTRAN sends.
- [ ] Live check of RetroServer's own node: it now reopens and sends the INIT at every start (`XmsgNodeService`,
      built and unit tested 2026-11-22, not yet run against D100; RetroServer must be restarted for it).
- [ ] Then: drop the message-number file if HDLC needs none either, else keep it under
      `<data>/xmsg/peers/<machine>/`. The link header number and the sync list are kept in any case.
- [ ] After our restart D100 reuses its old file-access connection and the listing hangs (RetroIO `src/BUGS.md`,
      Q group): find what a real file server answers to a request on a connection it does not know.

### Phase 2 - clear out xmsgrunner

- [ ] Move the chat load test to the chat samples in RetroIO.
- [ ] `retroserver-cli`: logs in like a user, runs commands one by one or from a script, through the management
      API. First commands: push / pull a file, send a batch job, link status, sync now.
- [ ] Live check that xmsgrunner on the shared packages still reaches D100 over HDLC (owed since the move).

### Phase 3 - XMSG program toolkit (`RetroIO.Protocols.Xmsg.Programs`)

Every program comes as a set (server, client, manager), as on SINTRAN: the manager reaches the server on its
second named port `<NAME>-ADMIN`, and the port a message arrived on is the permission check.
- [x] Command framework (SINTRAN abbreviations, described parameters, generated HELP, commas, no prompting for
      missing parameters) and the startup command file.
- [x] Settings file next to the program, defaults written on first run (`ProgramSettingsFile`); flags stay per program.
- [ ] Connection to RetroServer: address, token, features, reconnect after RetroServer restarts.
- [ ] Logging to RetroServer and the console with the same categories; a log-level command.
- [ ] Admin port: opens `<NAME>-ADMIN`, port check, STATUS / STOP / HELP for free.
- [x] Run loop: Ctrl+C stops cleanly (`ProgramStop`), shared exit codes (`ProgramExitCode`).
- [ ] Client parts with no drawing code (history, conversations).
- [ ] Test package: in-memory and real-socket RetroServer, the socket-reset relay.
- [ ] `xmsg-mon <server>`: one generic manager that fetches the server's command list for `?`.
- [ ] Program list (name, kind, version) for RetroAdmin's "Programs" page.

### Phase 4 - chat

Authority: CHATSV.PLNC / CHAT.PLNC / CHATMON.PLNC and `libxmsg/extras/protocols/chat-wire.json`, then D100.
Today `ChatServer` answers join, say, rename, leave, who, trunk hello and trunk said only.
- [ ] Server: `/list`, `/map`, `/topic`, who in all rooms, `/tell` (local and `D102!NAME`), history on join.
- [ ] Server: trunks (who, member lists, relay ids, `/tell` over a trunk, lock / unlock); test against CHATSV.
- [ ] Server: CHAT-MON commands in the server on `CHAT-ADMIN`; a `chat-mon` sample that also speaks CHATSV's
      binary management messages.
- [ ] Shared client part in `samples/` (all 12 commands, rooms, history, windows), unit tested.
- [ ] Console `retro-chat`; full-screen Terminal.Gui v2 client matching CHAT's 80x24 layout; Avalonia client.
- [ ] Docs page in `docfx/xmsg/` with the CHAT-MON table and trunk setup.

### Phase 5 - many links on one node

- [ ] `XmsgLinkTable`: N `XmsgNodeHost`s with one shared server set and one shared port / number allocator.
- [ ] HDLC listener that keeps accepting (`TcpBridgeTransport.ListenAsync` takes one); limits, idle timeout, firewall.
- [ ] Ethernet: a link per peer on one backend (`EthernetLink` binds one peer), and a receive queue so only the
      pump thread touches node state (the receive thread runs server code today, read 2026-11-08).
- [ ] A second live link claiming the same system number is refused and logged.
- [ ] Routes from the topology plus the links that came up; direct and indirect shown with link state.
- [ ] Relay between links (`DatagramRelay`); hub <-> HDLC relay changes hop byte 13 to 12 and the checksum,
      keeps Flags1 and the destination.
- [ ] Node rules from live work: link up on our SABM's UA; obey the peer's window (LAPB address byte); XROUT services 75 and 69 answered on our own next Flags1; connect letters to the target system.
- [ ] Node defects dated 2026-11-10 in RetroIO `src/BUGS.md` (our own resends, name lookup, kernel
      bridge, repeats).

### Phase 6 - COSMOS file server with per-user folders

- [ ] Live check: RetroServer serving a folder to D100 (`@LIST-FILES`), then a TNFS server. The settings
      (`FileServerEnabled`, `FileServerProvider`, `FileServerUri`, `FileServerMountRef` in xmsgnode-config.json,
      the API and the RetroAdmin XMSG node page) are built; a TNFS server that cannot be reached at start is
      reported and the node runs without the file server. A TNFS file over the 16 MB copy limit cannot be opened.
- [ ] USER from each request -> `<root>/<USER>/`, name checks, no path escape, the real user in the directory walk.
      A user with no folder gets "no such user"; the server makes no folders (Ronny can overrule).
- [ ] Password word checked against RetroServer users, with the "WRONG PASSWORD" status.

### Phase 7 - console, connect-to gateway and the SINTRAN terminal

- [ ] `RetroIO.Protocols.Telnet` (telnet listener moved from RetroTerm; RetroIO must not depend on RetroTerm).
- [ ] Login with RetroServer users; menu `systems`, `connect <name|number> [server]`, `who`, `status`, `quit`;
      `^]` logs the remote session out and returns to the menu.
- [ ] One `TadAskerSession` per console user with its own port; a test with several sessions on one link.
- [ ] Terminal for SINTRAN users (`CONNECT-TO <RetroServer>`, `*TADADM`): login, XMSG and file server
      statistics, who, route table; more commands through the toolkit.
- [ ] Later: SSH console; an `ssh` command out of the SINTRAN terminal (own right; SSH.NET client, not checked).

### Phase 8 - sync and the rest of the runner, hosted

- [ ] Ask Ronny the sync settings first: when it runs, each direction on its own, folder per user, user and file
      patterns, binary vs parity file types, `.` for `:` in local names, a `.xat` file beside each file.
- [ ] Sync on the pump thread; ledger and credentials under `<data>/xmsg/`.
- [ ] REST `xmsg/status` and `xmsg/sessions`, SignalR, log lines; RetroAdmin XMSG page and one-off jobs page.
- [ ] Optional pcap per link. Refuse to start beside an xmsgrunner with the same number; move old state files.

### Phase 9 - hub and HDLC over the internet: TLS and login

- [ ] TLS (`SslStream`, PFX or Windows store) on the hub and HDLC listeners, own port and switch, off by default.
- [ ] Login before joining: hello roles 3 / 4, then `"LOGIN" 00 user 00 password`, answer 0 ok / 1 refused / 2 no
      right (agreed with libxmsg 2026-11-10); the same before the first 7E on an HDLC line. Rights: join the hub,
      dial in over HDLC.
- [ ] Clients: RetroCore TCP Ethernet and HDLC transports, RetroIO `TcpEthernetBackend`, the hub uplink, nd100x
      `--hdlc` with mbedTLS. Syntax `--net=tls:host:port`, `--hdlc=N:tls:host:port`.

### Phase 10 - plug-in servers over gRPC

- [ ] One two-way gRPC stream per program, at the level a SINTRAN task sees (Register, Send, Open/ClosePort,
      AdjustSeats; Registered, Letter, Data, PortClosed, LinkDown); every wire rule stays in the node.
- [ ] gRPC threads only queue; each stream is one `IXmsgServer`; a dropped stream frees its name and ports.
- [ ] Chat server as the first plug-in; needs Phase 5's node defects fixed first.
- [ ] Not known: how long a SINTRAN client waits for an answer.

### Phase 11 - protocols off by default; users, groups and rights

- [ ] `RetroServerOptions.Enable*` are never set by the host today (RetroIO BUGS.md). (Every protocol's
      `Enabled` is off by default and old installs keep theirs: RetroIO, 2026-11-23.)
- [ ] `RetroUser`: XMSG rights (file server, gateway, sync, console) and X.25 call; groups carry rights.
- [ ] Make the default admin password `retroadmin` be changed (still unchanged on the lab machine 2026-11-21).
- [ ] REST and RetroAdmin pages for users, groups and rights.
- [ ] One login for all protocols: each protocol checks the same RetroServer user and password where it can
      (XMSG file server, console, hub, dial-up PAP/CHAP, TNFS, Econet, AFP later); per-protocol details in
      the user profile.
- [ ] Telemetry for every part of RetroServer, ONE shape (rule from Ronny 2026-11-23): named metrics
      with labels, like a perfmon counter list and like OpenTelemetry, and the log next to them, so an
      OpenTelemetry export of metrics and logs later is one line in the host. The shared class is
      `RetroTelemetry` in its own package RetroIO.Telemetry (a .NET Meter plus a kept log that also goes to ILogger);
      the API is `api/v1/telemetry/sources|metrics|log`. Names `retroio.<part>.<thing>`, OpenTelemetry
      units (`By`, `s`, `{call}`), variants as labels (`direction=in`). Dial-up, the XMSG hub and DriveWire
      are on it (RetroIO 78a91e1, b429f96, and the DriveWire commit). TNFS and Econet have their metrics on it
      too (RetroIO afb2874), and the XMSG node has metrics and log (RetroIO 94b095d). TNFS server and Econet logs are kept next to their metrics by a logging provider (RetroIO 28a601a). DriveWire's per-session lines too. Every part's log is now on it.
      RetroAdmin has a Telemetry page that shows any part's metrics and log (not yet seen live). OpenTelemetry export is a setting, off by default: `--Telemetry:OtlpEndpoint=http://collector:4318` sends every part's metrics and the log over OTLP/HTTP, `--Telemetry:ExportIntervalSeconds` (default 60). Checked 2026-11-23 against a local listener: the hub's metrics and log arrived.

### Phase 12 - X.25: routing, XOT, calls from the console

Tested against the specs and `x25emu` (WSL) only; SINTRAN has never run over X.25 (Ronny 2026-11-09).
- [ ] `RetroIO.Protocols.X25.Hosting` out of `x25switch` (RetroX25); `x25switch` then uses it.
- [ ] Ports: XOT listen and dial-out, LAPB over TCP for emulators, many at once.
- [ ] Routing table by address prefix, routed XOT; console `call <address | name>`.
- [ ] Status over REST and RetroAdmin; one thread for switch state (check `LapbInstance` timer locking); clean teardown.

### Phase 13 - RetroAdmin shows every setting

- [ ] Remove `server-settings.json` and `api/v1/settings/server` (read by nothing).
- [ ] Each phase adds its own pages; the two "every setting has a field" tests guard it.

### Phase 14 - load, memory and security

- [ ] Load tests are done for every protocol (2026-11-23). XMSG node (`XmsgNodeServiceTests.Load_...`): 16 chat programs,
      4,800 lines heard in 1.4 s after the fix that wakes the node's pump when a program asks (it was 171 s). Dial-up (`DialUpServerTests.Load_...`: 16 callers at once, ten
      waves, 160 calls in 14.5 s, each its own address, every line free again, nothing refused), for the XMSG hub (`HubServerTests.Load_...`: 16
      machines at a steady pace, 48,000 frames delivered in about 0.3 s, none lost; sent as one burst, 3,000 frames
      to each 256-frame member queue, about a third are dropped as "slow", which is the hub's design) and for TNFS, Econet and DriveWire
      (RetroIO tests `TnfsLoadTests`, `EconetLoadTests`, `DriveWireLoadTests`, category Load, run by name): 32 TNFS
      clients, 3,200 requests in about 1 s; 16 Econet stations, 960 login/load/bye steps in 3.3 s; 32 DriveWire
      clients, 3,200 sector transfers in 86 ms. All answered right, no session left.
- [ ] Hot paths reviewed for allocations (Span, ArrayPool, stackalloc).
- [ ] Deep security review of the whole server: every listener, every parser of network input, logins,
      paths (traversal), passwords, TLS, the admin API. Every finding fixed or in BUGS.md by name.

### Phase 15 - run as a Docker container (an option)

Started 2026-11-23: `docker/Dockerfile`, `eng/build-docker.ps1`, `/health`, and `docs/DOCKER.md` (how to run it,
ports, what is missing). The image starts and answers `/health`; `eng/test-docker.ps1` checks that, and the CI job `docker` builds the
image and runs it (job written 2026-11-23, not yet seen running on GitHub). Still to do:
- [ ] When the hub login is built ("no login on 127.0.0.1"), check it in a container: a connection through
      Docker's port mapping must be asked to log in. (The image already makes the hub, DriveWire and dial-up
      listen on every address; the login itself is not built yet, so there is no hole today.)
- [ ] Windows-only parts need a Linux way: certificates from a file (not only the Windows certificate store),
      printing to a file or PDF when there is no system printer, serial devices passed in with `--device`.
- [ ] The XMSG node's shutdown (log TAD users out, then close the links) seen running on `docker stop` with a
      machine connected. Checked 2026-11-23 without one: `docker stop` ends RetroServer with exit code 0 in about
      1 s, the hub logs that it stopped, and `/health` exists.



## Config shape (`<data>\xmsg-config.json`, same loader pattern as the other protocols)

```jsonc
{
  "enabled": false,
  "self": 19999,                       // our system number
  "motd": "RetroServer",
  "links": {
    "hdlcListen":  { "enabled": true, "bind": "0.0.0.0", "port": 10364, "maxConnections": 32 },
    "hdlcDial":    [ { "system": 102, "host": "127.0.0.1", "port": 1362 } ],
    "ethernet":    { "enabled": false, "net": "tcp", "host": "127.0.0.1", "port": 5010 },
    "hub":         { "enabled": false, "port": 5010 }
  },
  "nodes": [                           // the existing topology schema, unchanged
    { "id": 100, "alias": "d100", "reach": "neighbour", "link": 1 },
    { "id": 103, "alias": "d103", "reach": "via", "via": [ 100 ] }
  ],
  "fileServer": { "enabled": false, "provider": "native", "uri": "D:\\cosmos", "checkPasswords": true },
  // or: { "enabled": true, "provider": "tnfs", "uri": "tnfs://host:16384/cosmos" }
  // or: { "enabled": true, "mountRef": "cosmos-volume" }   -> an entry in mounts.json
  "console":    { "enabled": false, "bind": "127.0.0.1", "port": 2023, "escape": "^]" },
  "sync":       [ { "folder": "D:\\sync\\d100", "system": 100, "user": "*", "direction": "both" } ],
  "chat":       { "enabled": false },
  "capture":    { "enabled": false }
}
```

Every key also works as `--Xmsg:...` on the command line, and `--help` lists them.

## Critical files

- New: `RetroIO\src\RetroIO.Protocols.Xmsg.Hosting\` (+ tests), `RetroIO\src\RetroIO.Protocols.Telnet\` (+ tests)
- `RetroIO\src\RetroIO.Protocols.Xmsg.Servers\src\Fa\FaServer.cs`, `RetroIO.Protocols.Xmsg.Ndfs\src\FolderFileStore.cs`
- `RetroIO\src\RetroIO.Protocols.Xmsg.Live\src\Seam\EthernetLink.cs`, `LapbLayerAdapter.cs`, `TcpBridgeTransport.cs`
- `RetroIO\src\RetroIO.Server\src\RetroServerComposition.cs`, `RetroServerOptions.cs`
- `RetroIO\src\RetroIO.Server.Hosting\src\ProtocolConfigurationLoader.cs`, `Configuration\`
- `RetroIO\src\RetroIO.Management.Api\src\Services\ServerManager.cs`, `Controllers\` (new `XmsgController`)
- `RetroIO\src\RetroIO.Identity\src\RetroUser.cs`, `RetroFsProtocol.cs`, `RetroUserProtocolAuth.cs`
- `RetroServer\src\RetroServer.Service\Program.cs` (help text), `RetroServer\src\RetroAdmin.Desktop\` (XMSG page, users)
- `RetroXmsg\src\RetroXmsg.Runner\` (switch to the shared package; code removed from here)
