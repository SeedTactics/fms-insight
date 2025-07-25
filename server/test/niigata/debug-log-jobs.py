import fileinput
import datetime
import json


def tolocal(str):
    d = datetime.datetime.fromisoformat(str[0:19] + "+00:00")
    return d.astimezone(tz=None)


def printCellState(timestamp, st):
    d = tolocal(timestamp)
    print(d.ctime() + "  (" + timestamp + ")")
    for jkv in sorted(
        st["CurrentStatus"]["Jobs"], key=lambda x: x["Value"]["RouteStartUTC"]
    ):
        j = jkv["Value"]
        print(
            f'{j["PartName"]} - Cycles {j["RemainingToStart"]}/{j["Cycles"]}, Start {tolocal(j["RouteStartUTC"]).ctime()}, Uniq {jkv["Key"]}'
        )
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


def addNewJobs(timestamp, jobs):
    d = datetime.datetime.fromisoformat(timestamp[0:19] + "+00:00")
    d = d.astimezone(tz=None)
    print(d.ctime() + "  (" + timestamp + ")")
    for j in jobs["Jobs"]:
        print(
            f'New job {j["PartName"]} - Cycles {j["Cycles"]}, Start {tolocal(j["RouteStartUTC"]).ctime()}, Uniq {j["UniqueStr"]}'
        )
    print()


for line in fileinput.input():
    msg = json.loads(line)
    if msg["@mt"] == "Periodic cell state: {@state}":
        printCellState(msg["@t"], msg["state"])
    elif msg["@mt"] == "Adding new jobs {@jobs}":
        addNewJobs(msg["@t"], msg["jobs"])
