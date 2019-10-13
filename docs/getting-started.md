---
id: getting-started
title: Getting Started
sidebar_label: Getting Started
---

FMS Insight is a program which runs on a flexible machining system (FMS) cell
controller which provides material tracking by serial, operator instructions
and data displays for loading/unloading, inspection decisions, daily
monitoring for supervisors and engineers, data analytics to improve the
performance, and simplify job data creation in the cell controller. Learn
more in the [overview](overview.md).

## Downloads

The simplest configuration is to install FMS Insight directly on the cell
controller itself. FMS Insight requires .NET Framework
4.6.1, which is pre-installed on Windows 7 and 10 so typically no dependencies
must be installed first.

The following are links to the latest installers for FMS Insight for each supported cell controller.
The installers will also upgrade an existing install to the latest version.

- [FMS Insight Mazak Install.msi](https://github.com/SeedTactics/fms-insight/releases/latest/download/FMS.Insight.Mazak.Install.msi)
- [FMS Insight Makino Install.msi](https://github.com/SeedTactics/fms-insight/releases/latest/download/FMS.Insight.Makino.Install.msi)

## Install the server

Install FMS Insight by running the `.msi` file. Once installed, the FMS
Insight server should start automatically as a Windows Service, automatically
detecting the cell controller software in it's default location. If FMS
Insight does not start correctly, you can look at the [error
log](server-errors.md) and change the [configuration](server-config.md). The
[Mazak](mazak.md) and [Makino](makino.md) documents contain more details
about each cell controller integration.

## Launch the client

The installer will create a link in the start menu called "FMS Insight". When
clicked, this link will open the FMS Insight client in the default web
browser. FMS Insight works with any recent version of Firefox, Chrome,
Edge, Android, or iOS browsers (Internet Explorer is too old to work properly).

The address `http://<ip address of cell controller>:5000` can be
entered into the location bar of the browser on any computer in the factory
(which can see the cell controller through the firewall). We suggest that
the page is bookmarked or set as the home page of the browser.

## Project Outline

##### Stage 1: Day-to-day monitoring of the cell

Once installed, the day-to-day monitoring pages for
[supervisors](client-operations.md) and [engineering](client-engineering.md)
will be updated with data about the past week of operation. Also, the
[flexibility analysis](client-flexibility-analysis.md) page will start
collecting data to help with monthly review.

##### Stage 2: Part Marking

To unlock the [full potential](material-tracking.md) of the remaining FMS Insight pages, each part
must be marked with a unique serial and that serial recorded in the FMS
Insight log. Each project is unique so this must be done with a
custom-developed plugin. Typically, marking is done inside the machine with a
special tool or via a label-printer at the load station.
You can [contact us](https://www.seedtactics.com/contact) to obtain a quote
for us to develop a customized part-marking plugin. Alternatively, you can
develop the plugin yourself; the [FMS Insight source
code](https://github.com/SeedTactics/fms-insight) contains some
documentation on building plugins.

Once part-marking is being used, the [station monitor
pages](client-station-monitor.md) will start working and can be used by
operators to help operate the cell.
Barcode readers and scanners can be attached to each computer on the factory floor
to read the marked serial.

##### Stage 3: Operator Instructions

[Operator Instructions](part-instructions.md) can be created for each part and station
in the factory. These instruction files can then be opened by the operator by
clicking on a card on the [station monitor page](client-station-monitor.md).

##### Stage 4: ERP to Cell Controller Automatic Job Download

FMS Insight provides the conduit to automatically create jobs and schedules
in the cell controller. External software is needed to read data from
the ERP and send it to the cell controller.

One option is paying for our [SeedTactic:
OrderLink](https://www.seedtactics.com/features#seedtactic-orderlink)
software which uses AI to automatically schedule a collection of orders and
due dates using a customized flexibility plan. _OrderLink_ will translate ERP
orders to the format expected by FMS Insight without custom programming.
Alternatively, you can develop some custom software to send the jobs and schedules from the
ERP system into FMS Insight, see the [creating jobs and schedules](creating-jobs.md)
documentation for more details.

##### Stage 5: Quality and Inspection Plan

Jobs downloaded into the cell controller from the ERP can optionally contain
an inspection plan; this includes the frequency of inspection, how separate
paths are tracked, and other data. Once inspections are included into the job
data, FMS Insight will automatically signal parts for inspection based on the
frequencies from the job and then display the inspections on the [station
monitor pages](client-station-monitor.md). Also, the [quality
monitoring](client-quality.md) page will be filled with data on the
inspections.

##### Stage 6: ERP Workorder Reports

Jobs downloaded into the cell controller from the ERP can optionally contain
a list of workorders. On the [station monitor pages](client-station-monitor.md),
the operator can assign each completed part to a workorder. FMS Insight then
provides a [workorder report](workorder-report.md) for each workorder. This
workorder report should be sent back into the ERP using custom software or
our _OrderLink_ product.
