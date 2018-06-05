This directory contains the common server code, which includes the
data definitions, database code, and ASP.NET Core controllers.  Here is an overview
of the data and APIs provided by the server.

# Log of events

To help track material, FMS Insight assigns a `MaterialID` to each piece of material produced by
the cell. The `MaterialID` is a unique integer that identifies a specific piece of material and is
never reused. The `MaterialID` helps track a single piece of material throughout its entire journey,
from initial loading through reclamping and multiple processes to unloading. The `MaterialID` is
especially helpful for parts with multiple processes since the same `MaterialID` will appear on
multiple machine cycles.

`MaterialID`s can either be created and assigned by the cell controller or FMS Insight. It is
better that the cell controller assigns `MaterialID`s because the cell controller knows more about
the cell operation, but Machine Watch can generate and attach `MaterialID`s to existing events if
the cell controller does not create unique IDs for each piece of material.

For each job added into the cell, our software creates a `JobUnique`. This is a string which uniquely
identifies the job and is typically a UUID or other unique randomly generated ID. Each event about a
piece of material should include also the `JobUnique` that is causing the material to be manufactured.

Here are the events that are stored:

##### Machine Cycle

An event for cycle start and cycle stop of each machine.  Data includes

* Date and Time
* Station number
* Pallet
* Program
* Active operation time (time that the program is actually cutting/running.  For example, if the machine goes down the time between
  cycle start and cycle stop will be longer than the active operation time.)
* List of material being cut by the program:
    - MaterialID
    - Part Name
    - Process #
    - Part Quantity
    - JobUnique
* Any additional data which might help the customer.  In the past we have included probe data produced by the part program,
  tools used and their life, and others.  This kind of data is largely based on what the customer wants.  Our system can attach
  any extra data that the cell controller produces.

This data must be present in the cell controller log and custom code will translate the cycle event
from the cell controller to the FMS Insight log.

##### Load Cycle

We log an event for start of loading/unloading (when the pallet arrives at the load station) and
another event for end of loading/unloading (when the pallet leaves the load station).  The data we
store is

* Date and Time
* Station number
* Pallet

For start of load/unload (when the pallet arrives at the load station), the event also contains

* a list of material the operator should load onto the pallet:
    - MaterialID
    - Part Name
    - Process #
    - Quantity
    - JobUnique
* a list of material that the operator should remove from the pallet:
    - MaterialID
    - Part Name
    - Process #
    - Quantity
    - JobUnique
* a list of material that the operator should transfer from one process to another (reclamp):
    - MaterialID
    - Part Name
    - Process # to remove from
    - Process # to place into
    - Quantity
    - JobUnique

For end of load/unload (when the pallet leaves the load station), the event contains the same
material data as the start, except restricted to what the operator actually performed. Typically
this will be the same as what the cell controller requested at the start, but could be different.
For example, the cell controller requests that a part ABC be loaded onto the pallet but there is no
raw casting so the operator sends the pallet to the buffer stand instead to wait. In this case, the
start of load/unload event will include the request for ABC but the end of load/unload event will
not have a piece of material loaded.

This data must be present in the cell controller log and custom code will translate the cycle event
from the cell controller to the FMS Insight log.

#### Inspections

For inspection signaling, there is a log entry for each MaterialID and each
inspection type. The log entry will store a boolean true/false for if the
part should be signaled for inspection or not. FMS Insight typically
automatically creates these log events when the part is unloaded based on an
inspection pattern, but the log entries can also be created by the user
clicking a button in the HTML client.

FMS Insight also stores a separate event for each completed inspection. This
event is created by the user pressing a button in the client. The event
stores the date/time, MaterialID, inspection type, and if the inspection succeeded or failed.

#### Serial Assignment

When a part is marked with a serial, an event is recored in the log with the
date/time, `MaterialID`, and serial. These log entries can be created automatically
via setting in the server, created by the user scanning a barcode into the
HTML client, or using custom software. For example, if the process uses a
scheme where a tool is used to mark a serial and barcode on the part inside
the machine, a custom program is written to deposit a unique serial in a NC
program file and also record the depositied serial in the FMS Insight log.

#### Wash

