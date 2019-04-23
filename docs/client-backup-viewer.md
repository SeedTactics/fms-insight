---
id: client-backup-viewer
title: FMS Insight Backup Viewer
sidebar_label: Backup Viewer
---

[FMS Insight Backup Viewer Install.exe](/installers/FMS%20Insight%20Backup%20Viewer%20Install.exe)

FMS Insight stores data in a SQLite database local to the cell controller
(path is set in the [configuration](server-config.md)). FMS Insight is
resilient to the loss of the databases; if the databases are deleted or lost,
FMS Insight will start logging cell controller events to a new blank database
and new jobs can be downloaded to continue production. The [Starting Serial
Config Setting](server-config.md) can be used to cause the new empty FMS
Insight to start producing serials larger than previously used serials. In
this way, the FMS Insight databases are not production critical; if they are
lost the cell can continue production. Thus periodic backups of the FMS
Insight databases are not required.

Despite this, the FMS Insight log database does contain a wealth of
historical data that is helpful for [analyzing the
cell](client-flexibility-analysis.md) as well as the production history for each
serial. Thus the SQLite databases can be periodically backed up to maintain
an archive of historical log data. The _FMS Insight Backup Viewer_ program is
a desktop application which allows you to open the SQLite database file
directly and view efficiency and serial data.

Therefore, we suggest that periodic backups of the SQLite databases occur. If
the cell controller crashes or data is otherwise lost, FMS Insight itself can
just be installed and restarted with blank empty databases. The _FMS Insight Backup Viewer_ can
then be used to view the historical data directly from the backed up files.

To create the backup, FMS Insight can be stopped, the database files copied,
and FMS Insight started again. Alternatively, SQLite files can be backed-up
online using the `.backup` command in the [sqlite command line
tool](https://sqlite.org/cli.html#special_commands_to_sqlite3_dot_commands_),
for example:

```text
sqlite3 c:\ProgramData\SeedTactics\FMSInsight\log.db .backup c:\log-backup.db
```
