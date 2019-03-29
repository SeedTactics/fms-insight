---
id: client-engineering
title: Engineering Daily Monitoring
sidebar_label: Engineering
---

The _Engineering_ page is intended for the part programming engineers. We suggest they bookmark this page and
visit it directly. The page can be used after a new part-program is introduced to closly watch the timings of
the new program. The page can also be used to stay on top of problematic programs which are frequently interrupted.

First, the page shows cycles from the last three days which are statistical
outliers. The outlier detection is based on the [median absolute deviation of
the median](https://en.wikipedia.org/wiki/Median_absolute_deviation), which
is more resiliant to outliers than the standard deviation.

Next, the planned and actual machine hours for the past 7 days are shown. The data can be toggled between a bar chart and
a table.

Finally, the page contains a chart of all machine cycles. The chart can
be zoomed by clicking and dragging, filtered to specific parts and/or
pallets, and toggled to a table. Clicking on any point allows more details
about the cycle to be loaded.
