---
title: FMS Insight Server Configuration
nav: FMS Insight Server > Configuration
description: >-
  Configure the FMS Insight server and preserve its configuration, databases, and production
  history.
---

FMS Insight can run on the cell-controller computer or a separate server, depending on the
integration and [network layout](/fms-insight/security#network-layout). Its settings are stored in
`config.ini` on the computer running Insight.

On Windows, the file is located in the Common AppData folder at `SeedTactics\FMSInsight\config.ini`.
On a default Windows install, this has a full path of
`c:\ProgramData\SeedTactics\FMSInsight\config.ini`. The config file itself describes the settings in
detail, but they fall into three groups: server settings such as network ports and HTTPS, FMS
settings such as automatic serial assignment, and settings for the cell controller.

The first time FMS Insight starts, if the `config.ini` file does not exist, FMS Insight will create
a default config file. Review the generated settings for the installation before production use,
including controller connections, queues, station names, and
[network access and authentication](/fms-insight/security). Any changes to settings in `config.ini`
require the FMS Insight server to be restarted before they take effect.

The `config.ini` file is an [INI file](https://en.wikipedia.org/wiki/INI_file). The default config
file contains comments prefixed by a hash (`#`) that describe each option. Optional settings are
often commented out with a semicolon (`;`). To enable one, remove its leading semicolon and edit the
value. Restart the FMS Insight server after editing the configuration. Configuration errors are
reported in the [event log](/fms-insight/server-errors).

## Databases

FMS Insight uses [SQLite](https://www.sqlite.org/) to store data about the cell. The primary data
stored is a log of the activity of all parts, pallets, machines, jobs, schedules, inspections,
serial numbers, orders, and other data. Alongside production history, the databases store current
material and basket state and records needed to avoid repeating completed operations.

The SQLite databases are by default stored locally in the Windows Common AppData folder which on a
default Windows install is `c:\ProgramData\SeedTactics\FMSInsight`. This path can be changed in the
`config.ini` file if needed, but we suggest that the databases are kept on the local drive for
performance and data integrity (SQLite works best on a local drive). Database size grows with
production activity and retained history; allow space for both production databases and backups.

## Backup and Recovery

Back up `config.ini`, any additional integration configuration, and the certificates or credentials
needed to restore the service, following the site's access-control policy. If scheduling uses
separate planning files or configuration, include those in the installation's backup procedure.

Preserve production databases and back them up before upgrades. Use a backup method that captures a
consistent SQLite database, such as stopping the server before copying its database files. If an
integration maintains additional coordination databases, follow its procedure for backing up the
complete installation consistently.

Deleting or restoring a database can leave Insight's records out of step with the physical cell. A
fresh database cannot reconstruct all of that state from the equipment. Follow the integration's
recovery procedure before resetting or restoring databases, and reconcile the restored records with
the physical cell. A backup from an earlier point in production is not automatically safe to restore
against a cell that has continued operating.

Fresh databases can be used for controlled QA or initial commissioning when the physical cell and
the integration's state have been prepared for a fresh start. FMS Insight creates missing databases
at startup; integrations may impose additional startup and initialization requirements.

## External Production History

For long-term retention or reporting, a separate service can collect production records through
Insight's HTTP API and live WebSocket event stream and store them in a system managed by the site.
It needs access to the Insight endpoint and any configured authentication, not to the machine
network. An event collector should reconnect after interruptions and retrieve missed records from
the API; a live connection alone does not provide a complete archive. Exported production history
serves a different purpose from a backup of the installation's configuration and operational state.
