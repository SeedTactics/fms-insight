---
id: server-config
title: Server Configuration
sidebar_label: Configuration
---

The FMS Insight server is configured via a file
`SeedTactics\FMSInsight\config.ini` located in the Windows Common AppData
folder. On a default Windows install, this has a full path of
`c:\ProgramData\SeedTactics\FMSInsight\config.ini`. The config file itself
describes the settings in detail, but they fall into three groups: server
settings such as network ports and SSL, FMS settings such as automatic serial
assignment, and finally cell controller specific settings.

The first time FMS Insight starts, if the `config.ini` file does not exist,
FMS Insight will create a default config file. FMS Insight should run fine
with the default settings, so typically when first getting started no config
file settings need to be changed. Any changes to settings in `config.ini`
require the FMS Insight server to be restarted before they take effect.

The `config.ini` file is an [INI file](https://en.wikipedia.org/wiki/INI_file).
The default config file contains a large number of comments describing each option.
Comments describing each option are prefixed by a hash (`#`) and the options themselves
are prefixed by a semicolon (`;`).  To edit an option, remove the leading semicolon (`;`)
and then edit the option value.  Remember to restart the FMS Insight server.  If any configuration
errors are present, FMS Insight will generate an error message with details into the
[event log](server-errors.md).

## Databases

FMS Insight uses [SQLite](https://www.sqlite.org/) to store data about the cell.
The primary data stored is a log of the activity of all parts, pallets, machines, jobs,
schedules, inspections, serial numbers, orders, and other data.  In addition, FMS Insight
stores a small amount of temporary data to assist with monitoring.

The SQLite databases are by default stored locally in the Windows Common
AppData folder which on a default Windows install is
`c:\ProgramData\SeedTactics\FMSInsight`. This path can be changed in the
`config.ini` file if needed, but we suggest that the databases are kept on
the local drive for performance and data integrity (SQlite works best on a
local drive). The SQLite databases are not large, typically under 100
megabytes, so disk space is not a problem.

Every time FMS Insight starts, it checks for the existence of the SQLite
databases and if they are missing it creates them. For this reason, the
databases can be deleted without impacting the operation of the cell (as long
as FMS Insight is restarted). Also, since the databases just store a log of
past activity and all complex logic is outside FMS Insight, if the databases
are deleted (and recreated) the cell will continue to operate just fine in
the future (minus the historical data). Thus it is not necessary to backup
the databases (but of course you can if you want to preserve historical
data).

## Secure HTTPS

By default, FMS Insight does not use security and all communication is over HTTP.
To enable HTTPS, a server certificate must be created and stored in a file, and the
path to this file must be added to the `config.ini` file in the `TLSCertFile` setting.

If you have an enterprise root certificate authority pre-installed on the
devices (usually as part of them joining the local AD domain), a private
certificate signed by the certificate authority should be created and then
used by specifying the path to the certificate in the `config.ini` file.
This will prevent browser warnings and is the most secure.

If you do not have an enterprise root certificate authority or cannot obtain
signed certificates, you can create a self-signed certificate.  This will present
a warning on each device by the browser on first load and you will be required to
trust the self-signed certificate.

To create a self-signed certificate, the following PowerShell commands can be used.
You must replace the `$fmsInsightServer` variable with the IP address or DNS name that
will be used in the browser to connect to the FMS Insight server.

~~~
$fmsInsightServer = "172.16.17.18"
$certificate = New-SelfSignedCertificate -CertStoreLocation cert:\localmachine\my -dnsname $fmsInsightServer -NotAfter (GetDate).AddYears(10)
Export-PfxCertificate -cert ("cert:\localMachine\my\" + $certificate.Thumbprint) -FilePath "c:\ProgramData\SeedTactics\FMSInsight\cert.pfx"
~~~

The above lines can be pasted one-by-one into an administrator PowerShell.  Once completed,
the certificate will be stored in `c:\ProgramData\SeedTactics\FMSInsight\cert.pfx`.  This path
should be then set as the `TLSCertFile` property in `config.ini` (should just require un-commenting
the `TLSCertFile` line).