## Description

The package **'Beckhoff.TwinCAT.Ads.TcpRouter'** implements a lean TCP ADS Router class to use on systems where no standard TwinCAT router is established or available.

It is running in UserMode only (no realtime characteristics) and contains only limited functionality than distributing the ADS Frames and Route handling (no ADS Secure). It is just used to route ADS frames locally between AdsServers 
and to/from remote ADS devices.

The router itself hosts **no ADS server** — neither the AMS Router port 1 nor the System Service port 10000 are answered by this package.
Add the **'Beckhoff.TwinCAT.Ads.SystemServer'** package to run an AMS Router server (Port 1) and a stripped down System Service (Port 10000)
next to the router. This is exactly what the ready-to-run **'Beckhoff.TwinCAT.Ads.AdsRouterConsole'** application does.

The Package is implemented in asynchronous .NET Code it can be run in your own services/daemon, as standalone console application and also in your customized application.

> **Intended use — please read before deploying**
>
> This package is intended to provide a **minimal TwinCAT Router** functionality for scenarios in which the standard/full TwinCAT
> components are **not available** (e.g. on platforms that TwinCAT does not support) or **not appropriate** (unit testing, Docker and
> other container scenarios).
>
> Because it has **functional limitations and lower security claims** than the full TwinCAT Router — for instance there is currently
> **no integrated ADS Secure support** — it should only be used in specifically secured environments and/or outside of productive use.

## Requirements

- **.NET 10.0**, **.NET 8.0**, or **.NET Standard 2.0** (e.g. >= **.NET Framework 4.61**) compatible SDK
- No other System allocating the same port (e.g. a regular TwinCAT installation).

## Installation

Along with the deployment of the application where the TcpRouter is implemented (a host application), a valid Router / ADS configuration must be placed to specify
the Local Net ID, the name and the default port of the Router system.

The preferred way to configure the system is with standard Configuration providers, which are part of the
.NET Core / ASP .NET Core infrastructure.

For more information how to implement and deploy your own Router please have a look at:

- [Beckhoff GitHub RouterSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/RouterSamples)
- [Beckhoff GitHub DockerSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/DockerSamples)
- [Microsoft Learn: Configuration in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)

This enables common options for application configuration that can be used 'out-of-the-box':

- Via the file appsettings.json
- With the StaticRoutesConfigurationProvider (StaticRoutes.xml)
- Using Environment Variables.
- Command line arguments
- etc.

The configuration has to be loaded during application startup and is placed into the **'TwinCAT.Ads.TcpRouter.AmsTcpIpRouter'** class via constructor dependency injection and
must contain the following information:

- The name of the local System (usually the Computer or Hostname)
- The Local AmsNetId of the local system as Unique Address in the network
- Optionally the used TcpPort (48898 or 0xBF02 by default)
- The static routes in the 'RemoteConnections' list.
- Logging configuration.

Actually the configuration is not reloaded during the runtime of the **'TwinCAT.Ads.TcpRouter.AmsTcpIpRouter'** class.
Please be aware that the "Backroute" from the Remote system linking to the local system (via AmsNetId) is necessary also to get functional routes.

Example for a valid 'appSettings.json' file (please change the Addresses for your network/systems.)

```json
{
  "AmsRouter": {
    "Name": "MyLocalSystem",
    "NetId": "192.168.1.20.1.1",
    "TcpPort": 48898,
    "RemoteConnections": [
      {
        "Name": "RemoteSystem1",
        "Address": "RemoteSystem1",
        "NetId": "192.168.1.21.1.1",
        "Type": "TCP_IP"
      },
      {
        "Name": "RemoteSystem2",
        "Address": "192.168.1.22",
        "NetId": "192.168.1.22.1.1",
        "Type": "TCP_IP"
      }
    ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "System": "Information",
      "Microsoft": "Information"
    },
    "Console": {
      "IncludeScopes": true
    }
  }
}
```

Alternatively a "StaticRoutes.Xml" Xml File can configure the system equally. Don't forget to add the **'StaticRoutesXmlConfigurationProvider'** to the Host configuration
during startup (see FirstSteps below).

An example of the local "StaticRoutes.xml" is given here:

```xml
<?xml version="1.0" encoding="utf-8"?>
<TcConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="C:\TwinCAT3\Config\TcConfig.xsd">
  <Local>
      <Name>MyLocalSystem</Name>
      <NetId>192.168.1.20.1.1</NetId> <!-- Local NetId -->
      <TcpPort>48898</TcpPort> <!-- Default TcpPort -->
  </Local>
  <RemoteConnections>
    <Route>
      <Name>RemoteSystem1</Name>
      <Address>RemoteSytem</Address> <!-- HostName -->
      <!--<Address>192.168.1.21</Address>  --> <!--IPAddress -->
      <NetId>192.168.1.21.1.1</NetId>
      <Type>TCP_IP</Type>
    </Route>
    <Route>
      <Name>RemoteSystem2</Name>
      <Address>192.168.1.22</Address> <!-- IPAddress -->
      <!--<Address>RemoteSystem2</Address>  --> <!--HostName -->
      <NetId>192.168.1.21.1.1</NetId>
      <Type>TCP_IP</Type>
    </Route>
  </RemoteConnections>
</TcConfig>
```

As further option, the configuration can also be set via Environment variables.

