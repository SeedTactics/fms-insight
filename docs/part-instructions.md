---
title: Operator Instructions
nav: FMS Insight Server > Instructions
description: >-
  The FMS Insight server can be configured to display arbitrary load, unload, wash
  and inspection instructions to the operator.
---

# FMS Insight Operator Instructions

FMS Insight can display load, wash, and inspection instructions to the operator.
The instructions are documents (typically PDF files) stored on the FMS Insight server,
and when the user clicks a button in the client the instructions are shown in a new
browser tab.

### Configuration

The instruction files are kept on the FMS Insight server in a single directory. The
directory is configured via a [configuration option](server-config) `InstructionFilePath`
within the `FMS` section. Once configured, this directory should be filled with files
that can be displayed in a browser (PDF is a good choice). When the user requests an
instruction file, the FMS Insight server will search the filename of all files inside this
directory

### Instruction Types

There are four instruction types: load, unload, wash, and inspections. Each instruction type will
search for a different file depending on the page on the
[station monitor screen](client-station-monitor) and the currently selected part. To create
instruction files for all possibilities, you should create files as follows:

- For each part, create a file with the partname and `load` in the filename. Inside this
  file, add pictures and text to explain how to load and optionally unload the part from the pallet.
  For example, for a part named `ABC123`, you could create a file `ABC123-load.pdf` which contains
  the instructions. When the user is on the Load Station page in FMS Insight, FMS Insight
  will search for a file with the partname and `load` in the filename, find `ABC123-load.pdf`,
  and display it to the user.

- For each part, optionally create a file with the partname and `unload` in the filename. If no
  file exists with the partname and `unload` in the filename, FMS Insight will fall back to searching
  for a file with the partname and `load` in the filename. Thus you could just create a single file
  (e.g. `ABC123-load.pdf`) which contains both the load and unload instructions, or you could create
  two separate files (e.g. `ABC123-load.pdf` and `ABC123-unload.pdf`).

- For each part, create a file with the partname and `wash` in the filename. Inside this
  file, add pictures and text to explain how to wash and perhaps other tasks at the wash station.
  For example, for a part named `ABC123`, you could create a file `ABC123-wash.pdf`.
  Similar to before, when the user is on the Wash page in FMS Insight, this instruction file
  will be displayed.

- For each part and each inspection type, create a file with the partname and the inspection type
  in the filename. For example, for a part named `ABC123` and inspection types `CMM` and `3DScan`,
  you should create two files: `ABC123-CMM.pdf` and `ABC123-3DScan.pdf`. Each file should contain
  pictures and text to explain how to perform the inspection on this part. When the user is on
  the Inspection page, FMS Insight will find the file based on the currently selected part and
  inspection type.

### Opening the instructions

To open the instructions, click on any material card in the [station monitor page](client-station-monitor). When clicked, a dialog will appear with details about the
material. On the bottom of the dialog, there is a button "Instructions". When clicked,
FMS Insight will search for a file matching the current partname and page (load, wash, or inspection) as described above. The instruction file will then be displayed in a new tab. When subsequent instructions are requested, the tab will be re-used. Thus the tab does not need to be
manually closed after each operation.
