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