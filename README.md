# FMS Insight

[Project Website](https://fms-insight.seedtactics.com)

[![OpenAPI](https://img.shields.io/swagger/valid/2.0/https/raw.githubusercontent.com/SeedTactics/fms-insight/main/server/fms-insight-api.json.svg)](http://petstore.swagger.io/?url=https%3A%2F%2Fraw.githubusercontent.com%2FSeedTactics%2Ffms-insight%2Fmain%2Fserver%2Ffms-insight-api.json)
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
reference the [BlackMaple.MachineFramework](https://www.nuget.org/packages/BlackMaple.MachineFramework/) nuget package.
You then need an implementation of `ISyncronizeCellState` and `IMachineControl` interfaces. These can either be
manually implemented for your specific machines or you can use one of the provided implementations for Mazak, Makino,
or Niigata: one of [BlackMaple.FMSInsight.Mazak](https://www.nuget.org/packages/BlackMaple.FMSInsight.Mazak/),
[BlackMaple.FMSInsight.Makino](https://www.nuget.org/packages/BlackMaple.FMSInsight.Makino/), or
[BlackMaple.FMSInsight.Niigata](https://www.nuget.org/packages/BlackMaple.FMSInsight.Niigata/).

The following is the minimial code to run FMS Insight with the Mazak backend:

```csharp
public static void Main()
{
  var cfg = Configuration.LoadFromIni();
  var serverSettings = ServerSettings.Load(cfg);

  InsightLogging.EnableSerilog(serverSettings, enableEventLog: true);

  var fmsSettings = FMSSettings.Load(cfg);
  var serialSt = SerialSettings.SerialsUsingBase62(cfg);

  new HostBuilder()
    .UseContentRoot(Path.GetDirectoryName(Environment.ProcessPath)!)
    .ConfigureServices(s =>
    {
      s.AddRepository(Path.Combine(fmsSettings.DataDirectory, "insight.db"), serialSt);
      s.AddMazakBackend(MazakConfig.Load(cfg));
    })
    .AddFMSInsightWebHost(cfg, serverSettings, fmsSettings)
    .UseWindowsService()
    .Build()
    .Run();
}
```

## Server

The server is written in C# and uses ASP.NET Core to expose a HTTP-based JSON
and REST-like API. The server uses a local SQLite database to store the log and
job data. The
[server/lib/BlackMaple.MachineFramework](server/lib/BlackMaple.MachineFramework)
directory contains the common server code, which includes data definitions,
database code, and ASP.NET Core controllers. The main method of the
BlackMaple.MachineFramework library is a `AddFMSInsightWebHost` extension method
on a host builder, which adds a kestral webhost and configures all the
controllers.

Because the library is designed for FMS Insight to be installed by end-users, we
do the configuration at the very beginning outside the HostBuilder. This allows
more flexibility and makes it easier for the end-user (typically a non-technical
manager on the shop floor) to configure. The example above shows loading using a
helper method which reads an ini file, but you can ignore this code and
configure however you wish. It is only required that you create an instance of
`ServerSettings`, `FMSSettings`, and optionally `SerialSettings`, and do that
before creating the host builder.

The library uses Serilog for logging, and provides a helper method
`InsightLogging.EnableSerilog` to configure logging to files and the event log.
You can also ignore this method and configure Serilog however you wish. FMS
Insight has extensive Debug logging and some Verbose logging, so you should
configure Serilog to log those levels on some kind of configuration flag (since
they produce a lot of data).

The server stores data in a SQLite database and wraps that in a `RepositoryConfig` class.
(This code is some of the oldest code, 20+years, and uses custom SQL commands rather than
an ORM like entity framework or dapper.) FMS Insight requires that a RepositoryConfig be
registered as a singleton service, which is done by the `AddRepository` extension method.
It takes the path to the database and an optional serial settings object to automatically
assign serials (can be null if serials are not automatically assigned).

The server requires `IMachineControl` and `ISyncronizeCellState`
singleton services to be registered (see the next section). In the example
above, this is done by the `s.AddMazakBackend` call. The different backends
provided by this project are in separate nuget packages and each registers
instances of the two interfaces: `IMachineControl` and `ISyncronizeCellState`.
You can also implement these interfaces yourself, and typically must implement
them when talking directly to custom PLCs.

## Machine Status

The `IMachineControl` interface provides a very simple interface to query data
about a machine (the naming is unfortunate, it is read-only and does not
directly control the machine). The interface provides methods to query information
about the machine and these methods are called directly from HTTP controllers in responce
to GET API requests.

## Logs and Cell State

The server is designed around an event-sourcing type pattern, where the server
maintains a log of all events and a log of planned jobs, and this is stored in
the SQLite database. The server recalculates the current cell state by looking
at the log and querying the current machines or PLCs. We try as much as possible
to store no state about the system either in memory or in the SQLite database.

To handle the cell state, the server has a thread which repeatedly builds a new
representation of the state of the cell from scratch and then decides if any
actions need to be sent to the machines. The thread spawned by the server will
recalculate the cell state on a timeout or whenever the backend raises an event.
If the cell controller provides some type of event such as a file creation event
or a database notification, these can be configured to raise to the
`NewCellState` event and then use a recheck timeout of 1-2 minutes. On other
systems which do not have events, we typically use a 10-second timeout,
essentially polling the machines to recreate the cell state and decide any
actions.

The most recent calculated cell state is cached and used to respond to HTTP API
requests by the controllers.

## ISyncronizeCellState

To facilitate the cell state, the server requires an implementation of the
`ISyncronizeCellState` interface be registered. There are two main methods,
`ISyncronizeCellState.CalculateCellState` and
`ISyncronizeCellState.ApplyActions`.

As mentioned above, the server starts a thread and waits either for the
`ISyncronizeCellState.NewCellState` event to be raised or a configurable timeout
to be reached. After that, the server thread calls `CalculateCellState` and then
immedietly calls `ApplyActions`. The server guarantees that these methods are
called on a single thread and handles all thread saftey; the implementation can
assume `CalculateCellState` and `ApplyActions` are always called from the same
thread. The server also handles all exceptions and turns them into alarms, so no
error handling is needed inside these methods.

For `CalculateCellState`, the implementation should build a new cell state from
the log of events and querying the machines. While doing so, `CalculateCellState`
might also record any missing log messages, for example if the machine has stopped
it might record the machine end log event if one has not yet been recorded.
There are some helper static methods in the static class `BuildCellState` which
can assist with this.

The `ApplyActions` method is called immediately after `CalculateCellState` with
the newly calculated cell state, and should apply any changes to the cell. For example,
it might start the machines, send data into the cell controller, print a label on
a label printer, or so on.

Since `CalculateCellState` and `ApplyActions` are always called together, the
server is indifferent to how much you divide the work between
`CalculateCellState` and `ApplyActions` methods. The main distinction is around
error handling: if `CaluclateCellState` has an exception, the stale cached cell
state from previously is still showed to the user, while if `CalculateCellState`
is successful but `ApplyActions` gets an error the cached cell state is still
updated.

There are some other methods in `ISyncronizeCellState` and `IMachineControl`
which are used to handle specific situations, but in fact all other methods can
do nothing and just return, and still have a functional FMS Insight server.

## Customizing

FMS Insight provides several interfaces in `BackendInterfaces.cs` which can be implemented to
customize various aspects of the server. If instances of these interfaces are registered
as services in the dependency injection container, they will be used by the server. Otherwise,
default implementations will be used. Another key way of adding additional logic is the
`RepositoryConfig.NewLogEntry` event. You can create a ASP.NET Core hosted service (implement
the IHostedService interface) and inject the `RepositoryConfig`. You can then subscribe to
the `NewLogEntry` event and then run your custom logic whenever a new log entry is added.

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
