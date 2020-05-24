---
id: niigata
title: Niigata ICC Integration
sidebar_label: Niigata
---

FMS Insight works with a modified version of the Niigata ICC Cell Controller.
Contact your Niigata Sales or Support Representative to request more information
about using FMS Insight with the Niigata ICC Cell Controller.

## Configuration

#### Reclamp Stations

By default, all machine stops in the downloaded jobs are assumed to be machines.
To support reclamp stops, a specific name of the reclamp stop can be defined
in the FMS Insight [config.ini](server-config.md) file. Once defined,
any machine stop in the downloaded jobs which match this configured name will
be treated as reclamp stops.

#### Custom Machine Names

By default, ICC machine numbers are mapped to the identical machine number in
the downloaded jobs. That is, ICC machine 1 is mapped to MC1 in the jobs,
ICC machine 2 is mapped to MC2, and so on. The [config.ini](server-config.md) file can
specify an alternative job machine name and number to be assigned to each ICC machine
number. This allows different types of machines to be named differently in the
flexibility plan and jobs.
