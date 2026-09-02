## Description

The package **'Beckhoff.TwinCAT.Ads.Abstractions'** contains interfaces and base implementations for the **'Beckhoff.TwinCAT.Ads.Server'** and
**'Beckhoff.TwinCAT.Ads'** packages. It is never used standalone and is a dependency of the above-named packages.

## Requirements

- **.NET 10.0**, **.NET 8.0** or **.NET Standard 2.0** (e.g. >= **.NET Framework 4.61**) compatible SDK
- A **TwinCAT 2.11** Build (XAE, XAR or ADS Setup) or later.

> **Note:** Accessing the local (not remote) TwinCAT 2.11 system requires the TcAmsServer.dll in the correct bitness, which is not available in all TwinCAT 2.11 installations and must be deployed manually. For TwinCAT 2.11 64-Bit Engineering it should reside in the C:\TwinCAT\Common32 and C:\TwinCAT\Common64 directories. For TwinCAT 2.11 32-Bit installations the location is C:\Windows\System32. Contact Beckhoff support for these DLLs (32-Bit and 64-Bit) if needed.

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

## Installation

As dependency of other Beckhoff packages

## Further documentation

The actual version of the documentation is available in the Beckhoff Infosys.

[Beckhoff Information System](https://infosys.beckhoff.com/index.php?content=../content/1033/tc3_ads.net/index.html&id=207622008965200265)