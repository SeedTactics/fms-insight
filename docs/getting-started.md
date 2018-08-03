---
id: getting-started
title: Getting Started
sidebar_label: Getting Started
---

FMS Insight is a program which runs on a flexible machining system (FMS)
cell controller which provides material tracking by serial, operator instructions
and data displays for loading/unloading, inspection decisions, data analytics to improve
the performance, and simplify job data creation in the cell controller.
Learn more in the [overview](overview.md).

## Downloads

The simplest configuration is to install FMS Insight directly on the cell
controller itself. FMS Insight requires .NET Framework
4.6.1, which is pre-installed on Windows 7 and 10 so typically no dependencies
must be installed first.

The following are links to the latest installers for FMS Insight for each supported cell controller.
The installers will also upgrade an existing install to the latest version.

* [FMS Insight Mazak Install.msi](/installers/FMS%20Insight%20Mazak%20Install.msi)
* [FMS Insight Makino Install.msi](/installers/FMS%20Insight%20Makino%20Install.msi)

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
browser. FMS Insight works with any recent version of Firefox, Chrome, or
Edge, so if Internet Explorer is the default browser, the address should be
opened in Firefox, Chrome, or Edge.

Instead, the address [http://localhost:5000](http://localhost:5000) can be
entered into the location bar of Firefox, Chrome, or Edge to open the FMS
Insight client page. Also, this address can be set as the homepage of
Firefox, Chrome, or Edge so that it opens automatically when the browser
starts.

## Explore data about the cell

The client has three tabs for exploring data about the cell:
[dashboard](client-dashboard.md), [efficiency](client-efficiency.md), and
[cost/piece](client-cost-per-piece.md).

## Implement station monitoring

The [stations](client-stations.md) tab in the client is designed to be viewed
on the factory floor by operators throughout the processes. The station
monitor screens are designed with touch-screens in mind, so we suggest either
mounting tablets or small touchscreen computers at various stations on the
factory floor. At these stations, the operator can view instructions and
details about the current operation, parts can be signaled for inspection,
marked with a serial, and the operator can record the completion of an
inspection or wash. Barcode readers and scanners can be attached to make
operator data entry a breeze.

## Automatic job download

FMS Insight provides the conduit to automatically create jobs and schedules
in the cell controller. External software is needed to create the jobs,
prioritize the work, and make sure no traffic jams occur (too much work for
too few machines) and that the cell stays efficient (enough jobs are created
to keep the machines busy). Despite requiring extra software, automatic job
download provides a large benefit in station utilization and lean
manufacturing when running a large variety of parts with variable part
quantities.

One option is paying for our [SeedTactic:
OrderLink](https://www.seedtactics.com/products/seedtactic-orderlink)
software which uses AI to automatically schedule a collection of orders and
due dates using a customized flexibility plan. *OrderLink* will translate ERP
orders to the format expected by FMS Insight without custom programming.
Alternatively, if daily scheduling takes place directly in the ERP system,
some custom software can be created to send the jobs and schedules from the
ERP system into FMS Insight; FMS Insight uses HTTP and JSON so the jobs just
need to be posted to FMS Insight in the correct format. More information is
available in the [creating jobs and schedules](creating-jobs.md)
documentation.

## ERP Data Reports

While the FMS Insight client provides some efficiency and cost reports, these
are useful for focusing on the cell operations only. FMS Insight does not
have the whole picture. FMS Insight provides the log data about the cell over
HTTP in the JSON format to easily allow the ERP software to integrate
information about the cell into its own reports. A preview of this data can
be obtained by vising the "Data Export" tab in the FMS Insight client. If
each material is assigned a workorder, FMS Insight will also provide a
workorder report in JSON over HTTP for even easier integration into the ERP
system. More information is available in the [workorder
reporting](workorder-report.md) documentation.
