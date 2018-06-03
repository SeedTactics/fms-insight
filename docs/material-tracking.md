---
id: material-tracking
title: Material Tracking
sidebar_label: Material Tracking
---

Tracking each piece of material by marking it with a unique serial and
barcode is vital for operating a system with a mix of automated and manual
handling. The key design objectives and goals are as follows:

1. Assist the operators with loading and unloading by providing information about what to produce,
   what material is available, the status of all the in-process material, and load/unload instructions.

2. Increase quality by allow the inspection operator to learn the history of the
   exact piece of material under test (the pallet, machine, program, date and time, etc.).

3. Track unloaded material through manual processes such as final wash or any in-process work
   between automated machine cycles.

4. Properly fill workorders, ensuring that all produced material is assigned to a workorder and that enough
   material is produced to fill workorders on time.

5. Make it as easy as possible for the operators to keep the data in the computers matching
   the reality of the material on the shop floor.

6. When the data in the computers no longer matches the reality on the shop floor, provide
   an easy way of rectifying this situation by updating the data in the computers.

7. Provide long-term tracking and part identification for downstream customers.

The key challenge is in points 5 and 6; the system must make the normal,
everyday operator tasks easy and straightforward but also be robust to the
unknown.

## Sticky Notes

Consider the following method of tracking material not using a computer. On a
whiteboard, divide the whiteboard into regions for all the possible places
that material could be, so a region for each pallet, a region for each
in-process transfer stand, a region for material waiting to be inspected, a
region for material waiting to be washed, a region for in-process queues and
conveyors, a region for castings, a region for material sent out of the
factory for external work, and so on. Any place that material could be has a
corresponding region on the whiteboard.

Now for each piece of material, a new sticky note is used. The part name and
serial is written on the sticky note and on the back is written a log of
activity. At the start of the process, a new sticky note is created and
placed on the whiteboard. As the process unfolds, the sticky note moves
around on the whiteboard corresponding to the material moving around the
factory floor.

For example, a part just completed process 1 and is waiting on pallet 6 at
the load station to be unloaded into the transfer stand. The sticky note for
the material is currently on the whiteboard in the region for pallet 6. As
part of the unload operation, the sticky note is moved on the whiteboard from
the pallet 6 region to the transfer-stand region. (Also, if any material is
loaded, its corresponding sticky note is moved into the pallet 6 region.) The
fact that there is a sticky note existing in the transfer stand position on
the whiteboard signals to the operators that the pallet for the second
operation should be brought to the load station. When the pallet for the
second operation arrives and the material is loaded, the sticky note will
then move from the whiteboard region for the transfer-stand to the region of
the whiteboard for the pallet.

This whiteboard of sticky notes has several great features:

* By centralizing all material on a single whiteboard, it is easy to visualize the
  entire process and make decisions about what actions to take (what material is
  available for loading, what material needs to be inspected, what material to wash and assign to
  a workorder, etc.).

* Operators can at a glance determine if the whiteboard matches the reality on the factory floor.  If there is
  a mismatch, the sticky notes can be moved so that the whiteboard matches reality.

* Operators at a single station can focus only on a section of the whiteboard to carry out their tasks.  For example,
  at the wash station, only the section of the whiteboard corresponding to the wash stand is relevant.

* It is straightforward to accommodate all sorts of material tasks both inside and outside of
  automation.  For example, if a part must be quarantined as potential scrap, its sticky note can be set on
  the side of the whiteboard.  Once examined, if the part is determined to be scrap the sticky note can
  be thrown in the trash can.  If the part needs some remachining, the sticky note can be added
  back onto the whiteboard once the remachining has completed and the part is ready for the remainder of the
  process.

* By using a metaphor of physical sticky notes, the design provides an intuitive understanding for users.
  The design is motivated by Google's Material Design, whose founding idea is: "A material metaphor is the unifying
  theory of a rationalized space and a system of motion. The material is grounded in tactile reality,
  inspired by the study of paper and ink, yet technologically advanced and open to imagination and magic."
  In fact, we suggest that operator training start with actual physical sticky notes on a whiteboard before
  transitioning the training to the computers.

Of course, there is one main downside to using a physical whiteboard; it is
tedious for the operators. This is solved by keeping the whiteboard and
sticky notes in software and using software monitoring to automatically move
around the sticky notes. We believe that when using automation and software as
part of process control, you must always start with a design that could be
done without software. This produces a design which does not force the
process to conform to the software but allows the software to support the
process. It also eases user understanding because it provides a
straightforward metaphor for the user to understand how the software
functions. The software should exist just to support the process by removing
the tedious and error-prone tasks.

## Software-Based Sticky Notes

![Screenshot of Load/Unload Station Screen](/docs/assets/insight-loadstation-small.jpg)

The tactile metaphor of moving around sticky notes, with one sticky note per
piece of material, is the basis for the FMS Insight Station Monitor page. See
the screenshot above, which shows the whiteboard with regions for Raw
Material, the two faces on Pallet 1, and Completed Material. Insight
allows the operator to view a section of the virtual whiteboard of material
sticky notes relevant to the current station, in this case the load station 1.

Insight monitors the machines and automation and moves around the material
sticky notes on the virtual whiteboard as parts are loaded, unloaded, and
machined. Also, by watching the virtual whiteboard, Insight can control the
pallets. For example, Insight will prevent a pallet from arriving at the load
station if the transfer stand queue virtual whiteboard region is empty.

