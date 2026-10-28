---
title: Barcode Scanners
nav: FMS Insight Client > Scanners
description: >-
  All FMS Insight pages support barcode or QR code scanners for easy material tracking on the shop
  floor.
---

Scan a part's serial to open its material details, event history, and the actions available on the
current page. This opens the same dialog as selecting the material card. For example, scanning at an
[inspection station](/fms-insight/client-station-monitor#inspection) opens the inspection actions
for that piece.

## Manual Serial Entry

Select the magnifying glass in the top-right of the page, then enter a serial to open its material
details.

## Configuring a Handheld Scanner

Most USB and Bluetooth scanners act as keyboards. Configure the scanner to send `F1` or `!*` before
the serial and `Enter` afterward. Insight must receive the serial and `Enter` within three seconds
of the prefix.

By default, Insight uses the text before the first comma as the serial. An installation's
integration can interpret additional barcode data.

To test without a scanner, open an Insight page, press `F1`, type a valid serial, and press `Enter`
within three seconds.

## Camera Scanning

When Insight is served over HTTPS, the toolbar also offers a camera scanner. Allow camera access in
the browser, then use it to scan a supported code. A handheld scanner or manual serial entry remains
useful on devices without a camera.
