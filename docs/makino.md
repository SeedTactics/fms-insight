---
title: Makino Cell Integration
nav: FMS Insight Server > Makino
description: >-
  FMS Insight works with recent Makino cell controller versions. FMS Insight
  can read events from the cell controller and can also insert parts, jobs, and
  orders; at the current time FMS Insight cannot create pallets or fixtures.
---

# FMS Insight Makino Integration

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
in the [configuration file](server-config).

## Download Only Orders

When creating data in the cell controller (by writing a file to the ADE
folder), FMS Insight has two modes. First, it can include a description of
the part, job, and order, causing the Makino cell controller to create a new
part, a new job, and a new order. This is the default, since it minimizes the
amount of data which must be manually entered into the cell controller.
Alternatively, via a setting in the [config file](server-config), FMS
Insight will only create orders. FMS Insight will assume that a matching part
already exists inside the Makino cell controller.
