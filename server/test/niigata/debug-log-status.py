import fileinput
import json

def printRouteStep(pal, i, route):
  before = ""
  if i + 1 == pal["Tracking"]["CurrentStepNum"] and pal["Tracking"]["BeforeCurrentStep"]:
    before = "*"
  after = ""
  if i + 1 == pal["Tracking"]["CurrentStepNum"] and (not pal["Tracking"]["BeforeCurrentStep"]):
    after = "*"
  if route:
    if route["$type"] == "LoadStep":
      return f'{before}LD[{",".join(map(str, route["LoadStations"]))}]{after}'
    if route["$type"] == "UnloadStep":
      return f'{before}UL[{",".join(map(str, route["UnloadStations"]))}]{after}'
    elif route["$type"] == "MachiningStep":
      return f'{before}MC[{",".join(map(str, route["Machines"]))}][{",".join(map(str, route["ProgramNumsToRun"]))}]{after}'
  return str(i)

def printCellState(timestamp, pals):
  print(timestamp)
  for p in sorted(pals["Status"]["Pallets"], key=lambda x: x["Master"]["PalletNum"]):
    msg = f'Pal {p["Master"]["PalletNum"]} - {p["CurStation"]["Location"]["Location"]} {p["CurStation"]["Location"]["Num"]}'
    msg += f' [cycles {p["Master"]["RemainingPalletCycles"]}, pri {p["Master"]["Priority"]}, nowork {p["Master"]["NoWork"]}, skip {p["Master"]["Skip"]}] '
    msg += " -> ".join([printRouteStep(p, i, step) for i, step in enumerate(p["Master"]["Routes"])])
    executed = p["Tracking"]["ExecutedStationNumber"]
    if (len(executed) > 0):
      msg += " to " + ",".join(map(str, executed))
    print(msg)
  for num in sorted(pals["Status"]["Machines"]):
    m = pals["Status"]["Machines"][num]
    if m["Machining"]:
      print(f'Mach {num} {m["CurrentlyExecutingProgram"]}')
    else:
      print(f'Mach {num} off')

  print()

for line in fileinput.input():
  msg = json.loads(line)
  if msg["@mt"] == "Computed cell state {@pals}":
    printCellState(msg["@t"], msg["pals"])
