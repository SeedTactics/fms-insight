---
id: niigata
title: Niigata ICC Integration
sidebar_label: Niigata
---

FMS Insight works with a modified version of the Niigata ICC Cell Controller.
Contact your Niigata Sales or Support Representative to request more information
about using FMS Insight with the Niigata ICC Cell Controller.

## Configuration

#### Reclamp Stations

By default, all machine stops in the downloaded jobs are assumed to be machines.
To support reclamp stops, a specific name of the reclamp stop can be defined
in the FMS Insight [config.ini](server-config.md) file. Once defined,
any machine stop in the downloaded jobs which match this configured name will
be treated as reclamp stops.

#### Custom Machine Names

By default, ICC machine numbers are mapped to the identical machine number in
the downloaded jobs. That is, ICC machine 1 is mapped to MC1 in the jobs,
ICC machine 2 is mapped to MC2, and so on. The [config.ini](server-config.md) file can
specify an alternative job machine name and number to be assigned to each ICC machine
number. This allows different types of machines to be named differently in the
flexibility plan and jobs.

#### Machine IP Addresses

If specified, FMS Insight will use the Machine IP addresses and ports to load
tooling data from the machines, record it as part of the log of events, and display some
[summary reports](client-tools-programs.md) of tooling data. FMS Insight does not
require the IP addresses to be specified; if they are missing, no tool data is loaded
but all other functions of FMS Insight work (managing jobs, logging events, etc.).

#### Sized Queues

FMS Insight supports specifying a size for each queue defined in the config file.
FMS Insight will then hold and unhold pallets that are waiting to be unloaded so that
the queue does not exceed the configured size. Note that by using the
[queues page](client-station-monitor.md), an operator can manually insert more material
into the queue beyond the configured size. The configured size is only used for holding
and unholding pallets.

## Operations

FMS Insight translates the [jobs](creating-jobs.md) into the Niigata ICC Cell Controller
via three mechanisms: adding programs, setting pallet master routes, and controlling
a few flags on the pallet tracking.

#### Programs

The jobs can optionally contain the program content for each process and machining stop.
If the job does not contain the program content, FMS Insight assumes the program has
already been manually registered with the Niigata ICC.

If the program content is included, FMS Insight will manage revisions and register
the program with the Niigata ICC. FMS Insight will compare the program content from
the newly created jobs and the already existing program. If they are the same, the
existing program is reused. If they are different, a new revision is created by
selecting a new program number and registering the new program content with the
Niigata ICC. This leaves the old program revision unchanged so both the old and
new revisions are simultaneously in the cell controller. FMS Insight tracks all
material and will use either the old revision or the new program revision
based on which material is currently loaded onto the pallet. Once the old
job completes, FMS Insight will remove the old program revision from the Niigata
ICC. Program revisions and Niigata ICC program numbers can be seen on the
[program report page](client-tools-programs.md).

#### Pallet Master

For each pallet, the Niigata ICC stores the "Pallet Master", a list of stops that
the pallet visits. The route starts with LD (load), has a sequence of MC (machining)
and RC (reclamp) stops, and ends with a UL (unload). Each load, unload, or reclamp stop
contains a list of load station numbers to use. Each machining stop contains the
machines to use plus a list of program to run.

FMS Insight sets the pallet master steps based on the data in the created jobs. For
each available pallet, FMS Insight sorts the jobs by date and priority and decides
which job should run on the pallet. FMS Insight then uses the job data to completely
replace the pallet master with the sequence of steps from the job. FMS Insight
uses the comment field to keep track of which job is currently assigned to the
pallet. FMS Insight continuously updates the pallet master data as jobs
complete and other jobs are assigned to the pallet; FMS Insight will only update
a pallet if it is waiting in the stocker with the "No Work" flag set (see below for
more information about the No Work flag).

#### Pallet Location and Status

For each pallet, the Niigata ICC stores the "Tracking Data", consisting of the current
pallet location, currently executing step, count of remaining pallet cycles, and a
couple of boolean flags.

FMS Insight reads but does NOT have write access to the current pallet location and
currently executing step (Before-LD, After-LD, Before-MC, etc.). The Niigata ICC
keeps these fields updated as the pallet moves through the route defined in the
pallet master. FMS Insight watches these values change and uses them to create
a log of what happens. The Niigata ICC does allow the user (but not FMS Insight)
to override the pallet location or current step, which can be used in emergencies;
overriding the pallet location and current step is not required during normal
operation. FMS Insight uses the transitions of pallet locations and currently
executing step to create log entries, so if they are manually edited by
the user, FMS Insight may miss log entries for the current cycle. FMS Insight
will recover once a new pallet cycle starts.

#### Pallet Cycle Count

For each pallet, the Niigata ICC tracks a count of remaining pallet cycles. The Niigata
ICC decrements this on the completion of the UL (unload) step. If it is greater
than zero and the operator presses the LOAD button, the Niigata ICC switches to the
After-LD (load) step and thus starts the route again. If the count is zero OR the
operator presses the button Niigata calls UNLOAD but might more accurately be translated
"NOT LOADED", the Niigata ICC places the "No Work" flag onto the pallet, which
causes the pallet to route to the stocker. (See below for more about the No Work flag.)

