---
title: Barcode Scanners
nav: FMS Insight Client > Scanners
description: >-
  All FMS Insight pages supports barcode or QR-code scanners for easy material tracking
  on the shop floor.
---

# FMS Insight Barcode Scanners

All FMS Insight pages supports barcode or QR-code scanners.
When a part's serial is scanned, the dialog corresponding to the piece of material is opened.
For example, if the [inspection station monitor](client-station-monitor) is open and a part's serial is scanned,
the dialog for this serial will be opened to show the material details, log of events, and buttons to
complete the inspection. (The exact same dialog which is opened if you click on a piece of material on the screen.)
In this way, the scanner can be used to more quickly locate the material card and open the details about it.

## Manual Serial Entry

A serial can be entered via the keyboard by clicking on the magnifying glass icon in the top-right of the page.
A dialog will open allowing you to enter a serial and then the material details for that serial will be loaded.

## Configuring a handheld scanner

Handheld USB or bluetooth scanners are presented to the system as keyboards.
When a barcode or QR-code is scanned, the contents of the barcode is sent as
keyboard presses. To support this, the FMS Insight website listens for a
keyboard press of either F1 or the combination of !\* and when it detects this,
starts a scan. Any keyboard presses of letters and numbers followed by the
Enter key (within 10 seconds) is treated as a serial. By default, only
characters up to the first comma or semi-colon are used, which allows the
QR-code to contain extra data (this can be modified in a custom plugin).

You can try this manually without a scanner; on a webpage, press F1, quickly type
a serial, and press Enter (must be within 10 seconds). The material detail dialog
will appear with the serial that you typed.

Almost all handheld USB or bluetooth scanners can be configured to send a prefix key
before the scan and a postfix key after the scan. For the scanner to work with
FMS Insight, the scanner should be configured to use F1 or !\* as the prefix key and Enter
as the postfix key.
