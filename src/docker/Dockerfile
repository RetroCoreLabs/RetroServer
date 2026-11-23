# RetroServer as a Linux container. Build it with eng/build-docker.ps1, which first publishes
# RetroServer for linux-x64 into docker/publish (RetroServer builds against RetroIO packages from a
# folder on the build machine, so the build happens there and only the result goes into the image).
# How to run it: docs/DOCKER.md.

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY publish/ .

# All data in one folder, mounted as a volume: settings files, users, state files, served folders, logs.
ENV RETROFS_DATA_PATH=/data
VOLUME /data

# Inside a container "localhost" cannot be reached from outside, so the management API listens on
# every address. Every protocol stays off until its settings file (or an environment variable such as
# Tnfs__Enabled=true) switches it on.
ENV Management__Urls=http://0.0.0.0:5000

# The same for the protocol listeners that default to 127.0.0.1 (each still off until switched on).
# The XMSG node's plug-in port (XmsgNode__ProgramBindAddress) stays on 127.0.0.1 on purpose: a plug-in
# program from outside the container would need TLS and a login first (plan: "Plug-in XMSG servers").
ENV XmsgHub__BindAddress=0.0.0.0     DriveWire__BindAddress=0.0.0.0     DialUp__BindAddress=0.0.0.0

# The management API. The protocol ports are published as each one is switched on (docs/DOCKER.md).
EXPOSE 5000

# SIGTERM is .NET's normal stop: the hosted services stop in order, so the XMSG node logs its TAD
# users out before the links close. Give it time with "docker stop -t 30".
ENTRYPOINT ["dotnet", "RetroServer.dll"]
