---
id: client-scanners
title: Scanners
sidebar_label: Scanners
---

All FMS Insight pages supports barcode or QR-code scanners.
When a part's serial is scanned, the dialog corresponding to the piece of material is opened.
For example, if the [inspection station monitor](client-station-monitor.md) is open and a part's serial is scanned,
the dialog for this serial will be opened to show the material details, log of events, and buttons to
complete the inspection. (The exact same dialog which is opened if you click on a piece of material on the screen.)
In this way, the scanner can be used to more quickly locate the material card and open the details about it.

## Configuring a handheld scanner

Handheld USB or bluetooth scanners are presented to the system as keyboards.
When a barcode or QR-code is scanned, the contents of the barcode is sent as
keyboard presses. To support this, the FMS Insight website listens for a
keyboard press of F1, keyboard presses of letters and numbers, followed by
the Enter key (within 10 seconds). When the Enter key is detected, the
preceding key presses are taken as the serial. Only the key presses up to
the first comma are used, which allows the QR-code to contain extra data (for
example, the QR-code might contain the serial, then a comma, then the part
name, then a comma, and even more data).

You can try this manually without a scanner; on a webpage, press F1, quickly type
a serial, and press Enter (must be within 10 seconds). The material detail dialog
will appear with the serial that you typed.

Almost all handheld USB or bluetooth scanners can be configured to send a prefix key
before the scan and a postfix key after the scan. For the scanner to work with
FMS Insight, the scanner should be configured to use F1 as the prefix key and Enter
as the postfix key.

## Using the camera to scan QR-codes

On devices with attached cameras such as tablets or phones, the camera can be used to
scan a QR-code. Similar to above, the contents of the QR-code up to the first comma
are used as the serial. To scan a QR-code, there is a camera button on the
station monitor toolbar. Clicking this button will open a dialog showing the camera
and will close when a QR-code is detected.

![Screenshot of station monitor toolbar](/docs/assets/insight-station-monitor-toolbar.jpg)

Due to browser security restrictions, the camera is only available to
web pages when the location is `localhost` or secure https SSL is used. Thus if you
are going to use the camera to scan QR-codes, FMS Insight server must be configured to
use SSL. The [security docs](security.md) contains details on enabling secure
https. If SSL is not currently in use, the camera icon will not be shown.
