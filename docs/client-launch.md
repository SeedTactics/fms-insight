---
title: FMS Insight Client
nav: FMS Insight Client > Launch
description: >-
  Open FMS Insight in a current browser on a computer, tablet, or phone, and choose the pages
  relevant to your role.
---

FMS Insight provides pages for operators, supervisors, engineers, quality staff, and planners. Open
the page that fits your role and bookmark it for day-to-day use. The pages support touchscreens and
connected [barcode scanners](/fms-insight/client-scanners).

Pages, station names, and available actions depend on the installation. The examples in these docs
use sample data; your cell may provide additional pages or use different terminology.

## Shop Floor

The [station pages](/fms-insight/client-station-monitor) show the work at each operator station. For
example, the load station shows material to load and unload, and the inspection station shows
requested inspections and recorded results.

The [Tools and Programs pages](/fms-insight/client-tools-programs) show current tool life, tool use
per program, and estimated tool use for the remaining scheduled work.

## Daily Monitoring

For day-to-day monitoring, choose the pages for
[factory floor supervisors](/fms-insight/client-operations),
[engineering/programming](/fms-insight/client-engineering),
[quality control](/fms-insight/client-quality), and [sales](/fms-insight/client-sales).

## Regular Review

The [Analysis pages](/fms-insight/client-efficiency) summarize production, resource use, and cost
per piece over the last 30 days or a calendar month. Use them for a
[regular review](/fms-insight/improve-fms) of cell performance and potential improvements.

## Opening FMS Insight

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
