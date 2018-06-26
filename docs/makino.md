---
id: makino
title: Makino Cell Integration
sidebar_label: Makino
---

FMS Insight works with recent Makino cell controller versions. FMS Insight
can read events from the cell controller and can also insert parts, jobs, and
orders; at the current time FMS Insight cannot create pallets or fixtures.

## ADE Folder and SQL Server Connection

FMS Insight communicates with the Makino cell controller via two methods.
First, FMS Insight uses a read-only connection to the Makino database to
obtain log and status information about the cell. Secondly, when downloading
parts, jobs, and orders, FMS Insight creates files in the Makino ADE folder.
This is a folder which the Makino cell controller watches for changes; when a
new file appears in this folder the Makino cell controller will itself import
the file. Thus, FMS Insight never modifies the cell data directly but instead
just creates the appropriate file in the ADE folder.

By default, FMS Insight uses `c:\Makino\ADE` as the ADE folder and connects
to the database on the local computer. Both of these settings can be changed
in the [configuration file](server-config.md).

## Download Only Orders

When creating data in the cell controller (by writing a file to the ADE
folder), FMS Insight has two modes. First, it can include a description of
the part, job, and order, causing the Makino cell controller to create a new
part, a new job, and a new order. This is the default, since it minimizes the
amount of data which must be manually entered into the cell controller.
Alternatively, via a setting in the [config file](server-config.md), FMS
Insight will only create orders. FMS Insight will assume that a matching part
already exists inside the Makino cell controller.

## Avoid the cell controller on your factory network

To avoid putting the cell controller on your factory network directly, we
suggest the following setup:

* Obtain a Windows 10 computer to be placed on the factory floor nearby the
  cell controller. (Windows 7 also works but Microsoft is ending support for
  Windows 7 in 2020.) Call this computer the FMS Insight Server. This
  computer will run only the FMS Insight server software. The FMS Insight
  server software is very lightweight and will run on any relatively recent
  computer; any modern Intel or AMD CPU, 1 GB of RAM, and 10 GB of disk space
  are more than enough (the cheapest computer you can buy will have much more
  than this). The main requirement is that this Windows 10 computer have two
  network cards. The only pre-installed software required is Microsoft's .NET
  Framework version 4.6.1, which comes pre-installed on Windows 10.

* Connect one network card in the FMS Insight Server to your factory network.
  Connect it to your domain using your usual security policies and
  enterprise domain control.  Automatic Windows Updates can be enabled.

* Connect the second network card in a point-to-point connection (perhaps with a
  crossover cable) to the Makino Cell Controller using static IPs
  for both sides of the connection. On the FMS Insight Server, you can set up
  a firewall to disallow all network traffic between the factory network and
  the cell controller. The only computer that needs direct communication with
  the Makino cell controller is the FMS Insight Server. All other computers
  and software in the factory will connect to the FMS Insight Server over the
  factory network.

* On the Makino cell controller,
  create a network share of the `c:\Makino\ADE` directory.  On the FMS Insight server
  computer, create a mapped drive (e.g. `Z:`) for the share so that the FMS Insight Server
  computer can access the Makino ADE folder.  On the FMS Insight server computer, edit
  [config.ini](server-config.md) and set `ADE Path = Z:\`.

* FMS Insight can access the Makino SQL server directly over the network.
  Edit the [config.ini](server-config.md) file and set the `SQL Server
  Connection String` setting to a SQL Server connection string which uses the
  IP address of the Makino cell controller.