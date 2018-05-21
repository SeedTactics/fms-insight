---
id: server-config
title: Server Configuration
sidebar_label: Configuration
---

#### Configuration

Machine Watch runs as a service with no GUI. Machine Watch is configured through a file named
`MachineWatch.xml` located in the install directory chosen when you first installed Machine Watch
(defaults to a folder in `C:\Program Files`). Any changes to the settings file requires restarting
the Machine Watch service. To restart the service open the Windows Service Manager, right click on
the Machine Watch service, and select "Stop" and then "Start". The `MachineWatch.xml` configuration
file contains two kinds of settings: app settings and network settings.

The network settings relate to the .NET Remoting lease times and formatters plus the network port
(defaults to 8086). The lease times and formatter settings should be left unchanged, but if needed
you can change the port that Machine Watch listens on. This port is used by the SeedTactic software
so if changed the SeedTactic software must be configured to use the same port. Also, this port
(defaults to 8086) must be allowed through any firewalls between the computers running SeedTactics
and the Machine Watch cell controller.

The app settings are specific to each cell controller and are described in the documentation for each company.

* [Mazak Machine Watch](/guide/machinewatch/mazak)
* [Makino Machine Watch](/guide/machinewatch/makino)
* [Mori Machine Watch](/guide/machinewatch/mori)
* [Cincron Machine Watch](/guide/machinewatch/cincron)

#### Databases

Machine Watch uses [SQLite](https://www.sqlite.org/) to store data about the cell.  The primary data stored is a log of the activity
of all parts, pallets, machines, jobs, schedules, serial numbers, and orders.  In addition, Machine Watch stores a small amount of temporary
data to assist with monitoring (for example, when a part is loaded on a pallet Machine Watch will store which parts and serial numbers are on
the pallet so that when the pallet gets to the machine Machine Watch knows what is being machined).

The SQLite databases are stored locally in the global AppData folder (not user specific).
The location of the AppData folder differs based on the Windows version and system configuration.  For Windows XP, it is located by
default at `C:\Documents and Settings\All Users` (must enable viewing hidden folders). For Windows 7 and 10, it is located by default at
`C:\ProgramData\`.  Within this folder is a folder named `Black Maple Software` or `CMS Research` and then a folder named `MachineWatch`.
Inside this folder will be several SQLite databases.  The SQLite databases are not large, typically under 100 megabytes, so disk space is not
a problem.

Every time Machine Watch starts, it checks for the existence of the SQLite databases and if they are
missing it creates them. For this reason, the databases can be deleted without impacting the
operation of the cell (as long as Machine Watch is restarted). Also, since the databases just store
a log of past activity and all complex logic is outside Machine Watch in the SeedTactic software, if
the databases are deleted (and recreated) the cell will continue to operate just fine in the future.
Thus it is not necessary to backup the databases (but of course you can if you want to preserve historical data).