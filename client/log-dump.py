#!/usr/bin/python3

import datetime
import json
import os
import urllib.request
import urllib.parse

host = "http://localhost:5000"

# Each event has an int64 counter that is guaranteed to be unique and increasing.
# Load the last seen event counter from the file names in the current directory.
# If the processed data is instead in a database, can load the last seen counter from there instead.
lastSeenCntr = None
for f in os.listdir("."):
    if os.path.isfile(f):
        cntr = int(os.path.splitext(os.path.basename(f))[0])
        if not lastSeenCntr or cntr > lastSeenCntr:
            lastSeenCntr = cntr

if lastSeenCntr:
    # load recent events (if any)
    with urllib.request.urlopen(
        "%s/api/v1/log/events/recent?lastSeenCounter=%i" % (host, lastSeenCntr)
    ) as r:
        events = json.loads(r.read())
else:
    # Load the last 2 days of events
    now = datetime.datetime.utcnow()
    start = now - datetime.timedelta(days=2)

    # The start and end UTC times are in ISO 8601 format.
    query = urllib.parse.urlencode(
        {
            "startUTC": start.strftime("%Y-%m-%dT%H:%M:%SZ"),
            "endUTC": now.strftime("%Y-%m-%dT%H:%M:%SZ"),
        }
    )
    req = urllib.request.Request("%s/api/v1/log/events/all?%s" % (host, query))
    req.add_header("Accept", "application/json")
    with urllib.request.urlopen(req) as r:
        events = json.loads(r.read())

# Write the events as files, one event per file.  The name of the file is the event counter so
# that the lastSeenCounter can easily be computed from the filenames.
for event in events:
    # Only look at the machine cycle end events:
    # The loc and locnum fields contain the specific machine.
    # The elapsed field contains the wall clock time from machine start to end in ISO 8601 duration format.
    # The active field contains the target time from the flexplan file in ISO 8601 duration format.
    # The material field contains the list of material. Each material has a unique int64 id along with
    #   the serial, partname, process number, and other fields.
    if event["type"] == "MachineCycle" and not event["startofcycle"]:
        with open(str(event["counter"]) + ".json", "w") as f:
            json.dump(event, f)
