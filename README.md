# FMS Insight

[Project Website](https://fms-insight.seedtactics.com)

[![NuGet Stats](https://img.shields.io/nuget/v/BlackMaple.FMSInsight.API.svg)](https://www.nuget.org/packages/BlackMaple.FMSInsight.API/) [![OpenAPI](https://img.shields.io/swagger/valid/2.0/https/raw.githubusercontent.com/SeedTactics/fms-insight/main/server/fms-insight-api.json.svg)](http://petstore.swagger.io/?url=https%3A%2F%2Fraw.githubusercontent.com%2FSeedTactics%2Ffms-insight%2Fmain%2Fserver%2Ffms-insight-api.json)
[![NuGet Stats](https://img.shields.io/nuget/v/BlackMaple.MachineFramework.svg)](https://www.nuget.org/packages/BlackMaple.MachineFramework/)

FMS Insight is a program which runs on an flexible machining system (FMS)
cell controller which has the following goals:

- Track material by barcode serial throughout the manufacturing process, tracking
  the material through both automated handling and manual handling. The log of all
  material is stored in a database.
- Use the material tracking to help the operators on the shop floor by providing instructions
  and data displays, for example to provide the inspection operator with data about the
  specific part being inspected. FMS Insight provides a HTML client optimized for touchscreen use.
- Disseminate information about the daily cell operation throughout the factory. FMS Insight provides targeted pages
  for engineering, quality control, supervisors, scheduling, and management.
- Facilitate continuous improvement and cost reduction via analytics. FMS Insight assists in a monthly review
  of the cell by display metrics and graphs to help improve the cost/piece and efficiency
  of the FMS.
- Simplify job/plan data creation in the cell controller to prevent traffic jams. Too many
  jobs with too much demand in an automated system leads to pallet traffic jams just like too many
  cars on the road lead to traffic jams. When job/plan data creation inside the cell controller
  is simple and painless, it is much easier to rate-limit the flow of demand into the system
  to prevent traffic jams.

To implement the above goals, FMS Insight provides:

- A server which stores a log of all events and stores a log of planned jobs.
- A server which translates incoming planned jobs into jobs in the cell controller.
- A REST-like HTTP API which allows other programs to view the events and create planned jobs.
- An HTML client which displays a dashboard, station monitor, and data analysis
  based on the log of events, planned jobs, and current cell status.

## Getting Started

Each cell is different and in our experience requires a small amount of custom logic,
so FMS Insight does not provide a standard build. Instead, FMS Insight is designed as a library
which you import into your own C# executable project.

To create a plugin, start a new executable C# project and
reference the [BlackMaple.MachineFramework](https://www.nuget.org/packages/BlackMaple.MachineFramework/) nuget package
and then one of [BlackMaple.FMSInsight.Mazak](https://www.nuget.org/packages/BlackMaple.FMSInsight.Mazak/),
[BlackMaple.FMSInsight.Makino](https://www.nuget.org/packages/BlackMaple.FMSInsight.Makino/), or
[BlackMaple.FMSInsight.Niigata](https://www.nuget.org/packages/BlackMaple.FMSInsight.Niigata/).

```
# dotnet new console
# dotnet add package BlackMaple.MachineFramework
# dotnet add package BlackMaple.FMSInsight.Mazak
```

Next, edit `Program.cs` to contain:

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

The above code is enough to run FMS Insight without any customization. To add customized behavior,
the `FMSImplementation` data structure contains properties and settings that can be overridden (see the definition in
[BackendInterfaces.cs](server/lib/BlackMaple.MachineFramework/backend/BackendInterfaces.cs)). Also, the `MazakBackend`
class contains events that can be registered to respond to various events. For example, to implement customized part
marking, a class can be implemented which attaches to the events in the `IMazakLogReader` interface. When a Mazak
Rotary Table Swap event occurs, the code can output a EIA program to perform the mark and also record the generated
serial in the FMS Insight Log.

## Server

The server is written in C# and uses ASP.NET Core to expose a HTTP-based JSON
and REST-like API. The server uses a local SQLite database to store the
log and job data which for historical reasons use a custom ORM instead of
Entity Framework or Dapper (the code has been developed for over 10 years).
The [server/lib/BlackMaple.MachineFramework](server/lib/BlackMaple.MachineFramework)
directory contains the common server code, which includes data definitions,
database code, and ASP.NET Core controllers.

For each machine manufacturer (currently Mazak, Makino, and Niigata),
there is a per-manufacturer project. The code lives in [server/machines](server/machines/).

The server focuses on providing an immutable log of events and planned jobs.
This provides a great base to drive decisions and analysis of the cell. The design
is based on the [Event Sourcing
Pattern](https://martinfowler.com/eaaDev/EventSourcing.html). All changes to
the cell are captured as a stream of events, and analysis and monitoring of
the cell proceeds by analyzing the log. FMS Insight tries to minimize the
stateful data and operations that it supports, instead focusing as much as
possible to provide a stable immutable API for cell controllers.

## HTML Client

The client is written using React, Typescript, and MaterialUI. The
client is compiled using vite and the resulting
HTML and Javascript is included in the server builds. The code lives in
[client/insight](client/insight/).

I use VSCode as an editor and there are VSCode tasks for launching parcel in
development/watch mode and a debug configuration for launching Chrome. There
is also a mock server which I use while developing the client. The mock
server lives in [server/debug-mock](server/debug-mock/).
There is a VSCode task for launching the debug mock server.
