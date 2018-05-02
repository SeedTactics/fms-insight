#!/usr/bin/python3
import subprocess
import sys

if len(sys.argv) < 2:
    print("Must specify tag prefix")
    sys.exit(1)

incr_last = not ("--no-increment" in sys.argv)

curtag = subprocess.check_output(["hg", "id", "-t", "-r", ".^"]).decode("utf-8").rstrip()
prefix = sys.argv[1]

if curtag.startswith(prefix):
    print(curtag.replace(prefix + "-", ""))
else:
    tag = subprocess.check_output(["hg", "id", "-t", "-r", "ancestors(.) and tag('re:" + prefix + "')"]).decode("utf-8").rstrip()
    parts = tag.replace(prefix + "-", "").split(".")
    if incr_last:
        print(parts[0] + "." + parts[1] + "." + str(int(parts[2]) + 1))
    else:
        print(parts[0] + "." + parts[1] + "." + parts[2])
