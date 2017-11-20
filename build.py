import subprocess
import re
import sys
import datetime

def freplace(filename, match, new):
    with open(filename) as f:
        ct = f.read()
    ct = re.sub(match, new, ct)
    with open(filename, "w") as f:
        f.write(ct)

def run(cmd, d):
    print(d + "$ " + cmd)
    subprocess.run(args=cmd.split(" "), cwd=d, check=True)

# Check if current rev is a tag
curtag = subprocess.check_output(["hg", "id", "-t", "-r", ".^"]).decode("utf-8")

# Build mwi
if curtag.startswith("mwi-"):
    ver = curtag.replace("mwi-", "")
    run("dotnet pack -c Release --include-symbols /p:VersionPrefix=" + ver,
        "lib/BlackMaple.MachineWatchInterface")
elif sys.argv[1] == "--alpha-mwi":
    tag = subprocess.check_output(["hg", "id", "-t", "-r", "ancestors(.) and tag('re:mwi')"]).decode("utf-8")
    parts = tag.replace("mwi-", "").split(".")
    ver = parts[0] + "." + parts[1] + "." + str(int(parts[2]) + 1)
    suffix = "alpha-" + datetime.datetime.utcnow().strftime("%Y%m%d%H%M%S")
    run("dotnet pack -c Release --include-symbols /p:VersionPrefix=" + ver +
           " -o ../../nugetpackages --version-suffix " + suffix,
        "lib/BlackMaple.MachineWatchInterface")
else:
    run("dotnet build", "lib/BlackMaple.MachineWatchInterface")

# Build framework
if curtag.startswith("framework-"):
    # Switch reference to use nuget seedorders
    mwiver = subprocess.check_output(["hg", "id", "-t", "-r", "ancestors(.) and tag('re:mwi')"]).decode("utf-8")
    mwiver = mwiver.replace("mwi-", "").split(".")[0]
    freplace("lib/BlackMaple.MachineFramework/BlackMaple.MachineFramework.csproj",
            r'<ProjectReference Include="../BlackMaple.MachineWatchInterface[^>]+>',
             '<PackageReference Include="BlackMaple.MachineWatchInterface" Version="' + mwiver + '.*"/>')
    freplace("test/test.csproj",
            r'<ProjectReference Include="../lib/BlackMaple.MachineWatchInterface[^>]+>',
             '<PackageReference Include="BlackMaple.MachineWatchInterface" Version="' + mwiver + '.*"/>')
    
    # Pack framework
    ver = curtag.replace("framework-", "")
    run("dotnet pack -c Release --include-symbols /p:VersionPrefix=" + ver,
        "lib/BlackMaple.MachineFramework")
else:
    run("dotnet build", "lib/BlackMaple.MachineFramework")

# Build service
if curtag.startswith("service-"):
    # Switch reference to use nuget mwi and framework
    mwiver = subprocess.check_output(["hg", "id", "-t", "-r", "ancestors(.) and tag('re:mwi')"]).decode("utf-8")
    mwiver = mwiver.replace("mwi-", "").split(".")[0]
    freplace("src/service/MachineWatch.csproj",
            r'<ProjectReference Include="../../lib/BlackMaple.MachineWatchInterface[^>]+>',
             '<PackageReference Include="BlackMaple.MachineWatchInterface" Version="' + mwiver + '.*"/>')

    frameworkver = subprocess.check_output(["hg", "id", "-t", "-r", "ancestors(.) and tag('re:framework')"]).decode("utf-8")
    frameworkver = mwiver.replace("framework-", "").split(".")[0]
    freplace("src/service/MachineWatch.csproj",
            r'<ProjectReference Include="../../lib/BlackMaple.MachineFramework[^>]+>',
             '<PackageReference Include="BlackMaple.MachineFramework" Version="' + frameworkver + '.*"/>')

    # Pack service
    ver = curtag.replace("service-", "")
    run("dotnet pack -c Release --include-symbols /p:VersionPrefix=" + ver,
        "src/service")
else:
    run("dotnet build", "src/service")

# Build tests
run("dotnet build", "test")