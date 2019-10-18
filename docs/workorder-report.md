---
id: workorder-report
title: Workorder Reports in the ERP
sidebar_label: Workorder Report
---

While the FMS Insight client provides some efficiency and cost reports, these
are useful for focusing on the cell operations only. FMS Insight provides the
log data about the cell over HTTP in the JSON format to easily allow the ERP
software to integrate information about the cell into its own reports. A
preview of this data can be obtained by vising the "Data Export" tab in the
FMS Insight client.

The most useful query is to `http://<ip address or name of server>:5000/api/v1/log/workorders?ids=12345`, which returns a JSON document
describing the workorder `12345`. This JSON document contains all the serials
assigned to the workorder, the completed counts of parts in the workorder,
and most useful for costing, the elapsed and planned machine and labor
utilization summed over all parts assigned to the workorder. The elapsed
station time sums the wall-clock time from the beginning of the cycle or load
to the end of the cycle or load, while the planned station time sums up only
the expected cycle time or load/unload time. We suggest that you take the
planned machine and labor utilization for the workorder and integrate that
into the ERP cost reporting.

Full details of the data queries and resulting JSON are described in the
[swagger document](http://petstore.swagger.io/?url=https%3A%2F%2Fraw.githubusercontent.com%2FSeedTactics%2Ffms-insight%2Fmaster%2Fserver%2Ffms-insight-api.json)
and the [source code](https://github.com/SeedTactics/fms-insight) is available
with more details.
