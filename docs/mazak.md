---
title: Mazak Palletech Integration
nav: FMS Insight Server > Mazak
description: >-
  FMS Insight supports Mazak Palletech Web Version and Smooth/Neo PMC. FMS Insight reads controller
  activity and translates downloaded jobs into schedules.
---

FMS Insight supports Mazak Palletech Web Version and Smooth/Neo PMC. FMS Insight reads controller
events and status and translates downloaded jobs into controller schedules. The available operations
depend on the controller version and configured integration.

## Server Placement

FMS Insight usually runs on the Mazak cell-controller computer alongside Open Database Kit. It can
also run on a separate computer with access to the controller, including the
[isolated cell-network arrangement](/fms-insight/security#network-layout). Open Database Kit remains
on the controller. The controller connection and access to controller-generated files must be
configured for the chosen arrangement; the local paths below describe an installation on the
controller.

## Load station numbering

In the Mazak configuration section, Load Station Numbers = 10,30 maps controller L/U 1 to Insight
station 10 and controller L/U 2 to Insight station 30. This works like Machine Numbers: list
positions identify controller equipment, while values identify equipment in jobs, logs and APIs.
Both job downloads and status/event reads translate through the same map. Station display names are
maintained separately.

Omitting the list preserves identity numbering. Explicit lists must contain unique positive IDs and
cover every controller L/U station used. Job routes outside the map are rejected. The old Starting
Load Station Number setting has been removed and is no longer read; replace it with an explicit list
when updating configuration.

## Open Database Kit

To facilitate the communication between FMS Insight and the Mazak cell controller, you must acquire
a program called "Mazak Open Database Kit". This is a software program developed by Mazak which
allows safe access to the data inside the cell controller. Please contact your Mazak representative
and ask to obtain "Open Database Kit" that matches the specific cell controller (Web or Smooth PMC).

## Enable Log CSV

For Mazak Web and Mazak Smooth PMC, you must enable a setting in the Mazak cell controller which
will cause the Mazak cell controller to create log files of all events (pallet movements, machine
cycles, etc.) in the `c:\Mazak\FMS\Log` directory. Once enabled, FMS Insight will automatically find
any log entries from this directory.

To enable, go to the `c:\Mazak\FMS` directory. Rename the file `log-parameters.ini.sample` to
`log-parameters.ini` and restart the Mazak Palletech software. The `log-parameters.ini` file
contains settings for the path to use and how often to delete, but FMS Insight works fine with the
default settings. If you want, you can change the log directory in `log-parameters.ini` and then
specify the same folder in the FMS Insight server configuration file.

## Load Instructions

On MazakWeb, one parameter must be changed. If you are using Mazak Smooth PMC, this step can be
skipped! Open the Mazak Palletech software, go to the parameter edit screen, select `X`, and scroll
to the setting `X-31`. Set the `X-31` setting from 0 to 1. This setting will cause CSV files
describing the current load and unload operation at the load station to be output to the
`c:\Mazak\FMS\LDS` directory. FMS Insight monitors this directory and uses the CSV files to display
the parts being loaded and unloaded from each pallet at the load station.

## Simulation Lab

The Mazak Palletech cell controller software has a great ability to be run in a simulation mode
without any machines or carts attached. In this mode, the Mazak cell controller software has all
features active but instead of sending commands to the actual machines and carts, just simulates
their operation.

We recommend that you set up what we call a simulation lab, which consists of two computers in an
office. One computer will run the Mazak cell controller software in simulation mode, Open Database
Kit, and the FMS Insight server. The second computer will run a browser and access the FMS Insight
client. This mirrors the usual installation with Insight on the controller. If production will use a
separate Insight server, include that arrangement in the lab so its connections can be tested as
well.

A simulation lab allows testing, training, and experimentation. You can set up some dummy orders,
create a schedule, copy the schedule into the Mazak cell controller, run the schedule inside the
Mazak cell controller, see the assigned serial numbers, view inspection decisions, and examine
efficiency reports of cell operations, all without impacting the real system.

The simulation lab is also great for training before the machines are even installed, testing out
new scheduling or inspection signaling techniques, or training new operators and managers.

## Starting Offset and Decrement Priority

The Mazak [config.ini](/fms-insight/server-config) contains a setting `Use Starting Offset` which
controls how new schedules are added into the cell controller.

When `Use Starting Offset` is enabled, new Mazak schedules will be created with a "Due Date" and
"Priority" field based on the simulation's prediction of its actual start time. If disabled, a due
date of January 1, 2008 and priority of 91 is used for all Mazak schedules, effectively disabling
the use of due dates. Since Mazak uses due dates to decide which part or pallet to start producing
first, due dates can help smooth production flow.

Due dates and priorities affect which work Mazak selects and when a pallet is emptied between
schedules. When enabled, Insight also adjusts the priority of existing unfinished schedules as new
work is downloaded. Agree on these settings during commissioning and evaluate their effect on flow
with the actual part mix; they are not an operator adjustment for an individual delayed part.
