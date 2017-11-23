#!/usr/bin/python3
import subprocess
import sys

if len(sys.argv) < 2:
    print("Must specify tag prefix")
    sys.exit(1)

curtag = subprocess.check_output(["hg", "id", "-t", "-r", ".^"]).decode("utf-8")
prefix = sys.argv[1]

if curtag.startswith(prefix):
    print(curtag.replace(prefix + "-", ""))
else:
    tag = subprocess.check_output(["hg", "id", "-t", "-r", "ancestors(.) and tag('re:" + prefix + "')"]).decode("utf-8")
    parts = tag.replace(prefix + "-", "").split(".")
    print(parts[0] + "." + parts[1] + "." + str(int(parts[2]) + 1))