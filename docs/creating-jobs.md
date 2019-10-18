---
id: creating-jobs
title: Creating Jobs and Schedules
sidebar_label: Creating Schedules
---

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
due dates using a customized flexibility plan. _OrderLink_ will translate ERP
orders to the format expected by FMS Insight without custom programming.

Alternatively, if daily scheduling takes place directly in the ERP system,
some custom software can be created to send the jobs and schedules from the
ERP system into FMS Insight and then to the cell controller. To do so,
the parts and jobs are described using a JSON document which is then posted
to `http://<ip address or name of server>:5000/api/v1/jobs/add`.

The specific JSON format for the jobs is described in the
[swagger document](http://petstore.swagger.io/?url=https%3A%2F%2Fraw.githubusercontent.com%2FSeedTactics%2Ffms-insight%2Fmaster%2Fserver%2Ffms-insight-api.json), in particular the `NewJobs` model. For more details,
see the [source code](https://github.com/SeedTactics/fms-insight).
