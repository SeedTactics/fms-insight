# Machine Watch

Machine Watch is a service application that runs on an FMS cell controller. Machine Watch does not
implement any complex logic or control itself; instead Machine Watch is a generic conduit which
allows other software to communicate with the cell. Machine Watch provides two main services.
First, it records a log of the operation of the machines, pallets, and other events that occur in
the cell. Secondly, Machine Watch provides the ability to create and edit parts, pallets, schedules,
and jobs inside the cell controller.  Both of these services are available over the network using
a common API, so that clients do not need to know the specific details of the cell controller manufacturer.

This repository contains the network API and data structures exposed by Machine Watch to clients,
the server code, and some helper code for the communication between Machine Watch and the cell controller.
The Machine Watch server communicates with the cell controller software via a plugin loaded by the server;
each cell controller manufacturer has a distinct plugin.  The plugins for the various cell controllers are
developed separately in different repositories.

### Running Machine Watch

The [SeedTactics documentation](https://www.seedtactics.com/guide/machine-watch) describes installing, configuring,
and using a Machine Watch server.

### Writing a program to communicate with Machine Watch

If you want to write a client program to communicate with Machine Watch over the network, use the
`BlackMaple.MachineWatchInterface` assembly.  The source code for this assembly is in the
[lib/BlackMaple.MachineWatchInterface](https://bitbucket.org/blackmaple/machinewatch/src/tip/lib/BlackMaple.MachineWatchInterface/)
directory, but a compiled version is on NuGet as
[BlackMaple.MachineWatchInterface](https://www.nuget.org/packages/BlackMaple.MachineWatchInterface/).

Follow the following steps:

* Add a reference to [BlackMaple.MachineWatchInterface](https://www.nuget.org/packages/BlackMaple.MachineWatchInterface/).

* In the `main` function or other bootstrap code, add the following

~~~ {.csharp}
var clientFormatter = New Runtime.Remoting.Channels.BinaryClientFormatterSinkProvider();
var serverFormatter = New Runtime.Remoting.Channels.BinaryServerFormatterSinkProvider();
serverFormatter.TypeFilterLevel = Runtime.Serialization.Formatters.TypeFilterLevel.Full;

var props = New System.Collections.Hashtable();
props["port"] = 0;
System.Runtime.Remoting.Channels.ChannelServices.RegisterChannel(
  New System.Runtime.Remoting.Channels.Tcp.TcpChannel(props, clientFormatter, serverFormatter), False);
~~~

(We are currently using .NET Remoting but are working on a version of the server which uses REST HTTP.)

* Allow the user to enter a computer name for the computer running Machine Watch (and perhaps a port).
   If no port is entered, default to port 8086.

* Access one of the exposed interfaces such as `IJobServerV2` via Remoting:

~~~ {.csproj}
var jobServer = (BlackMaple.MachineWatchInterface.IJobServerV2)
    System.Runtime.Remoting.RemotingServices.Connect(
      typeof(BlackMaple.MachineWatchInterface.IJobServerV2),
      "tcp://" + locationAndPort + "/JobServerV2");
~~~

For details on the interfaces and data available, see the
[source code](https://bitbucket.org/blackmaple/machinewatch/src/tip/lib/BlackMaple.MachineWatchInterface/).

### Writing a plugin for a cell controller

First, read the [integrations document](https://bitbucket.org/blackmaple/machinewatch/src/tip/integration.md) which
describes at a high level the data and communication between Machine Watch and the cell controller.

When Machine Watch starts, it searches the `plugins/` directory inside the AppDomain base directory for assemblies.
The first assembly which contains a type which implements the
[IServerBackend](https://bitbucket.org/blackmaple/machinewatch/src/tip/lib/BlackMaple.MachineFramework/BackendInterfaces.cs)
interface is loaded and used for communication between the service and the cell controller (the type must have a zero-argument constructor).
In addition, any types which implement the
[IBackgroundWorker](https://bitbucket.org/blackmaple/machinewatch/src/tip/lib/BlackMaple.MachineFramework/BackendInterfaces.cs) interface
are loaded and started.  There must be only one `IServerBackend` but can be multiple `IBackgroundWorker`s.

The `BlackMaple.MachineFramework` assembly assists with implementing this interface.  It contains code to store jobs and log data
in SQLite databases, make inspection decisions, and more.  It doesn't have to be used, but it does make implementing plugins easier.
The source code is here in the
[lib/BlackMaple.MachineFramework](https://bitbucket.org/blackmaple/machinewatch/src/tip/lib/BlackMaple.MachineFramework/)
directory, but is also available on NuGet at [BlackMaple.MachineFramework](https://www.nuget.org/packages/BlackMaple.MachineFramework/).
