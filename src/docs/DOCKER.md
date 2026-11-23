# RetroServer in a Docker container

RetroServer can run as a Linux container. It is an option beside the normal Windows and Linux install.

## Build the image

From `src`:

```powershell
./eng/build-docker.ps1                 # image retroserver:local
./eng/build-docker.ps1 -Tag retroserver:1.0
```

The script publishes RetroServer for linux-x64 into `docker/publish`, builds `docker/Dockerfile` from it
and deletes the publish folder again. The build happens outside Docker because RetroServer is built
against RetroIO packages from a package folder on the build machine.

## Run it

```powershell
docker run -d --name retroserver -v retroserver-data:/data -p 5000:5000 retroserver:local
docker stop -t 30 retroserver
```

- **All data is in `/data`**: the settings files (`tnfs-config.json`, `xmsghub-config.json`, ...), users,
  state files, served folders and logs. Mount a volume or a folder there, or the data goes with the
  container.
- **Health check:** `GET /health` on the management port answers `OK` with no login.
- **Stopping:** `docker stop` sends SIGTERM, which stops RetroServer's services in order. The XMSG node
  must log its TAD users out before its links close, or a real SINTRAN machine's XMSG can crash, so give
  it time with `-t 30`.

## Every protocol starts off

A fresh `/data` starts with every protocol switched off. Switch one on in its settings file in `/data`, or for
the run with an environment variable such as `Tnfs__Enabled=true`.

## Settings from environment variables

Every setting that works on the command line as `--Part:Key=value` also works as an environment variable
`Part__Key=value` (two underscores), for example:

```powershell
docker run -d -e XmsgHub__Enabled=true -e XmsgHub__BindAddress=0.0.0.0 -p 5010:5010 ...
```

The image sets two itself: `RETROFS_DATA_PATH=/data` and `Management__Urls=http://0.0.0.0:5000`.

To send statistics and logs to an OpenTelemetry collector, set `Telemetry__OtlpEndpoint`; see
[TELEMETRY.md](TELEMETRY.md).

## Listen addresses

Inside a container, a service listening on `127.0.0.1` cannot be reached from outside it, even with the
port published. So the image sets the XMSG hub, DriveWire and dial-up to listen on `0.0.0.0`
(`XmsgHub__BindAddress`, `DriveWire__BindAddress`, `DialUp__BindAddress`); each is still off until you
switch it on. Checked 2026-11-23: the hub in a container answers a connection from the PC through a
published port.

The XMSG node's plug-in port stays on `127.0.0.1`: plug-in programs outside the container are not
supported until that port has TLS and a login.

A connection through Docker's port mapping does not come from `127.0.0.1` inside the container, so
anything that trusts 127.0.0.1 more than other addresses treats those callers as remote.

## Ports to publish

Default ports, published only for the protocols you switch on:

| Part | Default port |
|---|---|
| Management API (RetroAdmin) | 5000 TCP |
| TNFS | 16384 UDP and TCP |
| Econet (AUN) | 32768 UDP |
| DriveWire | from `drivewire-config.json` (a fresh one picks a port) |
| XMSG hub | 5010 TCP |
| XMSG node, programs (gRPC) | 5011 TCP |
| Dial-up | 6400 TCP |

## Checking an image

`./eng/test-docker.ps1 -Tag retroserver:local` starts the image, waits up to 60 seconds for `/health`
to answer `OK`, then removes the container and its data volume. The CI build (job `docker`) builds
the image and runs this check on every push.

## Not done yet

- Windows-only parts have no Linux way yet: a certificate from the Windows certificate store, printing to
  a system printer, serial devices.
