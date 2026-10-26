# Current-status alarms

Current status is the backend's latest knowledge, including retained observations when an update
fails. Clients display those facts and the backend's alarms. They do not infer validity from the age
of the snapshot, diagnostic wording, or which intermediate states they witnessed.

`Alarms` remains the compatible list of human-readable messages. Optional `AlarmDetails` adds a
stable code, the same message, explicit impact, and optional activity and basket filters. Use
`AddAlarm` to produce both representations from one diagnostic. Unstructured equipment messages
remain visible and do not implicitly disable anything.

`Informational` only displays the diagnostic. `SuspendChanges` marks affected instructions
unavailable and disables changes that depend on that status. Known material, jobs, observations, and
saved targets remain visible. Protective actions such as withholding automation remain available. A
later accepted snapshot without the blocking alarm restores availability; no client history or
manual acknowledgment of the alarm is needed.

Scope filters intersect:

| Activity          | Basket  | Scope                   |
| ----------------- | ------- | ----------------------- |
| omitted           | omitted | Whole status            |
| BasketLoadStation | omitted | All basket station work |
| omitted           | 7       | All work on basket 7    |
| BasketLoadStation | 7       | Basket 7 station work   |

`BasketMovement` distinguishes movement instructions from station work. Add other activities or
resource filters when a consumer needs them. An alarm scoped to every action on a basket also
suspends its correction work. The backend should choose such a scope only when that is intended.
Ordinary holds, waiting for material, and completion readiness keep their existing domain rules;
they need not become alarms. In particular, loss of robot sensing need not suspend valid saved
station work or unrelated manual production.

Alarm impact is a presentation contract, not authorization. On submission the backend must still
validate current eligibility and the opaque work identity, even when a client displayed a usable
instruction. A stale WorkId cannot confirm replacement work. Revisions order delivered snapshots;
they do not certify a new equipment observation or completion of an outstanding request.

The shared basket station uses the common `alarmsBlockingChanges` rule to disable Confirm and label
retained targets. Integrations should use that same rule for their own instruction and control
surfaces. Each surface supplies its activity and resource context; it does not reinterpret
diagnostic codes or invent a second model of backend readiness.

Complete snapshots returned by custom actions use the same revision/session acceptance as status
GETs. If a reconnect or intervening local edit prevents installation, the shared loader obtains a
replacement read with the existing timeout and retry backoff. The action remains acknowledged and
recovery survives navigation. A queued socket frame cannot satisfy that particular read requirement;
a later successful read, including connection bootstrap, can. A read already pending when the
requirement arose cannot clear it. Recovery never resubmits the action.
