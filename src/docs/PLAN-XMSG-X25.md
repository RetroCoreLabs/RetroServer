# Plan: XMSG in RetroServer (file server, connect-to gateway, sync), then protocol switches and users

**Next:** the XMSG program toolkit (command framework and startup file are built), then chat step 1 (server: the missing commands), then Phase 2 - many links on one node. Phase 1 is in place (uncommitted): the shared package `RetroIO.Protocols.Xmsg.Hosting` exists and xmsgrunner uses it. Still owed from Phase 1: a live check that xmsgrunner reaches D100. The topology class kept its name `TopologyConfig`.

## Context

RetroServer serves TNFS, Econet and DriveWire. It has no XMSG. All the XMSG protocol code already exists in
RetroIO packages, but the program that puts it together - topology, node building, sequence state, shutdown,
sync - lives as `internal` code inside `xmsgrunner` (`RetroXmsg\src\RetroXmsg.Runner`), and it can only talk to
ONE peer over ONE link.

Ronny wants RetroServer to be an XMSG node that:
- has a configured machine id (system number);
- serves a COSMOS file server (`*FA-SERVER`) that can be switched on and off; root folder, one folder per USER
  under it, the user's files under that;
- offers a login console (telnet, SSH later) where a user can `connect-to` a SINTRAN host by name or number;
- knows every remote machine and whether it is reached directly or through another machine;
- runs over XMSG on HDLC and on Ethernet;
- **accepts many incoming TCP connections from different SINTRAN emulators at once** (added 2026-11-08);
- and, after that: every protocol has an on/off switch, all off by default; users, groups and rights are configurable;
- then X.25: routing, XOT (X.25 over TCP, RFC 1613) connections that can be routed, and X.25 terminal calls from the
  SAME console as XMSG - the console offers whichever of XMSG and X.25 is switched on (added 2026-11-08);
- console telnet and SSH ports are configurable, and a user must be given the right to use the console and each
  kind of connection from it (added 2026-11-08);
- the RetroAdmin UI shows every function and every configuration option (added 2026-11-08).

## Facts this plan rests on (verified by reading the code, 2026-11-08)

