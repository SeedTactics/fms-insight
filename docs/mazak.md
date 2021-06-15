---
title: Mazak Palletech Integration
nav: FMS Insight Server > Mazak
description: >-
  FMS Insight works with all Palletech cell controller versions from Mazak:
  Version E, Web Version, and Smooth PMC. FMS Insight can read all events from
  the cell controller and can also edit almost all the data in the cell controller.
---

# FMS Insight Mazak Palletech Integration

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
machine cycles, etc.) in the `c:\Mazak\FMS\Log` directory. Once enabled, FMS Insight will
automatically find any log entries from this directory.

To enable, go to the `c:\Mazak\FMS` directory. Rename the file `log-parameters.ini.sample` to `log-parameters.ini` and restart the Mazak Palletech software. The `log-parameters.ini` file
contains settings for the path to use and how often to delete, but FMS Insight works fine with
the default settings. If you want, you can change the log directory in `log-parameters.ini`
and then specify the same folder in the FMS Insight server configuration file.

## Load Instructions

On Version E and MazakWeb, one parameter must be changed. If you are using Mazak Smooth PMC, this step can be
skipped! Open the Mazak Palletech software, go to the parameter
edit screen, select `X`, and scroll to the setting `X-31`. Set the `X-31` setting from 0 to 1.
This setting will cause CSV files describing the current load and unload operation at the load
station to be output to the `c:\Mazak\FMS\LDS` directory. FMS Insight monitors this directory
and uses the CSV files to display the parts being loaded and unloaded from each pallet at
the load station.

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

## Starting Offset and Decrement Priority

The Mazak [config.ini](server-config) contains a setting `Use Starting Offset`
which control how new schedules are added into the cell controller.

When `Use Starting Offset` is enabled, new Mazak schedules will
be created with a "Due Date" and "Priority" field based on the
simulation's prediction of its actual start time. If disabled, a due
date of January 1, 2008 and priority of 91 is used for all Mazak schedules, effectively
disabling the use of due dates. Since Mazak uses due dates to decide which
part or pallet to start producing first, due dates can help smooth production
flow.

The main downside to using due dates and priorities is that Mazak will empty out a pallet
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

In our opinion, due dates and priorities should be enabled; the pallet
running empty is not typically that big of a deal. The primary goal of the
system is to keep the machines busy and a pallet with an empty location will
return to the load station after cutting just the material on the pallet (as
long as each process has a separate program). This increases slightly the
burden on the cart and load station, but these are not the bottleneck and
there should be enough work in the system to keep the machines busy. In
addition, occasionally a pallet will empty out in any case because today's
schedule had a limited quantity to allow a different part some time on the
machines. Finally, occasionally emptying out a pallet seems to help
tremendously with preventing traffic jams.

Finally, when FMS Insight adds a new day's schedule into the cell controller,
then FMS Insight will first decrement the priority on all existing
uncompleted schedules. Since each day a new schedule is downloaded, if
priorities of existing schedules are first decrement then any unfinished work
from the previous day gains higher priority and will finish before today's
schedule starts.
