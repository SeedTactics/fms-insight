---
title: FMS Insight Client
nav: FMS Insight Client > Launch
description: >-
  FMS Insight provides a webpage viewable in any recent version of Firefox, Chrome, Edge, Android,
  or iOS browsers.  FMS Insight provides specific pages targeted at specific users which display
  relevant information.
---

FMS Insight provides touchscreen optimized webpages. Many people throughout the factory interact
with the cell in different ways. Thus, FMS Insight provides specific pages targeted at specific
users which display relevant information. We suggest that each user bookmarks the relevant page; for
example, engineers in the quality department should just bookmark the quality page in FMS Insight,
visiting it directly to monitor the cell. All FMS Insight pages support a connected
[barcode scanner](/fms-insight/client-scanners) to scan the serial number of a part.

Pages, station names, and available actions depend on the installation. The examples in these docs
use sample data; your cell may provide additional pages or use different terminology.

## Shop Floor

FMS Insight provides several [station monitor](/fms-insight/client-station-monitor) pages. These
pages are intended to be viewed at various operator stations throughout the factory and provide
information to the operator on what actions to take. For example, at the load station it shows what
to load and unload and at the inspection stand it shows what inspections were triggered.

FMS Insight also provides a page for operators in the
[tool room](/fms-insight/client-tools-programs). This page shows current tool use in the machines,
tool usage per program, and estimated tool usage for the remaining schedule in the cell controller.

## Daily Monitoring

FMS Insight provides targeted pages to assist with the day-to-day operation of the cell. There is a
targeted page for [factory floor supervisors](/fms-insight/client-operations),
[engineering/programming](/fms-insight/client-engineering),
[quality control](/fms-insight/client-quality), and [sales](/fms-insight/client-sales).

## Monthly Review

FMS Insight supports continuous improvement by assisting with a monthly review. We suggest that
approximately once a month, all stakeholders review the operation of the cell and decide on
potential improvements. FMS Insight provides a
[flexibility analysis](/fms-insight/client-efficiency) page which displays a monthly summary of the
cell, displays cost/piece, and helps identify areas for improvement.

## Browser Page

Once installed, the FMS Insight client can be launched by connecting to the server using a browser.
Use a current browser such as Firefox, Chrome, Edge, or Safari, including on supported tablets and
phones. Use the Insight address provided for your installation, including HTTPS and sign-in where
configured. Client devices need access to this address and any identity-provider endpoints used for
login. They do not need direct access to the machine network; see
[network access and security](/fms-insight/security).

At an operator station, the relevant page can be set as the browser homepage or opened automatically
in full-screen mode. Keep the device and browser updated through the site's normal IT management.

By default, FMS Insight listens on port 5000 and uses `http` so you can visit
`http://<ip address or name of Insight server>:5000` from your browser. From the computer on which
you installed FMS Insight server, you can open the FMS Insight client by visiting
[http://localhost:5000](http://localhost:5000). The port and HTTPS settings can be changed in the
[server configuration](/fms-insight/server-config). Firewall rules should allow approved devices to
reach the endpoint chosen for the installation.
