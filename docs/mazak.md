---
id: mazak
title: Mazak Palletech Integration
sidebar_label: Mazak
---

FMS Insight works with all Palletech cell controller versions from Mazak:
Version E, Web Version, and Smooth PMC. FMS Insight can read all events from
the cell controller and can also edit almost all the data in the cell controller.

## Open Database Kit

To facilitate the communication between FMS Insight and the Mazak cell
controller, you must acquire a program called "Mazak Open Database Kit". This
is a software program developed by Mazak which allows safe access to the data
inside the cell controller. Please contact your Mazak representative and ask
to obtain "Open Database Kit" that matches the specific cell controller
(Version E, Web, or Smooth PMC).

## Enable Log CSV

For Mazak Web and Mazak Smooth PMC, you must enable a setting in the Mazak cell controller
which will cause the Mazak cell controller to create log files of all events (pallet movements,
machine cycles, etc.) in the `c:\Mazak\FMS\Log` directory.  Once enabled, FMS Insight will
automatically find any log entries from this directory.

## Simulation Lab

The Mazak Palletech cell controller software has a great ability to be run in a
simulation mode without any machines or carts attached. In this mode, the
Mazak cell controller software has all features active but instead of sending
commands to the actual machines and carts, just simulates their operation.

We recommend that you set up what we call a simulation lab, which consists of
two computers in an office. One computer will run the Mazak cell controller
software in simulation mode, Open Database Kit, and the FMS Insight server.
The second computer will run a browser and access the FMS Insight client.
This mimics the actual layout in operations, where the cell controller
computer runs Open Database Kit and the FMS Insight server and a separate
computer on the factory floor or in the office runs the client views.

A simulation lab allows testing, training, and experimentation. You can set
up some dummy orders, create a schedule, copy the schedule into the Mazak
cell controller, run the schedule inside the Mazak cell controller, see the
assigned serial numbers, view inspection decisions, and examine efficiency
reports of cell operations, all without impacting the real system.

The simulation lab is also great for training before the machines are even
installed, testing out new scheduling or inspection signaling techniques, or
training new operators and managers.

## Avoid the cell controller on your factory network

Mazak does not recommend that you enable Windows updates, and Mazak cell
controllers run on Windows XP and Windows 8 which no longer receive support
and security updates from Microsoft.  In addition, the FMS Insight server
requires Windows 7 or later so can't be installed on Version E cell controllers
using Windows XP.  To avoid putting the cell controller on your factory network
directly, we suggest the following setup:

* Obtain a Windows 10 computer  to be placed on the factory floor nearby the
  cell controller. (Windows 7 also works but Microsoft is ending support for
  Windows 7 in 2020.) Call this computer the FMS Insight Server. This
  computer will run only the FMS Insight server software. The FMS Insight
  server software is very lightweight and will run on any relatively recent
  computer; any modern Intel or AMD CPU, 1 GB of RAM, and 10 GB of disk space
  are more than enough (the cheapest computer you can buy will have much more
  than this). The main requirement is that this Windows 10 computer have two
  network cards. The only pre-installed software required is Microsoft's .NET
  Framework version 4.6.1, which comes pre-installed on Windows 10.

* Connect one network card in the FMS Insight Server to your factory network.
  Connect it to your domain using your usual usual security policies and
  enterprise domain control.  Automatic Windows Updates can be enabled.

* Connect the second network card in a point-to-point connection (perhaps with a
  crossover cable) to the Mazak Cell Controller using static IPs
  for both sides of the connection. On the FMS Insight Server, you can set up
  a firewall to disallow all network traffic between the factory network and
  the cell controller. The only computer that needs direct communication with
  the Mazak cell controller is the FMS Insight Server. All other computers
  and software in the factory will connect to the FMS Insight Server over the
  factory network.

* (Version E And Web) For Mazak Web or Mazak Version E, on the Mazak cell controller
  create a network share of the `c:\Mazak\NFMS\DB`
  directory. On the FMS Insight Server computer, create a mapped drive (e.g.
  `Z:`) for the share so that the FMS Insight Server computer can access the
  DBs. On the FMS Insight server computer, edit
  [config.ini](server-config.md) and change two settings in the Mazak section. First, set
  `VersionE = true` and then set `Database Path = Z:\` (or whichever mapped
  drive letter you chose).

* (Web and Smooth PMC) For Mazak Web or Mazak Smooth PMC, on the Mazak cell controller
  create a network share of the `c:\Mazak\FMS\Log` directory.  On the FMS Insight server
  computer, create a mapped drive (e.g. `Y:`) for the share so that the FMS Insight Server
  computer can access the Mazak log files.  On the FMS Insight server computer, edit
  [config.ini](server-config.md) and set `Log CSV Path = Y:\`.

* (Smooth PMC) Mazak Smooth PMC uses SQL Server which FMS Insight can access over the network.
  Edit the [config.ini](server-config.md) file and set the `SQL ConnectionString` setting
  to a SQL Server connection string which uses the IP address of the Mazak cell controller.

## Starting Offset and Decrement Priority

The Mazak [config.ini](server-config.md) contains two settings `Use Starting Offset For Due Date` and
`Decrement Priority On Download` which control how new schedules are added into the cell controller.

When `Use Starting Offset For Due Date` is enabled, new Mazak schedules will
be created with a "Due Date" field based on the current date and a time based
on the simulations prediction of its actual start time. If disabled, a due
date of January 1, 2008 is used for all Mazak schedules, effectively
disabling the use of due dates. Since Mazak uses due dates to decide which
part or pallet to start producing first, due dates can help smooth production
flow

The main downside to using due dates is that Mazak will empty out a pallet
before switching schedules. For example, consider a situation where you have
a part PPP with two processes and a fixture which can hold one process 1
material and one process 2 material. Also, consider there are two schedules
for part PPP; a schedule AAA for today's production and a schedule BBB for
tomorrow's production. Say that the AAA and BBB schedules have different due
dates (say AAA has the earlier due date). Then Mazak will first run the
entire AAA schedule. In particular, the very last cycle of AAA will run with
a process 2 part and an empty process 1 location. Instead, if the due dates
of AAA and BBB are the same, Mazak will not empty out the pallet and instead
on the very last cycle of AAA in the process 2 location will add the very
first cycle of BBB in the process 1 location.

In our opinion, due dates should be enabled; the pallet running empty is not
typically that big of a deal. The primary goal of the system is to keep the
machines busy and a pallet with an empty location will return to the load
station after cutting just the material on the pallet (as long as each
process has a separate program). This increases slightly the burden on the
cart and load station, but these are not the bottleneck and there should be
enough work in the system to keep the machines busy. In addition,
occasionally a pallet will empty out in any case because today's schedule had
a limited quantity to allow a different part some time on the machines.
Finally, occasionally emptying out a pallet seems to help tremendously with
preventing traffic jams.

When FMS Insight adds a new day's schedule into the cell controller, if the
decrement priority setting is enabled then FMS Insight will first decrement
the priority on all existing uncompleted schedules. If disabled, no change to
the priorities will be made so all schedules will have identical priorities.

Since each day a new schedule is downloaded, if priorities of existing
schedules are first decrement then any unfinished work from the previous day
gains higher priority and will finish before today's schedule starts. Similar
to due dates and starting offsets, if schedules have different priorities
then then Mazak will empty out the pallet before switching to the new
schedule. Similar to before, we recommend enabling the decrement priority
setting.
