---
title: Use analytics to improve a FMS
nav: Procedures > Monthly Review
description: >-
  Implement continuous improvement of a flexible machining system by reviewing
  targeted metrics which expose cell efficiency and cost per piece.
---

# Use analytics to improve a FMS

The performance of an automated flexible machining system (FMS) depends on
the interaction between scheduling, part programming, operations, engineering
design, quality control, tooling, purchasing, and more. This encourages a
holistic view of the entire system instead of focusing on each component
(such as part programming) in isolation. Recent advances in big data
technology make it easier than ever to understand the entire manufacturing
process, investigate trade-offs, and calculate metrics on monthly
cost-per-piece, resource utilization, and system bottlenecks. Data analytics
leads to understanding the entire system as a whole and actionable insights
for improvements and future investments in automation and process
improvement.

## Actionable Insights

Data collection and analytics focuses on presenting [actionable
insights](https://www.forbes.com/sites/brentdykes/2016/04/26/actionable-insights-the-missing-link-between-data-and-business-value/).
Raw data collection and information visualization can generate many metrics and insights into a
system, but the challenge is to produce clear, relevant, specific insights. For a FMS, in our
experience there are two metrics which lead to actionable insights: cost-per-piece and resource utilization.

Cost-per-piece is a great actionable insight because it is easy to understand, specific to the
ultimate purpose of the FMS, highly relevant to the business, and can be clearly presented to all
stakeholders. For ordering, an accurate analysis of cost-per-piece allows easy quoting of work.
For accounting, cost-per-piece allows justifications for future capital investments in new machines
or new automation. For operational management, cost-per-piece helps understand the impact of a
process change. That is, management can see if a process change has improved cost-per-piece and
therefore if the process change should be kept or adjusted. The entire business benefits from
visible and accurate cost-per-piece metrics, because various departments and employees can see that
even if a change requires them to perform extra work, it improves the overall business value of the
system. The only data required from daily operations is the monthly quantity of parts produced,
and it is implemented on the [flexibility analysis page](client-flexibility-analysis).

While it is the ultimate goal, overall cost-per-piece is not a useful metric to improve the system.
Sure, a cost-per-piece breakdown will show the costs of labor, machining, and tooling but how do
you turn that into a plan to change something? Also, focusing on improving cost-per-piece directly
risks optimizing cost by sacrificing quality. Instead, the best metric for continuous improvement
is the bottlenecks and utilization of system resources, since the goal is to produce more stuff with
the same machines and quality.

Utilization and bottleneck metrics are more difficult to turn into actionable insights. Many
companies generate OEE (operational equipment effectiveness) data or collect machine utilization and
bottleneck information, but seemingly inconsequential details in one area can have large impacts in
other areas. For example, the chosen part programming technique might require complex operational
part tracking on the shop floor or scheduling techniques might increase quality control overhead.
When each department focuses only on their own slice of the system, they can't turn these insights
into actions.

To turn the system utilization and performance metrics into actionable insights, we suggest the
creation of a tactics council. The tactics council is made up of a representative from each
department and component of the system, and the council's job is to decide on the overall tactics for system
operation. By combining representatives from all areas of the system, the tactics council can
produce actions (changes in how the system operates) based on the information and metrics on OEE and
system performance. Helpful efficiency metrics and graphs are displayed in the
[flexibility analysis page](client-flexibility-analysis).

## Tactics Council

How can we produce actions from the various insights and information on OEE, machine utilization,
labor use, and system performance? Because improving a FMS requires coordination between various
stakeholders across several departments, a great management technique is a tactics council made up of a representative from
part programming, operations, scheduling, tooling, quality control, accounting, and anyone else
involved in the system. The council is the primary recipient of the system performance
metrics, meets once every 6 weeks or once every two months, reviews the data,
brainstorms improvements, and has final authority on the overall tactics for how the FMS operates.

Plan. Do. Check. Adjust. The [PDCA technique](https://en.wikipedia.org/wiki/PDCA) is an effective
strategy to improve the performance of an existing FMS and is ideal for the tactics council.
During the tactics council meeting, the council will perform the plan and
adjust phases of PDCA and in the 6-weeks to two months between meetings, individual departments will
perform the do and check phases of PDCA. The tactics council should focus on only a single or at
most two improvements at once.

For example, say that an improvement idea is to implement [material tracking](material-tracking) by adding a serial and barcode to each part during
machining and then scanning the barcode at the inspection station, allowing the inspection operator to
view the pallet, machine, and time for this specific part. The first step is to develop the plan,
which would include the cost of purchasing a barcode printer, an analysis of the extra time at the
load station applying the barcode to the part, a target for reducing the percentage of parts that
require inspection, the cost savings from the reduced inspections, and other facets such as
training. This plan is presented to the tactics council, the council discusses it, and say the
tactics council agrees to implement this change. In the 6-weeks to two months between council
meetings, operations will purchase the barcode printer, start barcoding parts, and start collecting
data (the do and check steps of PDCA). The inspection stand will start using the barcodes and
importantly record data on quality improvements. The next tactics council meeting can then review
the data to understand the impact on overall cost-per-piece and system performance. By reviewing
these metrics, the council can decide on any adjustments if necessary.

The tactics council is an important management technique because it encourages buy-in from everyone
to improve overall cost-per-piece and system performance instead of focusing on individual isolated
metrics such as cycle times or inspection rates. Typical improvements are similar to the previous
example, where one department (such as operations) has extra work and a different department (such
as quality control) sees an improvement. The tactics council and the repetitive nature of the PDCA
technique keeps everyone happy, because the next improvement implemented by the council might
improve operations while the inspection stand might have some extra work. Since everyone has a
representative on the tactics council and the tactics council focuses on part cost-per-piece and
total system performance, one department refusing to implement a change is greatly reduced.

## Continuous Improvement

The utilization and bottleneck metrics which lead to actionable insights vary
by system and part mix. The overall system OEE is easy to calculate by taking
the total quantity of parts produced in a month, adding up their planned
time, and dividing by the hours in a month. But the OEE is not an actionable
insight: is the OEE lowered by a pallet traffic jam, a problem in a
part-program, tooling capacity limitations, cart contention, load station
inattention, or something else? This is where the repetitive nature of the
PDCA technique shines. Every two months, the tactics council meets, reviews
the available data, and can suggest new metrics based on what was learned
from the previous reports. For example, perhaps the overview of the system
identifies that pallet 5 has very irregular and erratic flow times. This
could be caused by competition between pallets, lack of flexibility, traffic
jams, a problem in the part-program of a part run on pallet 5, lack of tool
capacity, or something else. In this case, more detailed data collection can
focus on pallet 5 to help identify the problem.

## Cost report

Calculating cost-per-piece requires splitting large fixed costs such as machine depreciation and
labor across the parts that were produced. To do so, we suggest a monthly analysis window and
dividing costs by planned use of resources. Collect data on the total quantity of parts produced
during a month, along with their planned use of each resource such as machining time, load station
time, tooling use, and inspection time and rates. The total cost is then divided among the parts
according to weights based on their planned use of system resources. The cost-per-part can then be
exported to the ERP and combined with order data from the ERP to calculate a cost-per-order.

The cost-per-piece is the primary metric and insight produced by the tactics council. It should be
broadcast to the entire business as an actionable insight for quoting work, future capital investment,
and general accounting. Cost-per-piece should be produced by the tactics council itself and not
management because the council is in the best position to decide on cost trade-offs.
For example, there is a trade-off between quality and inspections and cost, and the council is
in the best position to decide on the required quality and inspections and then work to reduce costs
only under these constraints without sacrificing quality.
