---
title: FMS Insight Server Configuration
nav: FMS Insight Server > Configuration
description: >-
  The FMS Insight server runs on the cell controller and is configured by a config.ini file.
---

# FMS Insight Server Configuration

The FMS Insight server is configured via a file `SeedTactics\FMSInsight\config.ini` located in the
Windows Common AppData folder. On a default Windows install, this has a full path of
`c:\ProgramData\SeedTactics\FMSInsight\config.ini`. The config file itself describes the settings in
detail, but they fall into three groups: server settings such as network ports and SSL, FMS settings
such as automatic serial assignment, and finally cell controller specific settings.

The first time FMS Insight starts, if the `config.ini` file does not exist, FMS Insight will create
a default config file. FMS Insight should run fine with the default settings, so typically when
first getting started no config file settings need to be changed. Any changes to settings in
`config.ini` require the FMS Insight server to be restarted before they take effect.

The `config.ini` file is an [INI file](https://en.wikipedia.org/wiki/INI_file). The default config
file contains a large number of comments describing each option. Comments describing each option are
prefixed by a hash (`#`) and the options themselves are prefixed by a semicolon (`;`). To edit an
option, remove the leading semicolon (`;`) and then edit the option value. Remember to restart the
FMS Insight server. If any configuration errors are present, FMS Insight will generate an error
message with details into the [event log](server-errors).

## Databases

FMS Insight uses [SQLite](https://www.sqlite.org/) to store data about the cell. The primary data
stored is a log of the activity of all parts, pallets, machines, jobs, schedules, inspections,
serial numbers, orders, and other data. The databases also hold operational state, including
committed basket contents, material-allocation identities, and operation receipts used to recognize
completed work on retries.

The SQLite databases are by default stored locally in the Windows Common AppData folder which on a
default Windows install is `c:\ProgramData\SeedTactics\FMSInsight`. This path can be changed in the
`config.ini` file if needed, but we suggest that the databases are kept on the local drive for
performance and data integrity (SQLite works best on a local drive). Database size grows with
production activity and retained history; allow space for both production databases and backups.

Preserve production databases and back them up before upgrades. Use a backup method that captures a
consistent SQLite database, such as stopping the server before copying its database files. If an
integration maintains additional coordination databases, follow its procedure for backing up the
complete installation consistently.

Deleting a production database loses both history and operational identities and retry records.
Creating a fresh database does not reconstruct that state from the equipment. Follow the
integration's recovery procedure before resetting or restoring databases, including reconciling the
physical cell and any integration-specific state. A backup from an earlier point in production is
not automatically safe to restore against a cell that has continued operating.

Fresh databases can be used for controlled QA or initial commissioning when the physical cell and
the integration's state have been prepared for a fresh start. FMS Insight creates missing databases
at startup; integrations may impose additional startup and initialization requirements.
