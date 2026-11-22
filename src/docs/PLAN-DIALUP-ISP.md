# Plan: dial-up ISP in RetroServer (SLIP, then PPP)

**Next:** build step 1 - the dial-up listener (TCP port, text login with the "dial-up" right, then SLIP onto `RetroIO.Network.Router` on 10.42.0.0/24, pool or fixed address). NAT (step 2) after the subnet question to RetroIO is answered.

## What Ronny decided (2026-11-22)

- Order of where a caller's IP packets go: **1. RetroServer's own services, 2. the internet through NAT,
  3. a bridge to the real LAN** ("do 1 then 2 then 3").
- Logins use RetroServer's existing users and passwords, both for a text login before SLIP and later for
  PPP's PAP/CHAP.
- Every caller sits on one dial-up subnet with its own reachable address, so other callers and the host can
  connect back to it. NAT to the internet is an extra, not the base.
- The router (point-to-point links, address pool, routing between them and the services, optional NAT) is a
  **shared network package**, used by RetroServer and by RetroCore, not RetroServer-only code.

## Facts (from the RetroCore session, not checked here)

- RetroCore's emulated Hayes modem already dials a TCP address in the dial string (`host:port`) through
  its TcpPhoneLine. RetroCore needs nothing new to reach RetroServer.
- RetroCore's NAT is in `Nuget/HackerCorpLabs.Emulation.Network/src/Nat` (RetroCore repository). It takes
  Ethernet frames; an entry for bare IP packets is being added.
- RetroIO will hold the protocols: `RetroIO.Protocols.Slip` (RFC 1055, CSLIP RFC 1144 later) and later
  `RetroIO.Protocols.Ppp` (RFC 1661/1662, PAP/CHAP, IPCP). No threads or timers: line bytes in, whole IP
  packets out, and back.

## Checked in this repository and RetroIO (2026-11-22)

No modem answer side, no SLIP/PPP, no IP router or NAT exists in RetroIO or RetroServer.
`RetroIO.Protocols.Telnet` exists.

## The pieces

1. **Shared router package in RetroIO** (name to agree with the RetroIO session, for example
   `RetroIO.Network.Router`): a list of point-to-point links, each with its own address from a pool; a
   routing table between them and a "local services" interface; a hook for NAT. Single-threaded: whoever
   owns it pumps it, the same rule as the XMSG node.
2. **Dial-up service in RetroServer**, off by default, same pattern as the other protocols (settings file,
   command-line override, REST, RetroAdmin page):
   - a TCP port that acts as the phone line (serial line later), many callers at once;
   - text login `login:`/`password:` checked against RetroServer users, then the line switches to SLIP;
     PPP's PAP/CHAP use the same users later;
   - each call becomes one point-to-point link on the shared router.
3. **Step 1 - own services:** the subnet reaches RetroServer's own IP services (TNFS over UDP first),
   and callers reach each other.
4. **Step 2 - NAT to the internet:** TCP/UDP through the PC's own sockets, built on the shared package.
5. **Step 3 - bridge to the real LAN:** needs a TAP driver or a route on the host, which needs admin rights
   and changes the host's network settings, so Ronny picks the exact form when we get there.

## Users

- New right on a RetroServer user: **dial-up** (off by default). Without it the login is refused.
- Optional per-user **fixed IP address**; without one the caller gets the next free address from the pool.
  This lets other machines connect back to a known address.

## Trade-offs, plainly

- Real subnet addresses: other machines can reach callers, but the host only reaches the subnet if it has a
  route to RetroServer (admin rights, changes host settings), or through step 3.
- NAT: works with no host changes, but nothing outside can open a connection to a caller.

## More decisions (Ronny, 2026-11-22)

- RetroCore's NAT moves onto the shared RetroIO package, so there is one copy. RetroCore switches once the
  package is in the local feed with tests.
- Subnet: **10.42.0.0/24** to start (RetroServer is 10.42.0.1); the size is a setting, so a larger subnet
  than /24 can be configured when needed.

## Open questions for Ronny

- Default TCP port for the phone line: not chosen yet (proposal follows with the service).

## Agreed with the RetroIO session (2026-11-22)

- `RetroIO.Network.Router` and `RetroIO.Protocols.Slip` keep their names; the router's API is settled
  (`IpRouter`, `AddLink`, `RemoveLink`, `SendFromLocal`, `OnLocal`, `OnUpstream`, `OnDrop`), plus
  `SendFromUpstream(packet)` for replies coming back from the NAT (RetroIO ca92d09).
- NAT: today's `IpNat` in `RetroIO.Network.Nat` is being deleted and replaced by RetroCore's NAT (`NatRouter`,
  `NatEthernetBackend`, same package and namespace). Do NOT build on `IpNat`; wait for RetroIO's message that
  the moved NAT is in. Hookup: `router.OnUpstream` into the NAT's IP-packet entry, the NAT's `OnPacketReceived`
  into `router.SendFromUpstream`.
- Also moving from RetroCore, no date yet: Ethernet links, hub, multicast and TAP as `RetroIO.Network.Ethernet`,
  and pcap as `RetroIO.Network.Pcap`. The LAN bridge (step 3) uses those.
- Step 1 (own services over SLIP) needs only the router and can start now.

## Decided by Ronny (2026-11-22, second round)

- **Who may dial in:** only users an admin has ticked. A new "dial-up" right per user and per group, off by
  default, set in RetroAdmin.
- **Caller address:** a free address from the pool; an admin can give a user a fixed address in the profile. A
  call whose fixed address is already in use is refused.
- **The phone line:** the machine dials with the virtual modem that already exists in RetroCore
  (`HackerCorpLabs.Emulation.Transports.Modem`, `HayesModemTransport`: `ATDT host:port` opens a TCP connection
  and prints `CONNECT` once it is up - checked in its `Dial` and `OnInnerStateChanged`). So RetroServer only
  listens on a TCP port and starts with the text login; it does not answer AT commands itself.
  - Old operating systems that only dial digits: the MODEM gets a table that maps phone numbers to
    `host:port` (Ronny). It does not have one today (checked 2026-11-22); that is RetroCore's change, asked of the
    RetroCore session.

## Open with the RetroIO session

- The moved NAT (RetroIO 9a68185) is set up on 10.0.2.0/24 (router 10.0.2.1, gateway 10.0.2.2, name server
  10.0.2.3). The dial-up subnet is 10.42.0.0/24 (Ronny). Asked whether the NAT works on any subnet.

