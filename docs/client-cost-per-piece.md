---
id: client-cost-per-piece
title: Part Cost Per Piece
sidebar_label: Cost/Piece
---

The cost/piece tab shows some calculations for dividing the labor and
machining costs among the parts, weighted by each parts relative use of the
resources. This calculation is largely intended to serve as a verification
that flexibility and operational changes are reducing part costs; you can
compare part costs from before a change to after a change to understand
if the change improved the system. At the top of the tab, you can choose to
view either the data from the past 30 days or view data for a specific month.

Indeed, FMS Insight has a narrow view of just the cell itself and does not
take into account any overhead or other costs. Therefore, for budgeting,
quoting work, and other management decisions, we suggest that you [export the
workorder data](workorder-report.md) to your ERP and implement a cost report
in your ERP taking into account all factors.

# Cost Inputs

In the top card labeled "Cost Inputs", enter the number of operators assigned
to the cell and their total hourly rate.  FMS Insight will multiply the number
of operators times the cost per hour times the total month time to obtain a
total cost of labor for the cell.  Next, enter the cost per year of each individual
station; that is, in the machine row, enter the cost of a single machine.  FMS Insight
will multiply this cost by the number of machines and divide it by 12 to obtain a total
cost of the station.

# Part Cost/Piece

![Screenshot of part cost/piece page](assets/insight-part-cost.jpg)

The card labeled "Part Cost/Piece" then shows the results of the cost
calculations. FMS Insight takes the total labor and station costs calculated
for the whole month and then divides it among the parts weighted by their use
of the labor or station. For example, when dividing up machining cost, FMS
Insight will sum up the expected machine cycle time for all part cycles that
were produced during the month, and then sum up the expected machine cycle
time for part `aaa`. Dividing these two quantities, FMS Insight obtains a
percentage use of the machine for part `aaa`. FMS Insight takes the total
machine cost (number of machines times machine yearly cost divided by 12) and
multiplies it by the use percentage. This produces the machining cost of the
`aaa` part for the month, and finally this cost is divided by the number of
`aaa` parts produced to obtain a cost/piece. This calculation is repeated for
each station and for the labor use.
