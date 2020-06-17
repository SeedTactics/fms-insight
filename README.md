# FMS Insight

[Project Website](https://fms-insight.seedtactics.com)

FMS Insight is a client and server which runs on an flexible machining system (FMS)
cell controller which provides:

- A server which stores a log of all events and stores a log of planned jobs.
- A server which translates incoming planned jobs into jobs in the cell controller.
- A REST-like HTTP API which allows other programs to view the events and create planned jobs.
- An HTML client which displays a dashboard, station monitor, and data analysis
  based on the log of events, planned jobs, and current cell status.

### Server

The server is written in C# and uses ASP.NET Core to expose a HTTP-based JSON
and REST-like API. The server uses two local SQLite databases to store the
log and job data which for historical reasons use a custom ORM instead of
Entity Framework or Dapper (the code has been developed for over 10 years).
The [server/lib/BlackMaple.MachineFramework](server/lib/BlackMaple.MachineFramework)
directory contains the common server code, which includes data definitions,
database code, and ASP.NET Core controllers.

For each machine manufacturer (currently Mazak, Makino, and Cincron),
there is an executable project which is what is installed by the user.
This per-manufacturer project references the common code in `BlackMaple.MachineFramework`
to provide the data storage and HTTP api. The per-manufacturer project
also implements the custom interop for logging and jobs. The code lives
in [server/machines](server/machines/).

The server focuses on providing an immutable log of events and planned jobs.
This provides a great base to drive decisions and analysis of the cell. The design
is based on the [Event Sourcing
Pattern](https://martinfowler.com/eaaDev/EventSourcing.html). All changes to
the cell are captured as a stream of events, and analysis and monitoring of
the cell proceeds by analyzing the log. FMS Insight tries to minimize the
stateful data and operations that it supports, instead focusing as much as
possible to provide a stable immutable API for cell controllers.

### Swagger

[![NuGet Stats](https://img.shields.io/nuget/v/BlackMaple.FMSInsight.API.svg)](https://www.nuget.org/packages/BlackMaple.FMSInsight.API/) [![Swagger](https://img.shields.io/swagger/valid/2.0/https/raw.githubusercontent.com/SeedTactics/fms-insight/main/server/fms-insight-api.json.svg)](http://petstore.swagger.io/?url=https%3A%2F%2Fraw.githubusercontent.com%2FSeedTactics%2Ffms-insight%2Fmain%2Fserver%2Ffms-insight-api.json)

The server generates a Swagger file and serves SwaggerUI using [NSwag](https://github.com/RSuter/NSwag).
The latest swagger file can be obtained by running the server and then accessing `http://localhost:5000/swagger/v1/swagger.json` or
[browsed online](http://petstore.swagger.io/?url=https%3A%2F%2Fraw.githubusercontent.com%2FSeedTactics%2Ffms-insight%2Fmain%2Fserver%2Ffms-insight-api.json).
The swagger file is then used to generate two
clients: one in typescript for use in the HTTP client and one in C#. The C# client is published on
nuget as [BlackMaple.FMSInsight.API](https://www.nuget.org/packages/BlackMaple.FMSInsight.API/).

### HTML Client

The client is written using React, Redux, Typescript, and MaterialUI. The
client is compiled using parcel and the resulting
HTML and Javascript is included in the server builds. The code lives in
[client/insight](client/insight/).

I use VSCode as an editor and there are VSCode tasks for launching parcel in
development/watch mode and a debug configuration for launching Chrome. There
is also a mock server which I use while developing the client. The mock
server lives in
[server/debug-mock](server/debug-mock/).
There is a VSCode task for launching the debug mock server.

# Custom Plugins

FMS Insight supports customized plugins. Actually, FMS Insight itself is a library so the "plugin"
is an executable project which sets up any customization and then calls into the FMS Insight library.

[![NuGet Stats](https://img.shields.io/nuget/v/BlackMaple.MachineFramework.svg)](https://www.nuget.org/packages/BlackMaple.MachineFramework/)

#### Project Structure

To create a plugin, start a new executable C# project and
reference the [BlackMaple.MachineFramework](https://www.nuget.org/packages/BlackMaple.MachineFramework/) nuget package
and then one of [BlackMaple.FMSInsight.Mazak](https://www.nuget.org/packages/BlackMaple.FMSInsight.Mazak/) or
[BlackMaple.FMSInsight.Makino](https://www.nuget.org/packages/BlackMaple.FMSInsight.Makino/).

#### Project Code

Create a file `Main.cs` with code such as the following

```
using System;

namespace MyCompanyName.Insight
{
  public static class InsightMain
  {
    public static void Main()
    {
      BlackMaple.MachineFramework.Program.Run(true, (cfg, st) =>
      {
        var Mazak = new MazakMachineInterface.MazakBackend(cfg, st);
        // Attach to events in Mazak here
        return new BlackMaple.MachineFramework.FMSImplementation()
        {
          Name = "MyCompanyNameInsight",
          Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
          Backend = Mazak,
        };
      });
    }
  }
}
```

The `FMSImplementation` data structure is defined in [BackendInterfaces.cs](server/lib/BlackMaple.MachineFramework/BackendInterfaces.cs). It contains properties and settings that can be overridden to add customized
behavior to FMS Insight. Also, the `MazakBackend` contains events that can be registered to respond to various events.

For example, to implement customized part marking, a class can be implemented which attaches to the events in the `IMazakLogReader` interface.
When a Mazak Rotary Table Swap event occurs, the code can output a EIA program to perform the mark and also record the generated serial
in the FMS Insight Log.

# Data and API

The data and API is divided into two main sections: event log data and jobs.

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
software to communicate jobs into the cell controller. FMS Insight also keeps
a log of all jobs sent into the cell controller.

## Log of events

To help track material, FMS Insight assigns a `MaterialID` to each piece of material produced by
the cell. The `MaterialID` is a unique integer that identifies a specific piece of material and is
never reused. The `MaterialID` helps track a single piece of material throughout its entire journey,
from initial loading through reclamping and multiple processes to unloading. The `MaterialID` is
especially helpful for parts with multiple processes since the same `MaterialID` will appear on
multiple machine cycles.

`MaterialID`s can either be created and assigned by the cell controller or FMS Insight. It is
better that the cell controller assigns `MaterialID`s because the cell controller knows more about
the cell operation, but FMS Insight can generate and attach `MaterialID`s to existing events if
the cell controller does not create unique IDs for each piece of material.

Each job added into the cell has a unique string called `JobUnique`. This is a string which uniquely
identifies the job and is typically a UUID or other unique randomly generated ID. Each event about a
piece of material should include also the `JobUnique` that is causing the material to be manufactured.

Here are the events that are stored:

##### Machine Cycle

An event for cycle start and cycle stop of each machine. Data includes

- Date and Time
- Station number
- Pallet
- Program
- Active operation time (time that the program is actually cutting/running. For example, if the machine goes down the time between
  cycle start and cycle stop will be longer than the active operation time.)
- List of material being cut by the program:
  - MaterialID
  - Part Name
  - Process #
  - Part Quantity
  - JobUnique
- Any additional data which might help the customer. In the past we have included probe data produced by the part program,
  tools used and their life, and others. This kind of data is largely based on what the customer wants. Our system can attach
  any extra data that the cell controller produces.

This data must be present in the cell controller log and custom code will translate the cycle event
from the cell controller to the FMS Insight log.

##### Load Cycle

We log an event for start of loading/unloading (when the pallet arrives at the load station) and
another event for end of loading/unloading (when the pallet leaves the load station). The data we
store is

- Date and Time
- Station number
- Pallet

For start of load/unload (when the pallet arrives at the load station), the event also contains

- a list of material the operator should load onto the pallet:
  - MaterialID
  - Part Name
  - Process #
  - Quantity
  - JobUnique
- a list of material that the operator should remove from the pallet:
  - MaterialID
  - Part Name
  - Process #
  - Quantity
  - JobUnique
- a list of material that the operator should transfer from one process to another (reclamp):
  - MaterialID
  - Part Name
  - Process # to remove from
  - Process # to place into
  - Quantity
  - JobUnique

For end of load/unload (when the pallet leaves the load station), the event contains the same
material data as the start, except restricted to what the operator actually performed. Typically
this will be the same as what the cell controller requested at the start, but could be different.
For example, the cell controller requests that a part ABC be loaded onto the pallet but there is no
raw casting so the operator sends the pallet to the buffer stand instead to wait. In this case, the
start of load/unload event will include the request for ABC but the end of load/unload event will
not have a piece of material loaded.

This data must be present in the cell controller log and custom code will translate the cycle event
from the cell controller to the FMS Insight log.

#### Inspections

For inspection signaling, there is a log entry for each MaterialID and each
inspection type. The log entry will store a boolean true/false for if the
part should be signaled for inspection or not. FMS Insight typically
automatically creates these log events when the part is unloaded based on an
inspection pattern, but the log entries can also be created by the user
clicking a button in the HTML client.

FMS Insight also stores a separate event for each completed inspection. This
event is created by the user pressing a button in the client. The event
stores the date/time, MaterialID, inspection type, and if the inspection succeeded or failed.

#### Serial Assignment

When a part is marked with a serial, an event is recored in the log with the
date/time, `MaterialID`, and serial. These log entries can be created automatically
via setting in the server, created by the user scanning a barcode into the
HTML client, or using custom software. For example, if the process uses a
scheme where a tool is used to mark a serial and barcode on the part inside
the machine, a custom program is written to deposit a unique serial in a NC
program file and also record the depositied serial in the FMS Insight log.

#### Wash

The HTML client allows the user to click a button to record that final wash
is complete. This produces an event recording the date/time, MaterialID, and
the operator which completed the wash.

#### Workorder Assignment

The HTML client allows the user to assign a part to a workorder. The workorder can be any
string. When the user clicks the button to assign a part to a workorder, an event is added
to the log recording the date and time, MaterialID, and workorder.

FMS Insight also can record an event when a workorder is finalized. This includes the
date/time and workorder. Typically these events are created by custom code which
integrates with the customer's ERP system.

#### API Access

The log API provides several different ways of querying the log: obtaining
all log events in some date range, log events for a single MaterialID or
serial, or log events for completed parts. The API also provides one report,
a workorder report which calculates a summary of all parts assigned to a
specific workorder. Finally, the API allows creation of serial assignments,
workorders, wash cycles, and inspection cycles. All of these APIs are implemented
by FMS Insight itself and don't require access to the cell controller; indeed all the
events have already been translated into FMS Insight's own SQLite database.

## Jobs

Each job is assigned a unique string that is never reused. This `JobUnique`
must then appear in the log of events which allows us to connect events back
to jobs. The following is the data included as part of the job.

- `JobUnique`: a string uniquely identifying the job.
- Part Name
- Priority
- Quantity to Produce
- Number of Processes
- (optional) Hold Pattern. Occasionally the customer wants a hold pattern such as hold this
  job until 3rd shift.

For each process, the job includes the following data:

- list of allowed pallets
- list of allowed load/unload stations
- list of allowed machines
- program to run at the machine
- part quantity loaded onto a pallet at once
- fixture or fixtures to use

#### Current Status of the Jobs

FMS Insight will translate the current jobs in the cell controller into the
common JSON definition and then provide it over the network HTTP API. That
is, the current status includes all the jobs currently in the system with
data about them (part, priority, flexibility, planned quantity, etc.). The
list should also include data about the progress of the job: the competed
quantity and the number of parts currently in-process.

The cell controller must provide a method of accessing this data; the FMS Insight
integration will translate it into the common JSON format for the API.

#### Current Pallet Status

FMS Insight provides data over the network API on the current pallet status:
current location of the pallets, current material loaded onto the pallets,
machine status, load/unload instructions (material to load/unload at the load
station).

The current pallet status can be recovered from the log of events so is not
strictly required from the cell controller. But, access to this data in the
cell controller is a great help for debugging, testing, and cross-checking
the data. If it is easy to provide access to the current cell status then FMS
Insight can take advantage of this data. Otherwise, the FMS Insight
integration will create the current pallet status from the log of events.

#### Job History

FMS Insight stores a log of all jobs sent into the cell controller. This log can be
queried by date range over the network API.

#### Adding Jobs

On a schedule determined by the customer but typically once a day or once a
week, some custom software outside of FMS Insight will examine the customer's
orders and due dates and completed material and then use artificial
intelligence or some other techniques to optimize what to produce and what
flexibility to use. To keep the system lean, only enough demand to keep the
machines busy is then sent to the cell controller in the form of a collection
of jobs. (These are also called schedules on some cell controllers.) New jobs
are created each day and the flexibility of which pallets or machines to use
can change for each job.

The network API provides a route which allows this custom software to post
the new jobs to create. Each FMS Insight integration then has code to convert
the new jobs from the common JSON definition into the format expected by the
cell controller.

On
typical projects, planned jobs are created by analyzing incoming orders. To
do so, we develop a custom algorithm for each project to translate orders
from the ERP into to planned jobs. The
[seedscheduling](https://github.com/SeedTactics/scheduling) repository
contains some framework code and jupyter notebooks to translate orders into
planned jobs. Typically during development I use the jupter notebook to
create and send planned jobs to FMS Insight, and during production OrderLink
is used to perform the scheduling.

#### Decrement and Remove Jobs

FMS Insight provides an API to remove planned but not yet started quantity
from existing jobs. When we are scheduling a new day, sometimes it is helpful
to start from a blank slate and remove planned but not yet started quantities
from jobs. We don't want to delete the job because any in-process material
should finish, but we don't want the job to start any new parts. The easiest
way to do that is to reduce the planned quantity on the job. The FMS Insight
integration provides a way to perform these edits on the cell controller itself.

Implementation of this is optional and depends on the level of access FMS Insight
has to the cell controller. If such job modification is not implemented, it isn't that
bad since jobs are only sized for one day and it is easier operationally to just
let past jobs run to completion.

#### Queue Editing

Finally, FMS Insight provides an API to edit material in queues.
