/* Copyright (c) 2017, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
  public delegate void NewCurrentStatus(CurrentStatus status);

  public interface IJobControl
  {
    ///loads info
    CurrentStatus GetCurrentStatus();

    //checks to see if the jobs are valid.  Some machine types might not support all the different
    //pallet->part->machine->process combinations.
    //Return value is a list of strings, detailing the problems.
    //An empty list or nothing signals the jobs are valid.
    List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs);

    ///Adds new jobs into the cell controller
    void AddJobs(NewJobs jobs, string expectedPreviousScheduleId);

    void SetJobComment(string jobUnique, string comment);

    //Remove all planned parts from all jobs in the system.
    //
    //The function does 2 things:
    // - Check for planned but not yet machined quantities and if found remove them
    //   and store locally in the machine watch database with a new DecrementId.
    // - Load all decremented quantities (including the potentially new quantities)
    //   strictly after the given decrement ID.
    //Thus this function can be called multiple times to receive the same data.
    List<JobAndDecrementQuantity> DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId);
    List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC);

    //In-process queues

    /// Add new raw material for part.  The part material has not yet been assigned to a specific job,
    /// and will be assigned to the job with remaining demand and earliest priority.
    /// The serial is optional and is passed only if the material has already been marked with a serial.
    void AddUnallocatedPartToQueue(string partName, string queue, int position, string serial);

    /// Add new castings.  The casting has not yet been assigned to a specific job,
    /// and will be assigned to the job with remaining demand and earliest priority.
    /// The serial is optional and is passed only if the material has already been marked with a serial.
    void AddUnallocatedCastingToQueue(string casting, int qty, string queue, int position, IList<string> serial);

    /// Add a new unprocessed piece of material for the given job into the given queue.  The serial is optional
    /// and is passed only if the material has already been marked with a serial.
    /// Use -1 or 0 for lastCompletedProcess if the material is a casting.
    void AddUnprocessedMaterialToQueue(string jobUnique, int lastCompletedProcess, int pathGroup, string queue, int position, string serial);

    /// Add material into a queue or just into free material if the queue name is the empty string.
    /// The material will be inserted into the given position, bumping any later material to a
    /// larger position.  If the material is currently in another queue or a different position,
    /// it will be removed and placed in the given position.
    void SetMaterialInQueue(long materialId, string queue, int position);

    void RemoveMaterialFromAllQueues(IList<long> materialIds);

    event NewCurrentStatus OnNewCurrentStatus;
  }

  public interface IOldJobDecrement
  {
    //The old method of decrementing, which stores only a single decrement until finalize is called.
    Dictionary<JobAndPath, int> OldDecrementJobQuantites();
    void OldFinalizeDecrement();
  }
}