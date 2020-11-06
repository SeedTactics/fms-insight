import fileinput
import datetime
import json

# { "TimeUTC":"2020-11-02T05:59:46.0000000Z",
#   "Code":"PalletMoveComplete",
#   "ForeignID":"LG20201101-235946-001.csv",
#   "Pallet":25,
#   "FullPartName":"",
#   "JobPartName":"",
#   "Process":0,
#   "FixedQuantity":0,
#   "Program":"",
#   "StationNumber":0,
#   "TargetPosition":"LS02",
#   "FromPosition":"S025",
#   "$type":"LogEntry"
# }

def printEvent(timestamp, e):
  d = datetime.datetime.fromisoformat(timestamp[0:19] + "+00:00")
  d = d.astimezone(tz=None)
  print(d.ctime() + "  (" + timestamp + ")")
  print(e["Code"])
  msg = f'  Pal = {e["Pallet"]}, FullPart = {e["FullPartName"]}, JobPart = {e["JobPartName"]}, Proc = {e["Process"]}, FixQty = {e["FixedQuantity"]}, '
  msg += f'Program = {e["Program"]}, StatNum = {e["StationNumber"]}, TargetPos = {e["TargetPosition"]}, FromPos = {e["FromPosition"]}'
  print(msg)
  print()

lastPalMove = None
for line in fileinput.input():
  msg = json.loads(line)
  if msg["@mt"] == "Handling mazak event {@event}":
    e = msg["event"]
    if e["Code"] == "PalletMoveComplete" and lastPalMove == e["Pallet"]:
      # don't display, is a duplicate message
      pass
    elif e["Code"] == "PalletMoveComplete":
      lastPalMove = e["Pallet"]
      printEvent(msg["@t"], e)
    else:
      lastPalMove = None
      printEvent(msg["@t"], e)