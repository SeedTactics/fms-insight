---
title: Cell Control Integration
date: September 5, 2017
author: John Lenz
---

This document describes the data which flows between us and the cell controller. This data flow is
mediated by our small service application Machine Watch, so in fact just the communication between
Machine Watch and the cell controller is required for integration.

Machine Watch does not get involved with the operation of the cell controller: it does not control
pallet movements, it does not decide what to load or unload, and it does not start or stop machines.
Instead, Machine Watch is a data conduit between the cell controller and the rest of the factory.

To develop this integration, we quote a fixed price to the cell manufacturer which is typically
then passed on to the customer as part of the entire system's quote.  To obtain a quote, we require
an idea of how the integration will occur; ideas and various options we have used in the past are
described in the second section below.

# Machine Watch <-> Cell Control Data Flow

This section describes the data that is passed between Machine Watch and the Cell Controller.

## Log of past events

The cell controller should output a log of events.

To help track material, Machine Watch assigns a `MaterialID` to each piece of material produced by
the cell. The `MaterialID` is a unique integer that identifies a specific piece of material and is
never reused. The `MaterialID` helps track a single piece of material throughout its entire journey,
from initial loading through reclamping and multiple processes to unloading. The `MaterialID` is
especially helpful for parts with multiple processes since the same `MaterialID` will appear on
multiple machine cycles.

`MaterialID`s can either be created and assigned by the cell controller or Machine Watch. It is
better that the cell controller assigns `MaterialID`s because the cell controller knows more about
the cell operation, but Machine Watch can generate and attach `MaterialID`s to existing events if
the cell controller does not create unique IDs for each piece of material.

For each job added into the cell, our software creates a `JobUnique`. This is a string which uniquely
identifies the job and is typically a UUID or other unique randomly generated ID. Each event about a
piece of material should include also the `JobUnique` that is causing the material to be manufactured.

Here are the events we look for:

##### Machine Cycle

An event for cycle start and cycle stop of each machine.  Output data for:

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

##### Load Cycle

The cell controller must produce an event for start of loading/unloading (when the pallet arrives at the load station) and
another event for end of loading/unloading (when the pallet leaves the load station).  The data we
look for is:

* Date and Time
* Station number
* Pallet

For start of load/unload (when the pallet arrives at the load station), the event should also contain:

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

For end of load/unload (when the pallet leaves the load station), the event should contain the same
material data as the start, except restricted to what the operator actually performed. Typically
this will be the same as what the cell controller requested at the start, but could be different.
For example, the cell controller requests that a part ABC be loaded onto the pallet but there is no
raw casting so the operator sends the pallet to the buffer stand instead to wait. In this case, the
start of load/unload event will include the request for ABC but the end of load/unload event will
not have a piece of material loaded.

## Incoming Jobs

On a schedule determined by the customer but typically once a day or once a week, our software will
examine the customer's orders and due dates and completed material and then use artificial intelligence
to optimize what to produce and what flexibility to use. To keep the system lean, only enough demand
to keep the machines busy is then sent to the cell controller in the form of a collection of jobs.
(These are also called schedules on some cell controllers.) New jobs are created each day and the
flexibility of which pallets or machines to use can change for each job.

Each job is assigned a unique string that is never reused. This `JobUnique` must then appear
in the log of events which allows us to connect events back to jobs.
The following is the data included as part of the job.

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

It could also include other data based on the needs of the cell controller.

## Current Status of the Jobs

Machine Watch requires read access to the current list of jobs. That is, all the jobs currently in
the system with data about them (part, priority, flexibility, planned quantity, etc.). The list
should also include data about the progress of the job: the competed quantity and the number of
parts currently in-process.

Machine Watch must have a way of requesting the current status of jobs. Machine Watch only
occasionally needs this information: once a day during the order optimization and occasionally
throughout the day based on user monitoring. Therefore, if there is some mechanism that Machine
Watch can use to request the data that is OK. Alternatively, the current status of jobs could also
just always be available (via a database for example).

## Current Pallet Status

Machine Watch would also like to access the current status of the cell: current location of the
pallets, current material loaded onto the pallets, machine status, load/unload instructions
(material to load/unload at the load station). All of this data can be recovered from the log of
events so is not strictly required. But, access to this data is a great help for debugging, testing,
and cross-checking the data. If it is easy to provide access to the current cell status then Machine
Watch would take advantage of this data.  Otherwise, we can get by without it.

