---
title: Mazak Palletech Integration
nav: FMS Insight Server > Mazak
description: >-
  FMS Insight supports Mazak Palletech Web Version and Smooth/Neo PMC. FMS Insight can read all
  events from the cell controller and can also edit almost all the data in the cell controller.
---

# FMS Insight Mazak Palletech Integration

FMS Insight supports Mazak Palletech Web Version and Smooth/Neo PMC. FMS Insight can read all events
from the cell controller and can also edit almost all the data in the cell controller.

## Load station numbering

In the Mazak configuration section, Load Station Numbers = 10,30 maps controller L/U 1 to Insight
station 10 and controller L/U 2 to Insight station 30. This works like Machine Numbers: list
positions identify controller equipment, while values identify equipment in jobs, logs and APIs.
Both job downloads and status/event reads translate through the same map. Station display names are
maintained separately.

Omitting the list preserves identity numbering. Explicit lists must contain unique positive IDs and
cover every controller L/U station used. Job routes outside the map are rejected. The old Starting
Load Station Number setting has been removed and is rejected; replace it with an explicit list when
updating configuration.

## Open Database Kit

To facilitate the communication between FMS Insight and the Mazak cell controller, you must acquire
a program called "Mazak Open Database Kit". This is a software program developed by Mazak which
allows safe access to the data inside the cell controller. Please contact your Mazak representative
and ask to obtain "Open Database Kit" that matches the specific cell controller (Web or Smooth PMC).

## Experimental native tool counts (Smooth/Neo)

The public `FCREADDAT01.ToolPocket` table exposes tool-use time and life time, but no native use
counts in the inspected Smooth/Neo schema. By default, Insight reads that public table. Counts shown
by the tool report in this mode can be estimates based on machining history.

For testing only, `ExperimentalInternalToolData` replaces the entire tool read with one query of
`Mazak_Tool.dbo.Mazak_ToolData`, joined to `PMC_Basic.dbo.T5_Pocket` by device and pocket. Time,
count, and tool identity come from the same internal tool row, rather than combining observations
from the public and internal databases. This is a SQL READ COMMITTED read; it does not guarantee
that Mazak publishes its related internal updates atomically. Schedule and event reads still use
their ordinary sources. These internal tables are undocumented integration interfaces and require
Mazak confirmation and qualification on the target version before production use.

When using the proxy, install the updated proxy and Insight adapter, then set the **string**
registry value `ExperimentalInternalToolData` to `true` under the proxy's existing configuration
key, `HKEY_LOCAL_MACHINE\Software\SeedTactics\FMS Insight Mazak Proxy` (the 32-bit registry view for
a 32-bit proxy). Restart the proxy service. A setting in Insight's config does not change a remote
proxy. Without a proxy, set `Experimental Internal Tool Data = true` in Insight's `[Mazak]` section
and restart Insight. Both options default to false. Revert the setting and restart to restore the
public tool read. The experiment requires Smooth/Neo and read permissions on the internal tables;
query failures are logged and reported, without silently falling back to the public data.

Internal SQL column descriptions identify `mtdb_use`/`mtdb_lif` as used/life time and
`mtdb_cuse`/`mtdb_clif` as used/life quantity. The experiment assumes the time values use the same
seconds as the public interface and preserves native counts, including zero. Native count increments
are controller data, not an Insight assertion that one count equals one finished part. Counts feed
both the current tool report and machining-cycle usage history.

Until native status bits are verified, inclusion requires a valid, occupied machine pocket with a
matching internal tool row, excluding error and reserved pockets. This is a provisional inclusion
rule, not a decoded Mazak tool-data-valid flag. Stocker devices are excluded. Unknown tool status
bits are left uninterpreted, and the nominal diameter's numeric scale is not guessed. Duplicate
machine/pocket rows fail the read rather than producing ambiguous usage records.

Each experimental read logs its source, row/inclusion counts, and every raw and mapped tool row at
debug level, including the unmodified tool status/information words and pocket flags. With the proxy
these details go to its normal debug log under `%ProgramData%\FMS Insight Mazak Proxy`. On the real
machine, compare a few pockets with Mazak's tool screen: tool/group identity, pocket presence, time
units, zero/unset limits, count increments during machining, and resets on tool replacement. The
simulator schema accepts the query, but has no attached-machine tool data, so populated rows and
these semantics have not yet been validated on hardware.

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
client. This mimics the actual layout in operations, where the cell controller computer runs Open
Database Kit and the FMS Insight server and a separate computer on the factory floor or in the
office runs the client views.

A simulation lab allows testing, training, and experimentation. You can set up some dummy orders,
create a schedule, copy the schedule into the Mazak cell controller, run the schedule inside the
Mazak cell controller, see the assigned serial numbers, view inspection decisions, and examine
efficiency reports of cell operations, all without impacting the real system.

The simulation lab is also great for training before the machines are even installed, testing out
new scheduling or inspection signaling techniques, or training new operators and managers.

## Starting Offset and Decrement Priority

The Mazak [config.ini](server-config) contains a setting `Use Starting Offset` which control how new
schedules are added into the cell controller.

When `Use Starting Offset` is enabled, new Mazak schedules will be created with a "Due Date" and
"Priority" field based on the simulation's prediction of its actual start time. If disabled, a due
date of January 1, 2008 and priority of 91 is used for all Mazak schedules, effectively disabling
the use of due dates. Since Mazak uses due dates to decide which part or pallet to start producing
first, due dates can help smooth production flow.

The main downside to using due dates and priorities is that Mazak will empty out a pallet before
switching schedules. For example, consider a situation where you have a part PPP with two processes
and a fixture which can hold one process 1 material and one process 2 material. Also, consider there
are two schedules for part PPP; a schedule AAA for today's production and a schedule BBB for
tomorrow's production. Say that the AAA and BBB schedules have different due dates (say AAA has the
earlier due date). Then Mazak will first run the entire AAA schedule. In particular, the very last
cycle of AAA will run with a process 2 part and an empty process 1 location. Instead, if the due
dates of AAA and BBB are the same, Mazak will not empty out the pallet and instead on the very last
cycle of AAA in the process 2 location will add the very first cycle of BBB in the process 1
location.

In our opinion, due dates and priorities should be enabled; the pallet running empty is not
typically that big of a deal. The primary goal of the system is to keep the machines busy and a
pallet with an empty location will return to the load station after cutting just the material on the
pallet (as long as each process has a separate program). This increases slightly the burden on the
cart and load station, but these are not the bottleneck and there should be enough work in the
system to keep the machines busy. In addition, occasionally a pallet will empty out in any case
because today's schedule had a limited quantity to allow a different part some time on the machines.
Finally, occasionally emptying out a pallet seems to help tremendously with preventing traffic jams.

Finally, when FMS Insight adds a new day's schedule into the cell controller, then FMS Insight will
first decrement the priority on all existing uncompleted schedules. Since each day a new schedule is
downloaded, if priorities of existing schedules are first decrement then any unfinished work from
the previous day gains higher priority and will finish before today's schedule starts.
