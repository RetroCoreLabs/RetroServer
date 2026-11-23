# Statistics and logs (telemetry)

Every part of RetroServer reports its statistics the same way: named counters with labels, like a
Windows perfmon counter list, and its log lines kept right beside them. You can read them in three
places: the RetroAdmin Telemetry page, the management API, and an OpenTelemetry collector.

## The parts and their names

Each part is a *source*. A source has a name, a set of metrics and its own log.

| Source | Metrics |
|---|---|
| `RetroIO.Tnfs` | `retroio.tnfs.requests`, `.io`, `.files.opened`, `.uptime` |
| `RetroIO.Econet` | `retroio.econet.requests`, `.io`, `.errors`, `.uptime` |
| `RetroIO.DriveWire` | `retroio.drivewire.connections`, `.connections.active`, `.transactions`, `.sectors`, `.io`, `.errors`, `.retries`, `.resets` |
| `RetroIO.XmsgHub` | `retroio.xmsghub.members`, `.machines`, `.frames.received`, `.frames.forwarded`, `.frames.dropped`, `.frames.captured` |
| `RetroIO.XmsgNode` | `retroio.xmsgnode.link.up`, `.programs`, `.connections.opened`, `.datagrams.parked` |
| `RetroIO.DialUp` | `retroio.dialup.calls.active`, `.calls.answered`, `.calls.refused`, `.calls.limit`, `.logins.refused`, `.io` |

A source appears only while its protocol is switched on. Every protocol starts off.

Names follow the OpenTelemetry style: `retroio.<part>.<thing>`, lower case. Where one name covers
several counters (for example bytes in and bytes out), labels tell them apart.

## RetroAdmin

The **Telemetry** page lists the sources. Pick one to see its metrics and its log; both refresh
every 2 seconds.

## Management API

All three calls need a login (`POST /api/v1/auth/login`, then `Authorization: Bearer <token>`).

| Call | Answer |
|---|---|
| `GET /api/v1/telemetry/sources` | the sources and how many series each has |
| `GET /api/v1/telemetry/metrics?source=RetroIO.Tnfs` | every metric of that source, with its labels and value |
| `GET /api/v1/telemetry/log?source=RetroIO.Tnfs&after=0&limit=1000` | the log lines after line number `after` |

## Sending to an OpenTelemetry collector

Off unless you set an address. When it is set, RetroServer sends every source's metrics and its
whole log to the collector over OTLP/HTTP (the standard OpenTelemetry protocol over HTTP, port 4318
by default).

| Setting | What it does | Default |
|---|---|---|
| `--Telemetry:OtlpEndpoint=<url>` | the collector's address, for example `http://collector:4318`. RetroServer adds `/v1/metrics` and `/v1/logs` itself. | off |
| `--Telemetry:ExportIntervalSeconds=<n>` | how often the metrics are sent | 60 |

The same settings work as environment variables, which suits Docker:
`Telemetry__OtlpEndpoint=http://collector:4318`. See [DOCKER.md](DOCKER.md).

What arrives:

- service name `RetroServer`;
- metrics from every meter whose name starts with `RetroIO.`;
- the log, with the extra fields of each line (a part's labels) as attributes.

A bad address (not `http` or `https`) is reported at start and nothing is sent. The log goes out
in batches every few seconds.

Only HTTP is supported, not gRPC (port 4317).

Checked on 2026-11-23 by running RetroServer with the XMSG hub on against a local HTTP listener:
the hub's metrics and its log lines both arrived.
