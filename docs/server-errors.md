---
id: server-errors
title: Server Errors
sidebar_label: Errors
---

When Machine Watch encounters an error, it typically reports the error back over the network to the
SeedTactic software that requested the operation which will display the error. Occasionally, if an
error occurs which cannot be reported back to the SeedTactic software, the error will be logged into
a local error file. The most common type of error is a configuration error, where a configuration
setting is wrong or the cell controller is not configured to accept control from Machine Watch. The
error log file is located in the AppData folder (the same folder which stores the databases), but
then within a subdirectory named after the application version.

To assist with debugging, Machine Watch has the ability to output a trace of activity. The trace
consists of helpful messages describing what steps Machine Watch is taking. Most importantly,
it records the commands being sent to the cell controller and the responses from the
cell controller.  The trace is disabled by default because it generates a lot of data.  To enable it,
edit the `MachineWatch.config` file and change the "Output Trace Log" setting from `0` to `1` and restart
Machine Watch.  The trace log will be generated in the same folder as the error log (the global AppData folder).
Remember to disable the trace log after a day or two because it generates a lot of data.