```Powershell
PS> $env:AmsRouter:Name = 'MyLocalSystem'
PS> $env:AmsRouter:NetId = '192.168.1.20.1.1'
PS> $env:AmsRouter:TcpPort = 48898
PS> $env:AmsRouter:RemoteConnections:0:Name = 'RemoteSystem1'
PS> $env:AmsRouter:RemoteConnections:0:Address = 'RemoteSystem1'
PS> $env:AmsRouter:RemoteConnections:0:NetId = '192.168.1.21.1.1'
PS> $env:AmsRouter:RemoteConnections:1:Name = 'RemoteSystem2'
PS> $env:AmsRouter:RemoteConnections:1:Address = '192.168.1.22'
PS> $env:AmsRouter:RemoteConnections:1:NetId = '192.168.1.22.1.1'
PS> $env:AmsRouter:Logging:LogLevel:Default = 'Information'
```

```Powershell
PS> dir env: | where Name -like AmsRouter* | format-table -AutoSize

Name                                  Value
----                                  -----
AmsRouter:Name                        MyLocalSystem
AmsRouter:NetId                       192.168.1.20.1.1
AmsRouter:TcpPort                     48898
AmsRouter:RemoteConnections:0:Name    RemoteSystem1
AmsRouter:RemoteConnections:0:Address RemoteSystem1
AmsRouter:RemoteConnections:0:NetId   192.168.1.21.1.1
AmsRouter:RemoteConnections:1:Name    RemoteSystem2
AmsRouter:RemoteConnections:1:Address 192.168.1.22
AmsRouter:RemoteConnections:1:NetId   192.168.1.22.1.1
AmsRouter:Logging:LogLevel:Default    Information
```

### Configuration Parameters

| Name | Description |
| ---- | ----------- |
| Name | Name of the local System/Device |
| NetId | The AmsNetId of the local System/device |
| TcpPort | The TCP port used for external communication (communication to the routes/RemoteConnections)|
| ChannelPortType | The socket/channel type used for System/Device internal communication: ChannelPortType.All (default), ChannelPortType.UnixSocket, ChannelPortType.Loopback

#### Configuration Parameters for ChannelPortType.UnixSocket
The used UnixSocketPath/UnixSocket address can be configured via the **TWINCAT3AMSPATH** environment variable. By default TwinCAT 4026 versions with activated UnixSockets (by registry)
uses the following default (**C:\ProgramData\Beckhoff\TwinCAT\3.1\Ams\tcsyssrv.ams.sock**). If the **Beckhoff.TwinCAT.Ads.TcpRouter** package is used in parallel to a TwinCAT 4026
installation, a different path must be set.

The AmsRouter contained in this package determines the UnixSocketPath in the following order:
- **TWINCAT3AMSPATH** environmental variable
- Value of the 'AmsUnixSocketDir' value in registry path 'SOFTWARE\\WOW6432Node\\Beckhoff\\TwinCAT3\\3.1'

If no valid UnixSocketPath is found, the Router falls back to ChannelPortType.Loopback.

#### Configuration Parameters for ChannelPortType.Loopback

| Name | Description |
| ---- | ----------- |
| LoopbackIP | This is the IPAddress, that is used by the TcpRouter for its Loopback Connections (in combination with the LoopbackPort. By default this is set to IPAddress.Loopback (127.0.0.1) and is only accessible from the local machine. If AdsClient/AdsServers should run separated from the Router System, this LoopbackIP must be set to valid local IPAddress. Furthermore valid external addresses (where the AdsClients/AdsServer lives) must be specified via LoopbackExternalIPs or LoopbackExternalSubnet. Only those connections will be accepted|
| LoopbackPort | Sets the TCP Port that is used for the loopback. The LoopbackPort defines the Loopback **TcpEndpoints** in combination with the **LoopbackIP** | 
| LoopbackExternalIPs | The Loopback externals are IPAddresses, that are allowed to use the Loopback connection. Use this IP list or specify alternatively the **LoopbackExternalSubnet**|
| LoopbackExternalSubnet | Sets the loopback externals subnet. This is an alternative approach to set the allowed **'LoopbackIPs'** for loopback communication. In docker/virtual environments often a whole subnet will be spanned|
| RemoteConnections | Sets the list of remote Routes/Connections. This is the list of external devices which can be reached via the route.|

### Version Support

Historical package versions remain available for compatibility and traceability purposes.

**For new projects and production use, always use the latest available version.** Older versions may contain known defects, including cybersecurity vulnerabilities, and may no longer receive updates or support.

Please refer to the package version history and release notes for the current release.

### Version Support lifecycle

| Package | Description | .NET Framework |
|---------|-------------|----------------|
| 7.0 | Package basing on .NET 10.0 | net10.0, net8.0, netstandard2.0 |
| 6.2 | Package basing on .NET 8.0/6.0 | net8.0, net6.0, netstandard2.0 |
| 6.0 | Package basing on .NET 6.0 | net6.0, netcoreapp3.1, netstandard2.0, net461 |

- [Migrating to the latest .NET](https://docs.microsoft.com/en-us/dotnet/architecture/modernize-desktop/example-migration)
- [Microsoft .NET support lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

## First Steps

For first steps, please have a look at:
[Beckhoff GitHub RouterSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/RouterSamples)

## Further documentation and Sample Code

The actual version of the documentation is available in the Beckhoff Infosys.

- [Beckhoff Information System](https://infosys.beckhoff.com/index.php?content=../content/1033/tc3_ads.net/index.html&id=207622008965200265)
- [Beckhoff GitHub RouterSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/RouterSamples)
- [Beckhoff GitHub DockerSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/DockerSamples)
