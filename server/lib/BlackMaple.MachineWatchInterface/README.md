# Machine Watch Interface

[![NuGet Stats](https://img.shields.io/nuget/v/BlackMaple.MachineWatchInterface.svg)](https://www.nuget.org/packages/BlackMaple.MachineWatchInterface)

This project builds a DLL
[BlackMaple.MachineWatchInterface](https://www.nuget.org/packages/BlackMaple.MachineWatchInterface/).
The DLL contains the type definitions and APIs from the machine framework library. Older
versions of the software used .NET Remoting instead of HTTP for network communication, and
for .NET Remoting the serializable data types and interfaces must be shared between the client and server.
Thus the `BlackMaple.MachineWatchInterface` extracts these data types and interfaces and publishes
them on NuGet.  Some legacy clients still use this .NET Remoting API so the DLL is kept around on
NuGet.  Newly developed code should instead use the HTTP-based API which is described with Swagger.
