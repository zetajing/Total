## Description

The package **'Beckhoff.TwinCAT.Ads.Server'** contains the base framework to create your own ADS Server / virtual ADS Device.

## Requirements

- **.NET 10.0**, **.NET 8.0** or **.NET Standard 2.0** (e.g. >= **.NET Framework 4.61**) compatible SDK
- A **TwinCAT 2.11** Build (XAE, XAR or ADS Setup) or later.

> **Note:** Accessing the local (not remote) TwinCAT 2.11 system requires the TcAmsServer.dll in the correct bitness, which is not available in all TwinCAT 2.11 installations and must be deployed manually. For TwinCAT 2.11 64-Bit Engineering it should reside in the C:\TwinCAT\Common32 and C:\TwinCAT\Common64 directories. For TwinCAT 2.11 32-Bit installations the location is C:\Windows\System32. Contact Beckhoff support for these DLLs (32-Bit and 64-Bit) if needed.

## Installation

### Beckhoff.TwinCAT.Ads Version 7.X
The 7.X series of this package now supports TwinCAT 2.11 and upwards locally.

### Beckhoff.TwinCAT.Ads Version 6.X limitations

Because the Beckhoff.TwinCAT.Ads Version 6.X uses internal interfaces that are available only from TwinCAT 4024.10 on, an appropriate
version must be installed locally. The package doesn't work with older installations. An alternative approach for some use cases is
to use the 'Beckhoff.TwinCAT.Ads.AdsRouterConsole' / 'Beckhoff.TwinCAT.Ads.TcpRouter' packages to establish your own router.

### Systems without TwinCAT Installation

There are options to run AdsClient/AdsServer instances without having a full TwinCAT system installed on the host system.

- Use of the TwinCAT UM Runtime on supported systems
- Usage of AdsOverMqtt via a Mqtt Broker (Beckhoff.TwinCAT.Ads >= 6.2 necessary)
- Running a slim (feature reduced) .NET implemented ADS Router (formerly AdsRouterConsole). The implementation is in the 'Beckhoff.TwinCAT.Ads.TcpRouter' package

For more information please have a look at:

- [Beckhoff GitHub RouterSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/RouterSamples)
- [Beckhoff GitHub DockerSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/DockerSamples)

### Version Support

Historical package versions remain available for compatibility and traceability purposes.

**For new projects and production use, always use the latest available version.** Older versions may contain known defects, including cybersecurity vulnerabilities, and may no longer receive updates or support.

Please refer to the package version history and release notes for the current release.

### Version Support lifecycle

| Package | Description | .NET Framework | TwinCAT |
|---------|-------------|----------------|---------|
| 7.0 | Package basing on .NET 10.0 | net10.0, net8.0, netstandard2.0 | >= 2.11 [^1] |
| 6.2 | Package basing on .NET 8.0/6.0 | net8.0, net6.0[^2], netstandard2.0 | >= 3.1.4024.10 [^1] |
| 6.1 | Package basing on .NET 7.0/6.0[^2] | net7.0, net6.0, netstandard2.0 | >= 3.1.4024.10 [^1] |
| 6.0 | Package basing on .NET 6.0 | net6.0, netcoreapp3.1, netstandard2.0, net461 | >= 3.1.4024.10 [^1] |
| 4.x | Package basing on .NET Framework 4.0 | net4 | All |

[^1]: Requirement on the Host system. No version limitation in remote system communication.
[^2]: Microsoft support for .NET6/.NET7 has ended. Therefore it is recommended to update .NET Applications to Version 8.

- [Migrating to the latest .NET](https://docs.microsoft.com/en-us/dotnet/architecture/modernize-desktop/example-migration)
- [Microsoft .NET support lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

## First Steps

Create your customized ADS Server by deriving the TwinCAT.Ads.Server.AdsServer class. Fill the virtual handlers with your own
code.

```csharp
using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TwinCAT.Ads;
using TwinCAT.Ads.Server;

namespace TestServer
{
    /*
     * Extend the AdsServer class to implement your own ADS server.
     */
    public class AdsSampleServer : AdsServer
    {
        /// <summary>
        /// Fixed ADS Port (to be changed ...)
        /// </summary>
        const ushort ADS_PORT = 42;

        /// <summary>
        /// Fixed Name for the ADS Port (change this ...)
        /// </summary>
        const string ADS_PORT_NAME = "AdsSampleServer_Port42";


        /// <summary>
        /// Logger
        /// </summary>
        private ILogger _logger;

        /* Instantiate an ADS server with a fix ADS port assigned by the ADS router.
        */


        public AdsSampleServer(ILogger logger) : base(ADS_PORT, ADS_PORT_NAME)
        {
            _logger = logger;
        }

        // Override Functions to implement customized Server
        ....
    }
}
```

## Further documentation

The actual version of the documentation is available in the Beckhoff Infosys:

[Beckhoff Information System](https://infosys.beckhoff.com/index.php?content=../content/1033/tc3_ads.net/index.html&id=207622008965200265)

## Sample Code

Demo Code for AdsServer implementations can be found here:

- [Beckhoff GitHub ServerSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/ServerSamples)
- [Beckhoff GitHub RouterSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/RouterSamples)
- [Beckhoff GitHub DockerSamples](https://github.com/Beckhoff/TF6000_ADS_DOTNET_V5_Samples/tree/main/Sources/DockerSamples)