Insight allows the operator to keep the virtual whiteboard in sync with the
material on the factory floor. Operators can remove virtual material sticky
notes from the whiteboard by clicking a button and add sticky notes by
scanning a barcode or manually entering a serial. Each queue or conveyor or
place where material is under manual control has a cooresponding whiteboard
region. By placing a computer or tablet at that place in the factory, the
operator can at a glance see if the virtual whiteboard matches the reality on
the factory floor, and if not simply edit the virtual whiteboard to match.

## An Example Design

The virtual whiteboard implemented by FMS Insight is versitle and can adjust
to a huge variety of processes with parts moving in and out of automation and
manual handling.

For example, consider a process with two cells: a horizonal machining cell
and a lathe cell. Parts are manually loaded and unloaded and transfer between
cells via a conveyor. The conveyor between the cells will correspond to a
region on the virtual whiteboard. Also, right next to the conveyor, there is
a computer or mounted tablet with an attached barcode scanner. The computer
uses FMS Insight to display the region of the whiteboard for the conveyor. As
parts are unloaded from one cell, FMS Insight automatically moves the
cooresponding material sticky note from the pallet to the conveyor region.
Simiarly, FMS Insight is configured to monitor the virtual whiteboard region
for the conveyor to know when to bring pallets to the load station to load
material out of the conveyor.

During normal operation, the operator does not need to manually edit the
virtual whiteboard. As parts are completed in one cell, their sticky notes
are moved to the whiteboard region for the conveyor and when the material is
loaded into the second cell the sticky notes are removed from the whiteboard
region for the conveyor.  The operator can at a glance see that the conveyor
matches the virtual whiteboard by comparing the conveyor to the computer screen.

Now consider that some parts are removed from the conveyor temporarily.
Perhaps the parts are inspected, a part needs some re-machining, or it must
travel elsewhere in the factory for a specialized operation. When the
material is removed from the conveyor, the operator can click a button in FMS
Insight to remove the sticky note from the virtual whiteboard. FMS Insight
will then prevent a pallet from arriving to load this missing material. The
material eventually returns and when it does, the operator can scan the
barcode on the part to add the corresponding virtual sticky note onto the
whiteboard.  FMS Insight will then activate the pallet to load this piece of
material in the second cell.

## Whiteboard tracking design

FMS Insight requires that you decide on a list of virtual whiteboard regions
and how material will move between regions. The initial design is best done
by using an actual whiteboard in a conference room with real sticky notes.
Draw regions on the whiteboard, create some sample sticky notes, and step through
the proposed process, moving the sticky notes around on the whiteboard.  This helps
design the whiteboard regions to configure in FMS Insight, but also highlights potential
complicated material flow which you might consider changing the process.

At the load station, use a computer or mounted tablet running Insight.
Insight will display the section of the virtual whiteboard for the pallet,
plus the region for material being loaded and the region to which material is
unloaded. Insight will determine based on the whiteboard which specific
material to load and display it with its serial. The operator should then
load the material with the specific serial requested by Insight. But, if the
serial that Insight wants to load is unavailable, the operator can just edit
the virtual whiteboard. Insight will automatically adjust with a new
instruction for the operator. (Hopefully this doesn't happen and the virtual
whiteboard is kept in sync, but if it does happen it is easy to correct.)

At any place where in-process material is queued outside of control of the
automation, create a whiteboard region. This could be conveyors between cells
or stands for holding parts between processes (e.g. large parts which have
process 1 and process 2 on different pallets). At each location, dedicate a
computer or mounted tablet with an attached barcode scanner to view and edit
the whiteboard region. Insight can be configured to automatically move the
virtual sticky notes during normal opreations as parts are loaded and
unloaded into these queues, but the operator can use the computer and scanner
to adjust the whiteboard region as needed.

For in-process manual operations such as manual CMM stations or custom
processes outside the auatomated cell, create two whiteboard regions. One
region for the input to the process and one region for the output from the
process. For example, at an in-process CMM stand, create a whiteboard region
for the unloaded parts not yet measured. FMS Insight can then be configured
to automatically move the material sticky note from the pallet to the inbound
CMM region. The CMM stand then has a computer or mounted tablet showing
Insight; the operator can then see the material not yet measured, perform the
CMM measurement, and if successful, use FMS Insight to transfer the virtual
sticky note from the inbound region to the outbound region. FMS Insight
monitors the outbound region and will bring a pallet to the load station once
the CMM has completed. Insight will also transfer the sticky note from the
outbound region to the pallet as part of the load operation.

For raw material, Insight supports two options. Insight can just assume raw
material is always available in which case Insight will create a new virtual
sticky note during the initial loading. In this design, the first time a
sticky note appears is on the pallet. Alternativly, Insight can be configured
to draw initial material from a virtual whiteboard region. In this case, when
material arrives, an operator must scan or add the material into the initial
whiteboard region. Once added, FMS Insight will then activate the pallet to
load this material.

For completed material, Insight automatically adds the sticky notes to
regions for signaled inspections and final wash. Technically, these could
just be configured virtual whiteboard regions similar to in-process queues or
operations, but inspections and final wash are so common that Insight
automatically has special regions for them. Each configured inspection type
has two regions: one for completed parts signaled for inspection but not yet
inspected and one region for completed inspection. For final wash, Insight
also has two regions: one for not yet washed parts and one for washed parts.
A computer mounted at the inspection and wash stations can show Insight and
allow the operator to move the virtual sticky note as the inspection or final
wash is completed.