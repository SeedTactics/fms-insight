# Historical analysis baseline

`InsightCliBaselineSpec` builds this small history through repository operations, serves the real
HTTP API and exercises the CLI. Run it with
`dotnet run --project server/test -- --treenode-filter '/*/*/InsightCliBaselineSpec/*'`. It checks
transport/projection arithmetic. The questions and expected answers below are a review baseline, not
evidence that an independent agent or an engineer has already reviewed the analysis.

The initial window is 2026-10-08 through 2026-10-09 UTC. One machining cycle contains two materials,
lasts 180 seconds and has 120 seconds of approved active time. A different 240-second cycle is
invalidated by a later action outside the window. A 90-second cycle precedes the window. Two
materials have the same serial. One receives a failed inspection two days after the window ends; the
other has no recorded outcome. Planning contains a shared ten-minute station interval and a
five-minute downtime interval. A ten-cycle job has an initial decrement of two and a later decrement
of three.

Questions for an independent analyst:

1. What is valid completed machining elapsed time inside the window? **180 seconds.** Joining
   material rows first gives 360 seconds, which counts the shared cycle twice.
2. What is the active-time target? **120 seconds after invalidation.** This is an approved planning
   target, not measured spindle time or a statistical estimate.
3. Was all measured time erased by invalidation? **No.** Completed machining rows still record **420
   seconds** of elapsed time; the invalidated 240 seconds remain recorded cost/time evidence.
4. How much simulated station use is present? **900 seconds**, including **300 seconds** of planned
   downtime. The shared 600-second interval is one interval despite its two job participants.
5. How much planned quantity was decremented? **Five**, leaving five of the original ten planned
   cycles before considering any separate production question. Job-read/event overlap does not count
   a decrement twice; neither number establishes accepted good output.
6. Does the window establish a complete lifecycle or a passing quality result? **No.** An operation
   precedes it, an inspection follows it, and absent outcomes remain unknown.
7. Does the duplicate serial identify one piece? **No.** Targeted lookup returns two material IDs.
   Only one has a recorded failed inspection; a serial-only join cannot attribute it to both.
8. Does targeted retrieval change global event progress? **No.** It records separate material
   coverage and exposes the pre-window operation and delayed inspection without moving that cursor.

Keep an independent analyst's SQL, outputs, coverage and CLI version with a closed database when
using this history to qualify agent behavior. A real recorded-history investigation is a separate
qualification step.
