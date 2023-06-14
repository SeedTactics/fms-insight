---
title: FMS Insight Client
nav: FMS Insight Client > Launch
description: >-
  FMS Insight provides a webpage viewable in any recent version of Firefox, Chrome,
  Edge, Android, or iOS browsers.  FMS Insight provides specific pages targeted at
  specific users which display relevant information.
---

# FMS Insight Client

FMS Insight provides touchscreen optimized webpages. Many people
throughout the factory interact with the cell in different ways. Thus, FMS Insight provides
specific pages targeted at specific users which display relevant information. We suggest that
each user bookmarks the relevant page; for example, engineers in the quality department should just
bookmark the quality page in FMS Insight, visiting it directly to monitor the cell. All FMS
Insight pages support a connected [barcode scanner](client-scanners) to scan the
serial number of a part.

## Shop Floor

FMS Insight provides several [station monitor](client-station-monitor)
pages. These pages are intended to be viewed at various operator stations
throughout the factory and provide information to the operator on what
actions to take. For example, at the load station it shows what to load and
unload and at the inspection stand it shows what inspections were triggered.

FMS Insight also provides a page for operators in the [tool room](client-tools-programs). This
page shows current tool use in the machines, tool usage per program, and estimated tool usage
for the remaining schedule in the cell controller.

## Daily Monitoring

FMS Insight provides targeted pages to assist with the day-to-day operation of the cell.
There is a targeted page for [factory floor supervisors](client-operations), [engineering/programming](client-engineering),
and [quality control](client-quality).

## Monthly Review

FMS Insight supports continuous improvement by assisting with a monthly review.
We suggest that approximately once a month, all stakeholders review the operation of the cell and
decide on potential improvements. FMS Insight provides a [flexibility analysis](client-flexibility-analysis)
page which displays a monthly summary of the cell, displays cost/piece, and helps identify areas for improvement.

## Browser Page

Once installed, the FMS Insight client can be launched by connecting to the
server using a browser. Any recent version of Firefox, Chrome, Edge, Android,
or iOS browsers will work (Internet Explorer is too old to work). By default,
FMS Insight listens on port 5000 and uses `http` so you can visit `http://<ip address or name of insigt server>:5000` from your browser. From the computer
on which you installed FMS Insight server, you can open the FMS Insight
client by visiting [http://localhost:5000](http://localhost:5000). Make sure
that firewall rules allow connections on port 5000 through to the FMS Insight
server. (The port 5000 and using https/SSL instead of http can be changed in
the [server configuration](server-config).)
