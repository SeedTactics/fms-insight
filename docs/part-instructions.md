---
title: Operator Instructions
nav: FMS Insight Server > Instructions
description: >-
  Make part-specific load, unload, closeout, and inspection documents available from the station
  pages.
---

Insight can open instructions for the material and operation currently selected on a
[station page](/fms-insight/client-station-monitor). These are typically PDF documents with the
site's approved pictures and procedures. They open in a separate browser tab that is reused for
subsequent requests.

## Configuration

For the standard file-based lookup, set `InstructionFilePath` in the `FMS` section of
[config.ini](/fms-insight/server-config) to a directory on the Insight server. Place
browser-readable files in that directory and restart the server after changing the setting. An
installation can also supply its own instruction lookup through a plugin.

## Instruction Types

Include the part name and instruction type in each filename. For a part named `ABC123`:

| Operation  | Example filename    | Notes                                                                                              |
| ---------- | ------------------- | -------------------------------------------------------------------------------------------------- |
| Loading    | `ABC123-load.pdf`   | Can include both loading and unloading instructions.                                               |
| Unloading  | `ABC123-unload.pdf` | If no unload document is found, Insight falls back to the load document.                           |
| Closeout   | `ABC123-wash.pdf`   | The Close Out page uses the `wash` instruction type, including for final tasks other than washing. |
| Inspection | `ABC123-CMM.pdf`    | Use the configured inspection type, such as `CMM`, in the filename.                                |

When a process number is available, Insight first searches for the part name followed by that
process, for example `ABC123-2-load.pdf`. If no match is found, it searches again without the
process number.

The standard lookup uses filename substring matching; part-name matching is case-sensitive and
instruction-type matching is case-insensitive. Keep names unambiguous and verify the document opened
for each part and process, especially when part names overlap. Do not rely on a particular choice
between multiple matches.

## Opening the Instructions

Open a material card and select **Instructions**. The station, selected inspection type, part, and
process determine the requested document. At a load station, the folder button opens instructions
for the displayed load/unload work.

If no document is found, check the configured directory, filename, part name, process, and requested
instruction type. The [server error log](/fms-insight/server-errors) can help diagnose configuration
problems.