## Editing Jobs

Finally, Machine Watch must be able to perform the following operations on jobs:

- delete completed jobs (alternatively, completed jobs can be automatically deleted by the cell controller).
  We are adding new jobs every single day so there must be some method to clean up old completed jobs.
  We will only ever delete completed jobs; in-process jobs will never be deleted by Machine Watch.

- edit priority of existing jobs (we use this to increase the priority of old jobs when we are
  adding new jobs into the system, so old jobs continue to run first before any of the new jobs).

- remove planned but not yet started quantity from existing jobs. When we are scheduling a new day,
  sometimes it is helpful to start from a blank slate and remove planned but not yet started
  quantities from jobs. We don't want to delete the job because any in-process material should
  finish, but we don't want the job to start any new parts. The easiest way to do that is to reduce
  the planned quantity.
  
  One major example of where this is helpful is machine downtime: say a machine goes down for 10
  hours. There will be a large backlog of work and the best technique to recover is to remove this
  backlog of work and let the artificial intelligence in our software re-assign flexibility. Because
  of order due dates, the work that was originally scheduled to run on the machine that went down
  might need to be spread across multiple machines in order to finish by the due date. The
  artificial intelligence inside our software automatically carries out these calculations, and just
  needs to be able to remove existing quantities from the old jobs to start from a blank slate each day.
  
Outside of this, Machine Watch does not need the ability to edit jobs.  Instead of editing jobs, we just
create new ones.

# Data Interchange Format

We have used all of the following techniques so can adapt to different possible solutions.

### .NET Framework DLL

The cell controller can provide a .NET Framework (version 3.5 or later) DLL which provides access to
this data. This approach only makes sense if such a DLL has already been created by the cell
controller and can just be provided. For developing something new, I recommend a different technique.

### Database

The cell controller can provide access to a database. We would prefer not to connect directly to the
cell controller's database since we do not want direct access or influence on the operation of the
cell itself. Instead, we would recommend to create a special intermediate communication-only
database. Both Machine Watch and the cell controller both connect to this intermediate database and
use it just to transfer data. The cell controller is then able to keep the communication database
isolated from the rest of the cell operation. This communication database makes sense only if it is
less work and programming than one of the other options.

### Microservices and IoT

[Microservices](https://martinfowler.com/articles/microservices.html) and the
[Internet of Things](https://aws.amazon.com/iot-platform/).  I think the
microservice architecture is a great fit for the communication between Machine
Watch and the cell controller, especially taking advantage of the recent
Internet of Things (IoT) technology. The IoT tooling has already developed the
communication, networking, and security which can be used off-the-shelf. The
communication can happen over a REST HTTP network transport or even better
using MQTT to define and access the data.  The main advantage is there are a
ton of tools and techniques since it is a very popular technology.

### Files

Another good technique is to have a special directory or several directories that both Machine Watch
and the cell controller monitor. On Windows, directories can be monitored using the file system
watcher to receive events when files are created. To communicate a request, a file is created for
the request and then the other party creates a response file.  When thinking about this interaction,
a HTTP-style request/response cycle is a good design.

Below, I give some sample files using JSON but these are just ideas or starting points.  The cell
controller should define a specific file format based on its internal data and capabilities.

##### Log Data

For each event (load/unload start, load/unload stop, machine start, machine stop), the cell
controller writes a separate file into a directory. Each event goes into its own file in a format
such as JSON or XML. Machine Watch monitors the directory for files and deletes the file once the event has
been processed. Each file is uniquely named based on a date/time stamp or event counter.  It is by far the easiest if
each file has a single event instead of multiple events in one file.

For example, in a file called `2017-08-23T16:44:12,533.json` (file named using ISO 8601 date format) could contain

~~~ {.json}
{
  "time": "2017-08-23T16:44:12,533",
  "type": "machinestart",
  "machine": 4,
  "pallet": 15,
  "program": "123456",
  "material":
    [
      {
        "id": 52122,
        "part": "ABC",
        "process": 1,
        "qty": 1,
        "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153"
      }
    ]
}
~~~

This would be an event for a machine start on machine 4 and pallet 15.  The material being cut by the program is a single piece of material
for part ABC on process 1 with `MaterialID` 52122 and the given job.

##### New Jobs

When Machine Watch wants a new job to be created, it will write a file into a special directory.  The cell controller will monitor the
directory and when a new file appears add the job into the system.  The cell controller should create a response file containing the
result (either success or error).

For example, Machine Watch might create a file named `26687b88-c81e-4a70-bfcd-6a3d012ac153.json` which contains

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "part": "ABC",
  "priority": 50,
  "plan-quantity": 72,
  "processes":
    [
      {
        "program": "123456",
        "parts-per-pallet": 1,
        "pallets": [1, 5, 12],
        "loads": [1, 2, 3],
        "machines": [1, 2],
        "fixture": "ABCfixture"
      },
      {
        "program": "987654",
        "parts-per-pallet": 1,
        "pallets": [1, 5, 12],
        "loads": [1, 2, 3],
        "machines": [1, 2],
        "fixture": "ABCfixture"
      },
    ]
}
~~~

This is a job for part ABC with two processes.  Again, the file format does not have to be exactly this and could be XML or
a different format, but it should be similar.

Once the cell controller imports the job, it should create a response file.  For example, if the import succeeds the
cell controller can create a file `26687b88-c81e-4a70-bfcd-6a3d012ac153.response.json` with the contents

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "success": true
}
~~~

