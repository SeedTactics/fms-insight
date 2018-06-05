# FMS Insight

[![Build status](https://ci.appveyor.com/api/projects/status/qfbp2wstg3if04t4?svg=true)](https://ci.appveyor.com/project/wuzzeb/fms-insight)

[Project Website](https://fms-insight.seedtactics.com)

FMS Insight is a client and server which runs on an flexible machining system (FMS)
cell controller which provides:

* A server which stores a log of all events and stores a log of planned jobs.
* A server which translates incomming planned jobs into jobs in the cell controller.
* A REST-like HTTP API which allows other programs to view the events and create planned jobs.
* An HTML client which displays a dashboard, station monitor, and data analysis
  based on the log of events, planned jobs, and current cell status.

## Server

The server is written in C# and uses ASP.NET Core to expose a HTTP-based JSON
and REST-like API. The server uses two local SQLite databases to store the
log and job data which for historical reasons use a custom ORM instead of
Entity Framework or Dapper (the code has been developed for over 10 years).
The [server/lib/BlackMaple.MachineFramework](https://bitbucket.org/blackmaple/fms-insight/src/default/server/lib/BlackMaple.MachineFramework)
directory contains the common server code, which includes data definitions,
database code, and ASP.NET Core controllers.

For each machine manufacturer (currently Mazak, Makino, and Cincron),
there is an executable project which is what is installed by the user.
This per-manufacturer project references the common code in `BlackMaple.MachineFramework`
to provide the data storage and HTTP api.  The per-manufacturer project
also implements the custom interop for logging and jobs.  The code lives
in [server/machines](https://bitbucket.org/blackmaple/fms-insight/src/default/server/machines/).

There is also a mock server which I use while developing the client.  The mock
server lives in [server/debug-mock](https://bitbucket.org/blackmaple/fms-insight/src/default/server/debug-mock/).  I
use VSCode as an editor and there is a VSCode tasks for launching the server.

## Data and API

The data and API is diveded into two main sections: event log data and jobs.

The event log data is a log of all events on the cell, including machine cycle
start, machine cycle end, load start, load end, pallet movements,
inspections, serial assignment, wash, and others. The logging format is
common and shared between all cell manufacturers, with specific details of
each cell controller manufacturer's log hidden by custom code which
translates the log events into the common FMS Insight log.

For jobs, FMS Insight defines a common JSON definition of a planned job which
includes the part name, program, pallets, flexiblity (which machines/load
stations to use), target cycles, and other data about the planned job. Each
cell controller manufacturer integration project includes code to translate
this common job JSON format into the specific jobs and schedules in the cell
controller. Typically the integration does not implement any complex logic or
control itself; instead FMS Insight is a generic conduit which allows other
software to communicate jobs into the cell controller.  FMS Insight also keeps
a log of all jobs sent into the cell controller.

The immutable log of events and planned jobs provides a great base to drive
decisions and analysis of the cell. The design is based on the
[Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html).
All changes to the cell are captured as a stream of events, and analysis and
monitoring of the cell proceeds by analyzing the log.  FMS Insight tries to
minimize the stateful data and operations that it supports, instead focusing
as much as possible to provide a stable immutable API for cell controllers.

## Swagger

[![NuGet Stats](https://img.shields.io/nuget/v/BlackMaple.FMSInsight.API.svg)](https://www.nuget.org/packages/BlackMaple.FMSInsight.API/)

The server generates a Swagger file and serves SwaggerUI using [NSwag](https://github.com/RSuter/NSwag).
The latest swagger file can be obtained by running the debug mock and then accessing `http://localhost:5000/swagger/v1/swagger.json`.  For each version of the API, I export the swagger document
and then commit it to source control in the `server` directory.  The swagger file is then used to generate two
clients: one in typescript for use in the HTTP client and one in C#.  The C# client is published on
nuget as [BlackMaple.FMSInsight.API](https://www.nuget.org/packages/BlackMaple.FMSInsight.API/).

## HTML Client

The client is written using React, Redux, Typescript, and MaterialUI.  The
client is compiled using webpack (actually create-react-app-ts) and the resulting
HTML and Javascript is included in the server builds.  The code lives in
[client/insight](https://bitbucket.org/blackmaple/fms-insight/src/default/client/insight/).

I use VSCode as an editor and there are VSCode tasks for launching webpack
in development/watch mode and a debug configuration for launching Chrome.

## Creating Jobs

The FMS Insight client and server has no method to create new planned jobs.
As mentioned above, it accepts a planned job as a JSON document and
translates that job into the cell controller. On typical projects, planned
jobs are created by analyzing incoming orders, but so far each project has a
custom algorithm to translate orders to planned jobs and integrate with the
customer's ERP. The
[seedscheduling](https://bitbucket.org/blackmaple/seedscheduling) repository
contains some framework code and jupyter notebooks to translate orders into
planned jobs. Typically during development I use the jupter notebook to
create and send planned jobs to FMS Insight, and during production OrderLink
is used to perform the scheduling.