# FMS Insight

[![Build status](https://ci.appveyor.com/api/projects/status/qfbp2wstg3if04t4?svg=true)](https://ci.appveyor.com/project/wuzzeb/fms-insight)

FMS Insight is a client and server which runs on an flexible machining system (FMS)
cell controller which provides:

* A server which stores a log of all events and stores a log of planned jobs.
* A server which translates incomming planned jobs into jobs in the cell controller.
* A REST-like HTTP API which allows other programs to view the events and create planned jobs.
* A view-only HTML client which displays a dashboard, station monitor, and data analysis
  based on the log of events, planned jobs, and current cell status.

### Logging

One core feature of the FMS Insight server is to maintain a log of all
events. These events include machine cycle start, machine cycle end, load
start, load end, pallet movements, inspections, serial assignment, wash, and
others. The server also maintains a log of planned jobs; each planned job is
stored with its date, part name, planned quantities, flexibility, and other
features of the job.

The logging format is common and shared between all cell manufacturers, with
specific details of each cell controller manufacturer's log hidden by custom
code which translates the log events into the common FMS Insight log. FMS
Insight stores the log of events in a SQLite database and access to the log
events is available via a REST-like HTTP API.

The immutable log of events and planned jobs can be then used to drive all
decisions and analysis of the cell. The design is based on the
[Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html).
All changes to the cell are captured as a stream of events, and analysis and
monitoring of the cell proceeds by analyzing the log.

### Job Translation

The second core feature of the FMS Insight server is to translate planned
jobs into the cell controller (because all current cell controllers are
stateful instead of based on the event sourcing pattern). The FMS Insight
server does not implement any complex logic or control itself; instead FMS
Insight is a generic conduit which allows other software to communicate jobs
into the cell controller.

FMS Insight defines a common JSON definition of a planned job which includes
the part name, program, pallets, flexiblity (which machines/load stations to
use), target cycles, and other data about the planned job. The FMS Insight
server then provides a REST-like HTTP API which allows other programs to post
this common JSON definition of a planned job wich FMS Insight then inserts
into the cell controller.  There is different code for each cell control
manufacturer, but this is hidden behind the common JSON definition of a planned
job.

Finally, the FMS Insight server HTTP API provides access to the current state
of jobs, pallets, and material. Technically this should all be able to be
recovered from the event log, but since current cell controllers are based on
modifying state FMS Insight provides a view of this state in a common JSON
format.

### View-Only HTML Client

FMS Insight serves a view-only HTTP client.

* Dashboard: The dashboard provides an overview of the current jobs and their
  progress, current machine status, and other data.
* Station Monitor.  The station monitor is intended to be viewed at various operator
  stations throughout the factory and provides information to the operator on what
  actions to take.  For example, at the load station it shows what to load and unload and
  at the inspection stand it shows what inspections were triggered.
* Efficiency Report.  The efficiency report shows various charts and data analysis of the
  event log.  These charts are intended to help improve the efficiency of the cell.
* Part Cost/Piece.  The part cost/piece report shows a calculated cost/piece based on the
  event log.  The cost/piece is intended to help validate that operational changes are
  actually improvements.

### Creating Jobs and Scheduling

The FMS Insight client and server has no method to create new planned jobs.
As mentioned above, it accepts a planned job as a JSON document and
translates that job into the cell controller. On typical projects, planned
jobs are created by analyzing incoming orders, but so far each project has a
custom algorithm to translate orders to planned jobs.  The
[seedscheduling](https://bitbucket.org/blackmaple/seedscheduling) repository
contains some framework code and jupyter notebooks to translate orders into
planned jobs.   Typically during development I use the jupter notebook to create
and send planned jobs to FMS Insight, and during production OrderLink is used to
perform the scheduling.

# Code Overview

### Machine Watch Interface

[![NuGet Stats](https://img.shields.io/nuget/v/BlackMaple.MachineWatchInterface.svg)](https://www.nuget.org/packages/BlackMaple.MachineWatchInterface)

If you want to write a client program to communicate with Machine Watch over the network, use the
`BlackMaple.MachineWatchInterface` assembly.  The source code for this assembly is in the
[lib/BlackMaple.MachineWatchInterface](https://bitbucket.org/blackmaple/machinewatch/src/tip/server/lib/BlackMaple.MachineWatchInterface/)
directory, but a compiled version is on NuGet as
[BlackMaple.MachineWatchInterface](https://www.nuget.org/packages/BlackMaple.MachineWatchInterface/).

### Machine Framework

First, read the [integrations document](https://bitbucket.org/blackmaple/machinewatch/src/tip/integration.md) which
describes at a high level the data and communication between Machine Watch and the cell controller.
