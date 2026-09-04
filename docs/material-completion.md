# Material completion

Finished material is completed when it exits controlled handling, not merely when it moves off a
pallet or finishes machining. New unload events carry a per-material `MaterialCompleted:<id>`
program-detail fact (`True` or `False`), recorded atomically with the manufacturing operation.

- Final-process pallet unload to a null destination dictionary value is terminal (a destination
  object with a null queue instead requires a basket transfer). Queue destinations and explicit
  pallet-to-basket transfers are internal. Material reloaded in the same transaction is internal.
- Basket-to-pallet transfers are internal, even for the final process before machining.
- Basket-station unloading to a queue or reloading onto a basket in the same operation is internal.
  A final-process unload without either onward destination is terminal.
- Nonfinal-process exits do not establish finished production.

The fact belongs to the event's material. Mixed-destination events can complete only some of their
material. Idempotent basket-operation retries return the original facts without reclassifying them
from later job/material details. Explicit material-ID corrections move the fact with the corrected
event material.

Completed-part history and workorder quantities use these facts. The client keeps terminal
completion time separate from per-process pallet-unload and machining history, and uses terminal
time for completed-parts and cost reporting. `CompletedUnloadsSince` remains a stream of completed
pallet-unload operations across all processes, not a finished-parts query.

For backward compatibility, unmarked historical pallet unloads retain the old final-process
heuristic. Unmarked basket events do not imply completion. There is no schema migration or
retroactive reconstruction: previously recorded internal basket returns may therefore retain their
legacy accounting. Existing event consumers that calculate finished quantities must use the new
facts rather than adding basket unloads to an event-type filter.
