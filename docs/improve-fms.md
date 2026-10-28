---
title: Use analytics to improve an FMS
nav: Procedures > Monthly Review
description: >-
  Review production, resource use, quality, and cost together to choose and evaluate improvements to
  a flexible machining system.
---

A flexible machining system depends on scheduling, programming, operators, tooling, quality, and
material handling working together. Improving one operation in isolation can shift the problem
elsewhere. A regular review of the whole cell helps choose changes that improve accepted output and
cost per piece.

FMS Insight's [Analysis pages](/fms-insight/client-efficiency) provide production, cycle, flow,
inspection, and cost-allocation reports over a month or the last 30 days. They complement the recent
reports used for [daily operations](/fms-insight/client-operations).

## Actionable Insights

Start with a question that can lead to a decision. For example: why are machines waiting while
material accumulates at the load station? Is a recurring interruption tied to a program? Did a
change in route flexibility make production more consistent?

Use the overview reports to locate the pattern, then inspect the relevant cycles and material
histories. Distinguish elapsed time from expected active time, recorded production from simulated
production, and completed processes from accepted finished parts. Missing data or a limited time
window can change the interpretation.

Cost per piece helps assess the overall result, but it rarely identifies the cause of a problem.
Resource use, buffer occupancy, and cycle histories are more useful for deciding where to
investigate. Review quality alongside throughput so a faster process is not mistaken for an
improvement when it produces more rejected material.

## Tactics Council

Bring together representatives from operations, scheduling, programming, tooling, quality, and the
other teams involved in the cell. This group, or tactics council, should have authority to agree on
changes that affect more than one department.

Meet at a regular interval, such as monthly or every six weeks. Focus on one or two improvements at
a time. Keep a record of the question, the proposed change, the expected effect, and what evidence
will show whether it worked.

## Continuous Improvement

Use a plan, do, check, adjust cycle:

1. **Plan:** review the evidence, identify a likely cause, and agree on a change. Consider the
   effects on material flow, operators, tooling, inspections, and scheduling. Use simulation where
   it can test those interactions.
2. **Do:** introduce the reviewed change and ensure the people affected understand it.
3. **Check:** compare a suitable period before and after the change. Account for changes in part
   mix, available hours, and data coverage. Include quality outcomes.
4. **Adjust:** keep, revise, or reverse the change based on the evidence, then select the next
   improvement.

For example, unusually long pallet cycles may justify investigating one pallet's station history.
The cause might be machine interruptions, a transfer bottleneck, or competition with other pallets.
The cycle chart narrows the investigation; it does not decide which intervention is appropriate.

## Cost Report

The [cost reports](/fms-insight/client-efficiency#costpiece) allocate resource costs using the
cell's recorded production and resource-use weights. This spreads the cost of both productive and
idle capacity across the part mix.

Use a consistent method and comparable periods when judging an improvement. Export the allocation
percentages to a spreadsheet or ERP if you need to combine them with material, overhead, or other
costs outside Insight's view. Share the results with the teams involved so the effects of changes
are visible across the business.