Alternatively, if there is some error, the cell controller can instead create a file with contents such as

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "error": "Program 123456 not found"
}
~~~

Machine Watch and/or the cell controller delete these files once they have been processed.

##### Job Status

To request the current job status, Machine Watch can create an empty file called `request-job-status`.  When the cell controller
sees this file, it outputs the current jobs into a JSON or XML file.  In particular, the main pieces of information are the
quantities in process and completed quantity.  These files are deleted once they have been processed.  For example,
the cell controller could create a file `current-jobs.json` with

~~~ {.json}
{
  "jobs":
    [
      {
        "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
        "part": "ABC",
        "priority": 50,
        "plan-quantity": 72,
        "completed-quantity": 42,
        "processes":
          [
            {
              "program": "123456",
              "in-process-quantity": 3,
              "parts-per-pallet": 1,
              "pallets": [1, 5, 12],
              "loads": [1, 2, 3],
              "machines": [1, 2],
              "fixture": "ABCfixture"
            },
            {
              "program": "987654",
              "in-process-quantity": 8,
              "parts-per-pallet": 1,
              "pallets": [1, 5, 12],
              "loads": [1, 2, 3],
              "machines": [1, 2],
              "fixture": "ABCfixture"
            },
          ]
      },
      {
        "jobunique": "4a5e4c0f-d98e-4586-97f7-2372e517aa45",
        "part": "DEF",
        "priority": 80,
        "plan-quantity": 10,
        "completed-quantity": 8,
        "processes":
          [
            {
              "program": "765432",
              "in-process-quantity": 2,
              "parts-per-pallet": 2,
              "pallets": [5],
              "loads": [1, 2],
              "machines": [7],
              "fixture": "DEFfixture"
            }
          ]
      }
    ]
}
~~~

##### Editing Jobs

To edit a job, Machine Watch will create a JSON file in a specific directory.  The cell controller processes the JSON file and
responds with a response file.  For example, to delete a completed job, Machine Watch could create a file
`26687b88-c81e-4a70-bfcd-6a3d012ac153.edit.json` with contents

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "delete-completed": true
}
~~~

and the cell controller can respond in a file `26687b88-c81e-4a70-bfcd-6a3d012ac153.response.json` either

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "success": true
}
~~~

or something such as

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "error": "Job still has in-process quantity and can't be deleted"
}
~~~

For editing priority, Machine Watch can create a file such as

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "new-priority": 60
}
~~~

and the cell controller can respond with either a success or error file.

For removing planned quantity, Machine Watch can create a file such as

~~~ {.json}
{
  "jobunique": "26687b88-c81e-4a70-bfcd-6a3d012ac153",
  "remove-planned-quantity": 8
}
~~~

signaling that 8 parts should be removed from the given job.  The cell controller should return an error
if the job does not have 8 parts to remove (that is, the difference between the planned quantity and the
completed plus in-process quantity is less than 8).
