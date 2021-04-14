#!/usr/bin/python3

from datetime import date, datetime, timedelta, timezone
import json
import os
import urllib.request
import urllib.parse

host = "http://localhost:5000"

# ------------------------------------------------------
#  Calculate shift starts
# ------------------------------------------------------

today = date.today()
yesterday = today - timedelta(days = 1)

# Set shift 1 start as yesterday 6am
shift1Start = datetime(yesterday.year, yesterday.month, yesterday.day, 6, 0, 0).astimezone(tz=None) # tz=None means local timezone

# Set shift 2 start as yesterday at 2pm (14 hours)
shift2Start = datetime(yesterday.year, yesterday.month, yesterday.day, 14, 0, 0).astimezone(tz=None)

# Set shift 3 start as yesterday at 10pm (22 hours)
shift3Start = datetime(yesterday.year, yesterday.month, yesterday.day, 22, 0, 0).astimezone(tz=None)

# Set shift 3 end as today at 6am
shift3End = datetime(today.year, today.month, today.day, 6, 0, 0).astimezone(tz=None)

# ------------------------------------------------------
#  Load the events
# ------------------------------------------------------

query = urllib.parse.urlencode({
  # Load all events from shift1Start to shift3End (converted to UTC)
  "startUTC": shift1Start.astimezone(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
  "endUTC": shift3End.astimezone(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
})
req = urllib.request.Request("%s/api/v1/log/events/all?%s" % (host, query))
req.add_header("Accept", "application/json")
with urllib.request.urlopen(req) as r:
    events = json.loads(r.read())

# -------------------------------------------------------------------
# Caluculate completed on each shift
# -------------------------------------------------------------------

shift1 = {}
shift2 = {}
shift3 = {}

for event in events:
  if event["type"] == "LoadUnloadCycle" and event["program"] == "UNLOAD" and not event["startofcycle"]:

    # Part and completed count
    part = event["material"][0]["part"] + "-" + str(event["material"][0]["proc"])
    completed = len(event["material"])

    # Load time in UTC
    if "." in event["endUTC"]:
      evtTime = datetime.strptime(event["endUTC"], "%Y-%m-%dT%H:%M:%S.%fZ").astimezone(tz=timezone.utc)
    else:
      evtTime = datetime.strptime(event["endUTC"], "%Y-%m-%dT%H:%M:%SZ").astimezone(tz=timezone.utc)
    # Convert to local
    evtTime = evtTime.astimezone(tz=None)

    if evtTime < shift2Start:

      # Add to shift1 completed
      if part in shift1:
        shift1[part] += completed
      else:
        shift1[part] = completed

    elif evtTime < shift3Start:

      # Add to shift2 completed
      if part in shift2:
        shift2[part] += completed
      else:
        shift2[part] = completed

    else:

      # Add to shift3
      if part in shift3:
        shift3[part] += completed
      else:
        shift3[part] = completed

print("Shift 1")
print(shift1)
print("-----------------")
print("Shift 2")
print(shift2)
print("-----------------")
print("Shift 3")
print(shift3)