FMS Insight always keeps the cycle count at 1 and increments it when the pallet arrives
to be unloaded if needed. That is, consider when a pallet arrives to be unloaded. The
current remaining cycle count is 1. FMS Insight will check the available material in the
[queues](material-tracking.md) to determine if there is a piece of material to be loaded.
If no material is available, FMS Insight will do nothing; the Niigata ICC will then
decrement the cycle count from 1 to 0 and send the pallet to the stocker no matter if the
operator presses the LOAD or UNLOAD/NOT LOADED button. Instead, if there is available
material to be loaded, FMS Insight will increment the remaining pallet cycles to 2. When
the operator presses the LOAD button, the Niigata ICC will then set the status to After-LD
(load) and proceed to route the pallet back through the pallet master steps. If instead
the operator presses the UNLOAD/NOT LOADED button, the Niigata ICC will set the No Work
flag and route the pallet to the stocker.

The FMS Insight [load station page](client-station-monitor.md) will always contain the
expected operations to occur at the load station.

#### No Work and Skip

For each pallet, the Niigata ICC stores two boolean flags: "No Work" and "Skip".

The "Skip" flag, also called hold, causes the Niigata ICC to move the pallet to
the stocker and then ignore it. FMS Insight uses the "Skip" flag to implement
sized queues. If there is an in-process queue with a specific size, FMS Insight
will set the "Skip" flag on all pallets destined to be unloaded into this queue.
FMS Insight then removes the "Skip" flag on only a single pallet at a time and only
when the unload would not increase the number of parts beyond the size of the queue.

The "No Work" flag is used to override the "Remaining Pallet Cycles" count. If the
No Work flag is set, the Niigata ICC assumes that the pallet is empty, sends the
pallet to the stocker, and ignores it. While similar to Skip, Skip is used to
hold a pallet in the middle of the route while No Work is used to not start the
pallet route at all. When the remaining pallet cycle count drops to zero, the
Niigata ICC sets the No Work flag when the pallet is unloaded. Also, if the
operator presses the UNLOAD/NOT LOADED button, the No Work flag is set
no matter what the remaining pallet cycle count is.

FMS Insight uses the No Work flag on a pallet in the stocker to trigger its
check for new jobs to assign to the pallet. For each pallet in the stocker
with No Work set, FMS Insight will sort the available jobs by date and
priority. For each job, FMS Insight checks if there is available material
to be loaded. If a piece of material for some job is found, FMS Insight
will clear the No Work flag (and also optionally update the pallet master
data for the new job, if required).

## Operator Procedures

#### Load Station Normal Operation

FMS Insight's [load station page](client-station-monitor.md) will display what the Niigata ICC is
expecting to happen at the load station. Specifically, the page will display the material
to load, the material to unload, and the material to transfer between faces (if the pallet has
multiple faces). If the operator performs all the tasks as specified, the operator presses
the "LOAD" button. The Niigata ICC will then begin the route on the pallet for the material
that was just loaded. (Note that if the instructions specify that nothing is to be loaded,
the operator can press either "LOAD" or "UNLOAD/NOT LOADED", both will preform the same
action.)

#### Material which is unloaded must be reworked or scrapped

Consider when the operator unloads a piece of material which cannot continue with the normal
flow but must be set aside to be reworked or possibly scrapped. If this is on the final process,
nothing needs to be specified. If instead this is an in-process piece of material, it
can be signaled to be quarantined after the unload completes. To do so, on the
load station page, click the material card for the piece of material. The dialog that
opens will contain a "Signal For Quarantine" action. Once clicked, FMS Insight will move
the material to the quarantine queue once the unload finishes.

If the material has already been unloaded without clicking the "Signal for Quarantine" action,
FMS Insight will add the material that was unloaded
into the in-process queue, visible on either the
[load station or queues pages](client-station-monitor.md).
To remove the material, the operator must click on the material card and then
click either the "Remove From System" or "Quarantine Material" action
(depending on if [quarantine queues](material-quarantine.md) are configured).
This will either remove the material from the system completely or remove it
from the active FMS Insight queue and place it in a special quarantine queue.
In either case, once the material is no longer in the queue, FMS Insight will
not set pallet master or tracking data into the Niigata ICC for this material.

Note that depending on timing and pallet availability, it may be the case that a
pallet has already been activated by the time the operator removes the material.
The operator must still remove the material from the queue as specified above,
and then reject the load as specified in the next section.

#### Load Station Material Can't Be Loaded

Consider that a pallet arrives at the load station, the Niigata ICC is expecting something to be
loaded, but the material is not available to be loaded. This could be because the material was removed
from the queue as described in the previous section but because of the timing a pallet had already been
activated. It could also be because the operator forgot to remove the material from the queue, or didn't
notice that the material was bad until the load operation started.

In any case, the operator should first confirm that the material is removed from the FMS Insight queue
and then press the UNLOAD/NOT LOADED button. This will cause the Niigata ICC to route the empty pallet
back to the stocker and as long as there is no material in the FMS Insight queues, FMS Insight will not
reactivate the pallet until more material enters the queue.

#### Reworked material is ready to re-enter the flow

Consider when material was removed from the system or placed into [quarantine queues](material-quarantine.md)
as described above, and now the material has been successfully fixed and is ready to complete the remainder of
the process. Once the material arrives back at the load station and is ready to go, the operator adds the material
back into the in-process queue. To do so, the operator clicks a button on the queues page, enters
or scans the serial number of the material to be added, and then verifies the job and process number. The
material will then be placed into the queue and FMS Insight will then activate the appropriate pallets
in the Niigata ICC.