The HTML client allows the user to click a button to record that final wash
is complete. This produces an event recording the date/time, MaterialID, and
the operator which completed the wash.

#### Workorder Assignment

The HTML client allows the user to assign a part to a workorder.  The workorder can be any
string.  When the user clicks the button to assign a part to a workorder, an event is added
to the log recording the date and time, MaterialID, and workorder.

FMS Insight also can record an event when a workorder is finalized.  This includes the
date/time and workorder.  Typically these events are created by custom code which
integrates with the customer's ERP system.

#### API Access

The log API provides several different ways of querying the log: obtaining
all log events in some date range, log events for a single MaterialID or
serial, or log events for completed parts. The API also provides one report,
a workorder report which calculates a summary of all parts assigned to a
specific workorder. Finally, the API allows creation of serial assignments,
workorders, wash cycles, and inspection cycles.  All of these APIs are implemented
by FMS Insight itself and don't require access to the cell controller; indeed all the
events have already been translated into FMS Insight's own SQLite database.

# Jobs

Each job is assigned a unique string that is never reused. This `JobUnique`
must then appear in the log of events which allows us to connect events back
to jobs. The following is the data included as part of the job.

- `JobUnique`: a string uniquely identifying the job.
- Part Name
- Priority
- Quantity to Produce
- Number of Processes
- (optional) Hold Pattern.  Occasionally the customer wants a hold pattern such as hold this
  job until 3rd shift.  We can adapt to whatever the cell controller provides (if anything).

For each process, the job includes the following data:

- list of allowed pallets
- list of allowed load/unload stations
- list of allowed machines
- program to run at the machine
- part quantity loaded onto a pallet at once
- fixture or fixtures to use

#### Current Status of the Jobs

FMS Insight will translate the current jobs in the cell controller into the
common JSON definition and then provide it over the network HTTP API. That
is, the current status includes all the jobs currently in the system with
data about them (part, priority, flexibility, planned quantity, etc.). The
list should also include data about the progress of the job: the competed
quantity and the number of parts currently in-process.

The cell controller must provide a method of accessing this data; the FMS Insight
integration will translate it into the common JSON format for the API.

#### Current Pallet Status

FMS Insight provides data over the network API on the current pallet status:
current location of the pallets, current material loaded onto the pallets,
machine status, load/unload instructions (material to load/unload at the load
station).

The current pallet status can be recovered from the log of events so is not
strictly required from the cell controller. But, access to this data in the
cell controller is a great help for debugging, testing, and cross-checking
the data. If it is easy to provide access to the current cell status then FMS
Insight can take advantage of this data. Otherwise, the FMS Insight
integration will create the current pallet status from the log of events.

#### Job History

FMS Insight stores a log of all jobs sent into the cell controller.  This log can be
queried by date range over the network API.

#### Adding Jobs

On a schedule determined by the customer but typically once a day or once a
week, some custom software outside of FMS Insight will examine the customer's
orders and due dates and completed material and then use artificial
intelligence or some other techniques to optimize what to produce and what
flexibility to use. To keep the system lean, only enough demand to keep the
machines busy is then sent to the cell controller in the form of a collection
of jobs. (These are also called schedules on some cell controllers.) New jobs
are created each day and the flexibility of which pallets or machines to use
can change for each job.

The network API provides a route which allows this custom software to post
the new jobs to create.  Each FMS Insight integration then has code to convert
the new jobs from the common JSON definition into the format expected by the
cell controller.

#### Decrement and Remove Jobs

FMS Insight provides an API to remove planned but not yet started quantity
from existing jobs. When we are scheduling a new day, sometimes it is helpful
to start from a blank slate and remove planned but not yet started quantities
from jobs. We don't want to delete the job because any in-process material
should finish, but we don't want the job to start any new parts. The easiest
way to do that is to reduce the planned quantity on the job.  The FMS Insight
integration provides a way to perform these edits on the cell controller itself.

Implementation of this is optional and depends on the level of access FMS Insight
has to the cell controller.  If such job modification is not implemented, it isn't that
bad since jobs are only sized for one day and it is easier operationally to just
let past jobs run to completion.

#### Queue Editing

Finally, FMS Insight provides an API to edit material in queues.