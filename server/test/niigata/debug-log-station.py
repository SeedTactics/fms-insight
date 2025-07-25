import fileinput
import datetime
import json


def tolocal(str):
    d = datetime.datetime.fromisoformat(str[0:19] + "+00:00")
    return d.astimezone(tz=None)


def printCellState(timestamp, st):
    d = tolocal(timestamp)
    print(d.ctime() + "  (" + timestamp + ")")

    found = False
    for palKv in st["CurrentStatus"]["Pallets"]:
        pal = palKv["Value"]
        if pal["CurrentPalletLocation"]["StationGroup"] == "L/U":
            found = True
            print(
                f'Pal {pal["PalletNum"]} - Load Station {pal["CurrentPalletLocation"]["Num"]}'
            )

    if found:
        for mat in st["CurrentStatus"]["Material"]:
            if mat["Location"]["Type"] == "OnPallet":
                print(
                    f'Mat {mat["PartName"]} - Pal{mat["Location"]["PalletNum"]} - Serial {mat["Serial"]} - {mat["Action"]["Type"]}'
                )
            elif mat["Location"]["Type"] == "InQueue":
                print(
                    f'Mat {mat["PartName"]} - Queue {mat["Location"]["CurrentQueue"]} - Serial {mat["Serial"]} - {mat["Action"]["Type"]}'
                )

    print()


for line in fileinput.input():
    msg = json.loads(line)
    if msg["@mt"] == "Periodic cell state: {@state}":
        printCellState(msg["@t"], msg["state"])
