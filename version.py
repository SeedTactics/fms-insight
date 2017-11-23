#!/usr/bin/python3
import subprocess
import sys

if len(sys.argv) < 2:
    print("Must specify mwi, framework, or service")
    sys.exit(1)

curtag = subprocess.check_output(["hg", "id", "-t", "-r", ".^"]).decode("utf-8")

def calcver(prefix):
    if curtag.startswith(prefix):
        return curtag.replace(prefix + "-", "")
    else:
        tag = subprocess.check_output(["hg", "id", "-t", "-r", "ancestors(.) and tag('re:" + prefix + "')"]).decode("utf-8")
        parts = tag.replace(prefix + "-", "").split(".")
        return parts[0] + "." + parts[1] + "." + str(int(parts[2]) + 1)

if sys.argv[1] == "mwi":
    print(calcver("mwi"))
elif sys.argv[1] == "framework":
    print(calcver("framework"))
elif sys.argv[1] == "service":
    print(calcver("service"))