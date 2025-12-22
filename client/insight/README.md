# FMS Insight Client

This is the client half of [FMS
Insight](https://github.com/SeedTactics/fms-insight). FMS Insight is a program
for a flexible machining system (FMS) cell controller which tracks material,
records a log of events, helps with inspections and serial marking, presents
details about the cell via webpages, and more. These web pages help operators
on the shop floor manage material, supervisors monitor the day to day
activities, and engineers evaluate the long-term efficiency of tools and the
cell.

The server is written in C# and available on nuget in the
[BlackMaple.MachineFramework](https://www.nuget.org/packages/BlackMaple.MachineFramework)
package. The client in this npm package is a React SPA which communicates with
the server with HTTP JSON requests. The client is built using vite and served
as static files, typically by the C# server. Because of that, the nuget package
for the server contain a built version of the client. If you are just using
FMS Insight and don't intend any changes to the client, you can just use the
BlackMaple.MachineFramework nuget. This npm package is needed only if you wish
to modify the client or import portions of the client into your own pages.

## Code Overview

The client stores data in [jotai](https://jotai.org/) stores. We use a
thick-client approach: each client loads a large amout of events at startup and
then uses a websocket connection to keep the stores updated as new events occur.
Most of the reports and pages are then generated on the client, typically using
jotai atoms. The atoms storing the information about the cell are in the
`src/cell-status` directory. The code in the `src/network` directory handles
the initial data load and the websocket connection to keep the jotai stores updated.

The `src/data` directory contains a large amout of the client-side business logic
and report generating typescript code. It takes the raw event data from the cell
status and turns it into a format suitable for display. Finally, the `src/components`
directory contains the React components which display the various reports and pages.
To customize, you should look at `src/components/App.tsx` for inspiration: it imports
the various pages and handles the routing.