| Fact | Where |
|---|---|
| `XmsgNodeHost(ILink, node, routes, IResponderSequenceStore, params IXmsgServer[])` is public; one host = one link = one peer; needs `Pump()` every ~20 ms | `RetroIO\src\RetroIO.Protocols.Xmsg.Node\src\Seam\XmsgNodeHost.cs:72,209` |
| No `Stop`/`Dispose` on the host; shutdown = `TadServer.ShutdownAllSessions` + `SendFrames` + LAPB DISC. Skipping it crashed D100's XMSG (measured 2026-08-17) | `XmsgNodeHost.cs:247`, `TadServer.cs:2245`, runner `Program.cs:2672-2712` |
| `FaServer` takes the concrete `FolderFileStore`; USER in a request is ignored; directory-walk user is the constant "SYSTEM"; no password check | `Xmsg.Servers\src\Fa\FaServer.cs:439,1004,2035,399` |
| `TadAskerSession` (the connect-to client) is callback driven: `Start()`, `OnReceive`, `SendLine`, `TerminalText` event - no Console. Only ever tested one at a time | `Xmsg.Node\src\Tad\TadAskerSession.cs:28` |
| Ethernet: receive thread runs server `Handle` while the pump thread runs `DrainPending`; no lock or queue between them. **A real hazard for a 24/7 daemon** | `Xmsg.Live\src\Seam\EthernetLink.cs:86,487,543` |
| `EthernetLink(name, localSystem, peerSystem, backend)` binds ONE peer | `EthernetLink.cs:177` |
| `TcpBridgeTransport.ListenAsync` returns the FIRST bridge that connects - one only | `Xmsg.Live\src\TcpBridgeTransport.cs:115` |
| nd100x HDLC can dial out: `--hdlc=N:HOST:PORT` | `nd100x\docs\command-line-options.md:71` |
| Topology schema, direct (`neighbour`) vs indirect (`via`) routes, `BuildRoutingEntries` - all `internal` to the runner | `RetroXmsg.Runner\Topology.cs:350,731` |
| Sync daemon, live transfer agent, credentials, push/pull drivers - `internal` to the runner; the planner/ledger/scanner are public in `Xmsg.Sync` | `RetroXmsg.Runner\SyncDaemon.cs`, `LiveSyncTransferAgent.cs`, `FaPushRun.cs`, `FaPullRun.cs` |
| State files (`xmsg-sequence.state`, `xmsg-link-seed.state`, `fa-connection.state`, `xmsg-sync-ledger.state`) sit beside the exe. **Losing the sequence file = the ND silently drops our frames** | runner `Program.cs:2167,2255,1046`, `SyncDaemon.cs:202` |
| A protocol is wired in RetroIO, not RetroServer: options + `Add*Server` + `BackgroundService` + config loader + `RetroServerOptions.Enable*` + `ServerManager` type test + REST controller + contracts. DriveWire is the newest template | `RetroIO.Server\src\RetroServerComposition.cs:119-142`, `RetroIO.Management.Api\src\Services\ServerManager.cs:94` |
| Data dir is `PathHelper.GetRetroFSPath()` (`RETROFS_DATA_PATH`, else Documents\RetroFS) | `RetroIO.Storage.Utilities\src\PathHelper.cs:28` |
| Users: `RetroUser` has only `AllowTnfs`/`AllowEconet`; `RetroFsProtocol` enum + `CanUseProtocol` switch; no home folder field | `RetroIO.Identity\src\RetroUser.cs:17,107`, `RetroUserProtocolAuth.cs:25` |
| Telnet server exists in RetroTerm (`ITelnetApp`); **no SSH server library anywhere in the tree** (SSH.NET is client only) | `RetroTerm\src\RetroTerm.Core.Protocols.TelnetServer\` |
| No shutdown-timeout setting in the daemon (host default applies) | grep, RetroServer + RetroIO |

**Not verified, must be checked in Phase 1 before building on it:**
- ~~FA server operations~~ SETTLED 2026-11-08: `FaServer.Dispatch` (`FaServer.cs:997-1040`) answers reserve,
  release, open, close, read, write, set block size, create, delete and the directory enquiry. Only requests 0x01,
  0x04 and 0x0D are refused (layouts never captured). The comment in `topology-d19999.json:32-36` is out of date.
- ~~Ethernet, several machines on one connection~~ SETTLED by Ronny 2026-11-09: yes, one Ethernet connection can
  serve several remote machines at once. Phase 2 builds a link per peer on one backend.
- ~~RetroCore HDLC dial-out~~ SETTLED by Ronny 2026-11-09: yes, the RetroCore ND-100 emulator can dial out
  over HDLC like nd100x. Both emulators can connect to RetroServer's HDLC listener.
- X.25: whether `LapbInstance` still runs its timers with no lock around the state machine (noted in the
  2026-09-02 survey); whether a SINTRAN host accepts a PAD call (X.28/X.29) placed by RetroIO's `PadClient`, or
  needs the SINTRAN TAD-over-X.25 protocol instead (`RetroIO.Protocols.X25.Sintran`; the old survey called its TAD
  handler an echo demo).
- **SINTRAN has never been tested over X.25** (Ronny, 2026-11-09). SINTRAN X.25 software exists but has not
  been run yet; testing against it is a later step, not part of this plan. Until then the only things to test
  X.25 against are the specifications and the C X.25 emulator `x25emu`, which runs in WSL in Ronny's
  `repos/os/x25emu` checkout (a portable X.25 stack with switching, PAD and TAD). So Phase 7 is verified against
  x25emu and the specs only. Anything SINTRAN-specific over X.25 stays marked "not tested against SINTRAN" until
  that later test is done. Per the house rule, agreeing with x25emu is a regression
  check, not proof of correct behaviour: the spec is the authority.

## Decisions made (Ronny, 2026-11-08)

- Shared code goes in a new RetroIO package; xmsgrunner and RetroServer both use it.
- Console: telnet first (localhost by default), SSH later behind the same stream interface.
- File server root = any storage location (local folder or TNFS endpoint, or a `mounts.json` volume).
- Protocols off by default; an existing install gets its old on/off value written into its config on first
  start after the update, so nothing changes for it.

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

## Phases

### Hub over the internet: TLS and login (added 2026-11-10, Ronny via the libxmsg session)

The hub itself is built (uncommitted): package `RetroIO.Protocols.Xmsg.Hub`, hosted by RetroServer from
`xmsghub-config.json` (off by default), API `api/v1/xmsghub/...`, admin page "XMSG Hub". Still to do:
- **TLS** on the hub listener and on the HDLC listener, beside plain TCP: `SslStream`, certificate from a PFX
  file or the Windows certificate store, its own port and switch, off by default.
- **Login before joining**, with RetroServer's users (passwords are already Argon2id hashes in
  `RetroIO.Identity`). New rights: join the hub, dial in over HDLC. The admin Users page manages them.
  - Hub hello roles: 3 = machine with login, 4 = hub with login. After the hello, one length-prefixed frame
    `"LOGIN" 00 user 00 password`. The answer is one byte: 0 ok, 1 refused, 2 no right. Agreed by libxmsg
    2026-11-10.
  - HDLC line: the same LOGIN exchange on the TCP stream before the first 7E flag.
  - **No login on 127.0.0.1** (Ronny). Login and TLS are required only for connections from other machines.
- **Clients to change:**
  - RetroCore's TCP Ethernet client (`Emulated.HW/Common/Network/TcpEthernetBackend.cs`);
  - RetroIO's `TcpEthernetBackend`;
  - the hub uplink;
  - RetroCore's HDLC TCP transport;
  - nd100x `--hdlc`, in C, **with mbedTLS** (Ronny). No WASM build is needed for now.
  - Proposed syntax: `--net=tls:host:port` and `--hdlc=N:tls:host:port`.

### Plug-in XMSG servers over gRPC (decided by Ronny 2026-11-10)

New XMSG servers, for example chat, run as their own programs and are NOT compiled into RetroServer.
- **What a plug-in is** (Ronny, 2026-11-10): a small program, in C, C# or any other language, that ONLY runs the
  server code. An emulator (nd100x, RetroCore) is NEVER a plug-in client. It reaches a server only as an XMSG
  machine, over the hub or an HDLC line.
- **Who connects:** the server program dials RetroServer over **gRPC** (Ronny's choice), using one two-way
  stream. The stream is local only by default. From other machines it needs the same TLS and login as the hub.
- **Where the boundary sits:** at the level a SINTRAN task sees, NOT raw frames. Every wire rule stays in the
  node, once: ACKs, Flags1, saved sequence state, XENSE recovery, resends, repeat replay and XEIMA.
  - The plug-in sends: `Register{name, kind, seats}`, `Send{fromPort, toMagic, bytes, secure}`,
    `OpenPort`/`ClosePort`, `AdjustSeats`, `Unregister`.
  - The node sends: `Registered{magic}` or `Refused{code}`, `Letter{port, fromMagic, bytes}`,
    `Data{port, fromMagic, bytes}`, `PortClosed`, `LinkDown{system}`.
- **Threading:** gRPC threads only queue. The pump thread applies the queue, the same rule as
  `DeliverInboundOnPump`.
- **In the node:** each stream appears as one `IXmsgServer`, so LIST-NAMES and `list servers` show it without
  any change. Name routing uses one shared name table.
- **A stream that drops:** its name and ports are released. New traffic to it gets XEIMA.
- **Chat first:** `ChatServer` is already written against the task-style API (`XmsgKernel`), so the
  `ChatRoom`/`ChatMessage` code can sit behind the plug-in unchanged.
- **Before this is built:** the node problems in RetroIO `src/BUGS.md` dated 2026-11-10 must be fixed (closed
  port, our own resends, the name lookup, the kernel bridge, repeats in the node). The reply-sequence question
  is listed there under "needs the real specification".
- **Not known yet:** how long a SINTRAN client waits for an answer.

### XMSG program toolkit: what every server, client and manager program shares (decided by Ronny 2026-11-10)

Every future XMSG program comes as a set - a server, a client, a manager ("-mon") program - each with its OWN
commands. What repeats is the machinery, not the commands. Two RetroIO packages: the toolkit, and its test helpers.

**How SINTRAN does it (read in CHATSV.PLNC / CHATMON.PLNC, 2026-11-10), and we follow it:** the manager is an
ordinary separate program reaching the server over XMSG on the server's second, ordinary named port
(`CHAT-ADMIN`), so an operator never spends a member's seat. The permission check is the port a message arrived on:
management only on the admin port, room traffic only on the room port. The server saves nothing; the machine's
boot file runs CHAT-MON commands at start. LOCK is deliberately never saved.

**Settings that change while running (Ronny's choice):** a startup command file next to the server, holding the
same commands you type in the manager (`START-TRUNK 102`). The SERVER runs it itself at every start, so the
manager is not needed at boot; the manager is for changes and questions while it runs. A command typed with
`SAVE` is also appended to the file. Lock-type commands are never saved.

**The command framework** (Ronny's specification):
- A command is registered with its name, its parameters - each required or optional, each with a description -
  a description of the command, and the function that runs it. HELP / `?` is generated from the registrations.
- The parser finds the command by SINTRAN abbreviation (each part shortened: `LI-T` = LIST-TRUNKS), in any
  letter case, splits the parameters, and calls the function.
- A command gives back zero or more outputs through one common way of reporting (lines, each with a kind:
  text, warning, error), so a console, Terminal.Gui, Avalonia, the startup file and a remote manager all show the
  same thing.
- **No asking for missing parameters.** A missing required parameter fails the command with a message naming
  every missing parameter and what it is. Optional ones may be left out.
- Commas separate parameters as on SINTRAN; an empty place (`CMD a,,c`) leaves that optional parameter out.
  (Confirmed by Ronny 2026-11-10.)
- The same table serves client slash commands (`/join`), manager commands and startup-file lines.

**The rest of the toolkit:**
- Settings file: JSON next to the program, defaults written on first run, flags override one run (today copied
  in both chat samples).
- Connection: address, token, connect, the features RetroServer has, and reconnecting after RetroServer restarts
  (nothing does that today).
- Logging: one call gives log lines to RetroServer and to the console, with the same categories everywhere. A
  log-level command for servers (new; SINTRAN has none).
- Admin port for servers: opens `<NAME>-ADMIN`, applies the port check, and gives every server STATUS, STOP and
  HELP for free.
- Run loop: polling, Ctrl+C stops cleanly (leave, close ports), exit codes.
- Client parts with no drawing code: the 100-line history, input history, conversation windows, a member list.
- Test package: the in-memory RetroServer and the real-socket one, now copied between test files, and the
  socket-reset relay that stands in for a killed program.
- Program list: each program reports its name, kind and version; RetroAdmin's "Programs" page shows them (Phase 8).
- RetroAdmin management of servers: later (Phase 8).

**Where management commands live (Ronny 2026-11-10: option A):** in the SERVER. They are registered in the server
like its startup-file commands, and the server runs them itself; nothing else runs them. A manager is only a
keyboard and a screen: it sends the typed line as text to the server's admin port (`<NAME>-ADMIN`) and prints the
0..n output lines that come back. When it connects it fetches the server's command list for `?` help. RetroServer
only carries the text there and the answer back. Places to type, in build order:
1. the startup file (read by the server at start);
2. `xmsg-mon <server name>`, one generic manager for every server (with the toolkit);
3. RetroAdmin "Programs" page: the running programs, and per server a command line with an output area, through
   RetroServer's API (Phase 8);
4. RetroServer's login console (Phase 4).
Chat's manager additionally speaks CHATSV's own binary management messages, so it can manage SINTRAN's server.

### Chat: a full chat client in three front ends, a complete server, trunks and CHAT-MON (decided by Ronny 2026-11-10)

Why: the console sample `retro-chat` knows `/who`, `/nick` and `/quit` only and has no screen. The SINTRAN client
(CHAT.PLNC, about 6,400 lines) has 12 commands and a full-screen 80x24 window. The C# chat server answers only join,
say, rename, leave and who, so most commands would get no answer from RetroServer today.

Decisions (Ronny, 2026-11-10):
- Complete the C# server so every command works against RetroServer as it does against CHATSV.
- Trunks to other machines, managed like CHAT-MON on SINTRAN.
- The shared client code is part of the samples and lives in `samples/`, not a published package.
- Terminal.Gui **version 2** for the full-screen client.
- Avalonia client in this plan, after the console clients.

What exists (read 2026-11-10): `ChatMessageKind` already has the wire kinds for map, rooms, topic, all-who,
history, direct messages, every trunk message and every admin message. `ChatServer` handles Join, Say, Rename,
Leave, Who, TrunkHello and TrunkSaid; `ChatTrunks` keeps the trunk states. Everything else is unhandled.
The authority for each message is CHATSV.PLNC / CHAT.PLNC / CHATMON.PLNC (new-planc `tests/corpus/ndinsight`)
and `libxmsg/extras/protocols/chat-wire.json`; agreeing with them by reading the PLANC, then measuring on D100.

Steps, in order:
1. **Server: the missing commands.** Room list (`/list`), machine map (`/map`), topic (`/topic`, `/topic <text>`),
   who in all rooms, private message (`/tell NAME text` and `/tell D102!NAME text`, with the sent and bad-name
   answers), history on join (`INITIALIZE <room> <n>` sets how much a room keeps). Each answer laid out as CHATSV
   sends it. Unit tests per message.
2. **Server: trunks.** Trunk who and member lists, the relay with its ids (a line passed on once, never looped),
   private messages over a trunk, lock and unlock (learn no new machines). A trunk is a belief that a peer is
   reachable, not a link - as CHATSV says. Test against CHATSV on D100/D102 over the hub.
3. **Server: management, CHAT-MON style.** The admin messages (status, stop, start/stop/restart trunk, list trunks,
   initialize, set name, lock/unlock trunks, drop member, list members) registered as commands IN the chat server
   (toolkit command framework, typed through xmsg-mon), with CHAT-MON's names, abbreviations (`LI-T` =
   LIST-TRUNKS) and any letter case. A `chat-mon` sample that is xmsg-mon plus CHATSV's binary management
   messages, so the same commands also manage CHATSV on SINTRAN. Trunks to start at boot: the startup command file.
   Who may send them: the same check CHATSV makes (Ronny 2026-11-10) - read 2026-11-10, it is the PORT: management
   is accepted only on the admin port `CHAT-ADMIN`, room traffic only on the room port (CHATSV.PLNC `fromAdmin`).
4. **Shared chat client part** in `samples/` (no screen code), built on the toolkit: the connection to RetroServer (settings, token), the
   command parser for all 12 commands, room state, the member list, the topic, the 100-line history, and the
   conversation windows (`/w <n>`, `/close`, ctrl-W). Unit tested without a screen.
5. **Console client** `retro-chat`: every command, `/help` as on SINTRAN, on that library.
6. **Full-screen client** with Terminal.Gui v2: the CHATUI layout exactly - 80x24, three stacked frames; title and
   status row (room, member count, `/help for commands`, date and time); 17 chat lines paged through the 100-line
   history; input line; window bar. Screenshots of CHAT on SINTRAN beside it to check it matches.
7. **Avalonia client** on the same library: the same parts in a desktop window.
8. **Documents**: the chat programs page in `docfx/xmsg/`, CHAT-MON command table, trunk setup.

Verified when: each command answered by the C# server and by CHATSV the same way (D100 measured); a trunk
RetroServer <-> D102 carries a line both ways and a `/tell D102!NAME`; `chat-mon` drives both servers; the
Terminal.Gui screen matches CHAT on SINTRAN side by side.

### Phase 2 - many links on one node

- `XmsgLinkTable`: owns N `XmsgNodeHost`s with one shared `IXmsgServer` set and one shared port/number allocator.
  The runner's relay mode already runs two hosts on one server set (`RelayHostWiringTests`); extend to N.
- Multi-accept HDLC listener: `TcpBridgeListener` that keeps accepting (the existing `ListenAsync` takes one).
  Each accepted socket gets its own LAPB link and host. Limits: max connections, idle timeout, firewall check.
- A per-peer sequence/seed store under `<data>\xmsg\peers\<system>\`, opened once the peer's number is known.
  A second live link claiming the same remote system number is refused and logged - it would corrupt the sequence.
- Ethernet: a link per learned peer on one backend (only after the "not verified" item above is answered).
- Hub: optionally host `HubServer` in-process, so emulators join `--net=tcp:<server>:5010` with no separate `xmsghub`.
- Routing: routes come from the topology PLUS links that came up. "Direct" = a live link to that system;
  "indirect" = `via` a direct one. `list route` and the gateway menu show both, with link state.
- Relay between the hub and HDLC lines, from the D100 captures (libxmsg session, 2026-11-10): change the hop
  byte 13 to 12 and work out the header checksum again. Keep the sender's Flags1 and the destination node
  exactly as they are, because Flags1 belongs end to end to the two machines, not to the relay.
- Facts from live work (libxmsg session, 2026-11-10), as requirements for a node that acts as a SINTRAN
  machine:
  - a link is up once our own SABM is answered by UA;
  - obey the peer's receive window, which is the LAPB address byte on data frames (0x09 = 4 frames,
    0x07 = 3);
  - answer data sent to a closed port with XEIMA;
  - answer XROUT service 75 and 69 at port 0 on our own next Flags1;
  - keep Flags1 per peer and persist it;
  - address a connect letter to the target system, not the link neighbour.
- Relay between links (a frame for D103 arriving from D100) - the runner has `DatagramRelay`; wire it across the table.

### Phase 3 - COSMOS file server with per-user folders

- `IFaFileStore` extracted from `FolderFileStore`; `FaServer` takes the interface (`FaServer.cs:439`).
  `FolderFileStore` keeps implementing it, so xmsgrunner is unchanged.
- **The root is a storage location, not a Windows path** (Ronny, 2026-11-08: "maybe its a linux/windows root
  folder, maybe its a TNFS endpoint"). `BackendFaFileStore` reads and writes through the same storage layer
  Econet discs use: inline `Provider` + `Uri`, or a `MountRef` into `mounts.json`
  (`RetroIO.Server.Hosting\src\EconetVfsDiscs.cs` - `native` and `tnfs` providers work today, `TnfsVolumeProvider`
  connects eagerly). Reuse that resolution code rather than writing a second one.
- USER parsed from each request (`FaFileName.TryParse` already exists) -> `<root>/<USER>/` on that storage.
  Name sanitising and a path-traversal guard. Directory walk returns the real user, not the constant "SYSTEM".
- A USER with no folder on the storage gets "no such user"; the server never creates user folders by itself
  (assumption, Ronny can overrule). Users are made by creating the folder on the storage.
- To verify first: the FA server reads/writes by byte offset; check a TNFS-backed volume gives a seekable stream
  through the storage layer, and what happens when the TNFS endpoint is down at start (Econet fails the mount).
- Password: the FA request carries a folded 16-bit word; check it against the RetroServer user (Phase 6) when
  configured, with the "WRONG PASSWORD" status the client side already expects.
- On/off switch and root folder in config. Off = the server is not registered at all, so a remote machine gets a
  clean "no such server" rather than a refusal.
- Later option: a root that is a `mounts.json` volume (Azure, VFS, NDFS image) via `IFileSystemBackend`.

### Phase 4 - login console and connect-to gateway

- TCP console with telnet line mode. Reuse RetroTerm's telnet server code by moving the minimal listener into a
  new RetroIO package `RetroIO.Protocols.Telnet` (RetroIO must not depend on RetroTerm).
- **One console for every protocol.** Telnet port and SSH port each configurable and each on/off. The menu offers
  XMSG `connect` only when XMSG is on, and X.25 `call` only when X.25 is on (Phase 7 plugs in here); a connection
  type is a small interface the console knows nothing about beyond "open, bytes in, bytes out, close".
- Login against RetroServer users. A user needs the console right, and separately the right for each connection
  type (XMSG connect, X.25 call); without it the option is not shown. Menu: `systems` (name, number, direct/indirect,
  link up/down), `connect <name|number> [server]` (default `*TADADM`), `who`, `status`, `quit`.
- Each console session owns one `TadAskerSession`, given its own client port from the shared allocator; frames
  are routed to it by port on the pump thread. Escape key (default `^]`) logs the remote session out cleanly
  and returns to the menu.
- Many users at once, to the same or different machines, over the shared links. This is untested today and gets
  a test with several sessions on one link.
- SSH: separate later step; the library choice is made then. The console code sits behind a stream interface so SSH plugs in
  without changing it.

### Phase 5 - the rest of the runner's functions, hosted

- **Folder sync** (you named it): configured mappings `local folder <-> machine + SINTRAN user` (or `*` = one
  subfolder per user), push, pull, or both; the ledger and credentials move to `<data>\xmsg\`. Runs on the pump thread.
- **Terminal server `*TADADM`** on our node: a SINTRAN user does `CONNECT-TO <our node>` and gets our menu
  (stat, who, tell, wall, list servers/route, chat). Users from RetroServer users instead of `tadUsers`.
- **Chat server** (`Xmsg.Chat`) - on/off.
- **One-shot transfers from the admin side**: push/pull a file to/from a machine via REST and RetroAdmin.
- **Status and management**: REST `xmsg/status` (node, links, peers, sequence, servers, session counts, routes),
  `xmsg/sessions`, SignalR push, log lines into the admin log view. RetroAdmin XMSG page.
- **Capture**: optional pcap of each link (the hub already has `PcapWriter`), off by default.
- **Safety**: refuse to start if `xmsgrunner` with the same system number runs on the box; show the number
  prominently; move existing runner state files into `<data>\xmsg\peers\` on first start when asked.

### Phase 6 - all protocols off by default; users, groups and rights

- Each protocol (TNFS, Econet, DriveWire, XMSG and its parts) has `Enabled`, default **false**, in one place.
  Today TNFS and DriveWire default to on: on first start after the update, a config file without the switch gets
  the old value written in; a fresh data dir gets every protocol off.
- `RetroUser`: `AllowXmsg`, `LastLoginXmsgAt`, plus finer XMSG rights (file server, gateway, sync, admin console).
- Groups carry rights; a user's effective right = user setting, else any group's, else off.
- Home/root folder per user for the FA server, defaulting to `<root>\<USERNAME>`.
- Console rights: console login, XMSG connect, X.25 call, each per user and per group.
- REST + RetroAdmin pages for users, groups and rights (`UsersController`, `UsersView`).

### Phase 7 - X.25: routing, XOT, and X.25 calls from the console

What exists (read 2026-11-08): the switch engine `RetroIO.Protocols.X25.Switch` (routes a call by address to a
port, joins the two circuits, counters, call records), `XotSwitchPort`, `XotServerTransport`/`XotClientTransport`
(`RetroIO.Protocols.X25\src\Routing\`), `XotLink`, `PadClient` with X.3/X.28/X.29, the SINTRAN-over-X.25 package,
and a telnet admin console for the switch (`X25.Switch.Management`). The program that assembles a switch from
config is `x25switch` in RetroX25 (`RetroX25\src\RetroX25.Switch`, ~900 lines) - the same problem as xmsgrunner.
- `RetroIO.Protocols.X25.Hosting`: move the switch assembly (config, ports, routes, start/stop) out of
  `x25switch`; `x25switch` then uses it. Same pattern as Phase 1.
- Ports in RetroServer: XOT listen (many routers/peers), XOT dial-out, and LAPB over TCP for emulators that speak
  X.25 on an HDLC line (nd100x `--hdlc=N:HOST:PORT`, many at once - the same accept loop as Phase 2).
- Routing table: address prefix -> port, with XOT peers as ports, so a call that arrives on one XOT connection
  can go out on another (routed XOT). Configurable in `x25-config.json` and the UI.
- Console `call <address | name>`: places a call through the switch and joins it to the console session; the
  same escape key returns to the menu and clears the call. Named hosts come from the config.
- Status and management: ports, circuits, routes, call records over REST and in RetroAdmin, reusing what the
  switch already counts.
- Same rules as XMSG: one thread for switch state (fix the timer locking if it is still unlocked), clean
  teardown (clear every circuit, then DISC) before the sockets close.

### Phase 8 - RetroAdmin shows every setting (Ronny 2026-11-10: "make sure all configuration for retroserver is available in the retroserver UI admin tool")

Outstanding (everything else from the 2026-11-10 inventory is done; the two "every setting has a field" tests
guard it):
- `server-settings.json` and `api/v1/settings/server` are read by nothing: remove them (RetroIO BUGS.md R row).
- The host switches in `RetroServerOptions` (EnableTnfs ... EnableXmsgNode, Swagger, banners, log size) are never
  set by RetroServer: joins Phase 6, every protocol off by default with the old value written into an existing
  install's files.
- Each later phase adds its own pages as it lands.

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

## Verification

- Unit tests for every moved class (they move with it), plus new ones: topology validation, per-peer store keying,
  two links claiming one system number, path traversal in per-user FA, N gateway sessions on one link, Ethernet
  inbound queue, shutdown ladder on both transports, config loader overrides.
- `dotnet build` and `dotnet test` with `-nodeReuse:false`, from `src\` in RetroIO, RetroXmsg and RetroServer; read
  the counts.
- `xmsgrunner` still reaches D100 after the move (regression on the real lab).
- End to end, on the lab: two nd100x emulators with `--hdlc=1:<server>:10364` both link up at once; from each,
  `@LIST-FILES` on our node lists that USER's folder; a file copied from one machine lands in `<root>\<USER>\`;
  RetroTerm to `<server>:2023`, log in, `connect d100`, log in as SYSTEM, `@WHO`, escape back; stop the service and
  confirm with `X-C LIST-SYSTEMS` on D100 that the link went down cleanly (no repeated link-setup frames).
- Phase 6: a fresh data dir starts with every protocol off; each switch on brings exactly that protocol up.
- Phase 7 (against x25emu in WSL, no SINTRAN): a call from the console through RetroServer reaches an x25emu host over XOT; a call arriving on one XOT
  connection is routed out on another; with X.25 off the console has no `call` option; a user without the
  X.25 right does not see it.
- Phase 8: the "every config key has a UI field" test passes; each setting changed in RetroAdmin lands in the file.
