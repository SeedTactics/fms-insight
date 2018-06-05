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

# Events


## Editing Jobs

Finally, FMS Insight must be able to perform the following operations on jobs in the cell controller:

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

Outside of this, FMS Insight does not need the ability to edit jobs.  Instead of editing jobs, we just
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
