/* Copyright (c) 2021, John Lenz

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
using System.Linq;
using System.Collections.Immutable;

namespace BlackMaple.MachineFramework
{
  public static class HoldCalculations
  {
    public static bool IsOnHold(this HoldPattern hold)
    {
      HoldInformation(hold, DateTime.UtcNow, out bool isOnHold, out var nextTransition);
      return isOnHold;
    }

    /// Given a time, allows you to calculate if the hold is active and the next transition time.
    public static void HoldInformation(this HoldPattern hold, DateTime nowUTC, out bool isOnHold, out DateTime nextTransitionUTC)
    {

      if (hold.UserHold)
      {
        isOnHold = true;
        nextTransitionUTC = DateTime.MaxValue;
        return;
      }

      if (hold.HoldUnholdPattern.Count == 0)
      {
        isOnHold = false;
        nextTransitionUTC = DateTime.MaxValue;
        return;
      }

      //check that the hold pattern has a non-zero timespan.
      //Without this check we will get in an infinite loop below.
      bool foundSpan = false;
      foreach (var span in hold.HoldUnholdPattern)
      {
        if (span.Ticks > 0)
        {
          foundSpan = true;
          break;
        }
      }
      if (!foundSpan)
      {
        //If not, then we are not on hold.
        isOnHold = false;
        nextTransitionUTC = DateTime.MaxValue;
        return;
      }

      if (nowUTC < hold.HoldUnholdPatternStartUTC)
      {
        isOnHold = false;
        nextTransitionUTC = hold.HoldUnholdPatternStartUTC;
      }

      //Start with a span from the current time to the start time.
      //We will remove time from this span until it goes negative, indicating
      //that we have found the point in the pattern containing the current time.
      var remainingSpan = nowUTC.Subtract(hold.HoldUnholdPatternStartUTC);

      int curIndex = 0;

      do
      {

        // Decrement the time.
        remainingSpan = remainingSpan.Subtract(hold.HoldUnholdPattern[curIndex]);

        // If we pass 0, we have found the span with the current time.
        if (remainingSpan.Ticks < 0)
        {
          //since remainingSpan is negative, we subtract to add the time to the next transition.
          isOnHold = (curIndex % 2 == 0);
          nextTransitionUTC = nowUTC.Subtract(remainingSpan);
          return;
        }

        curIndex += 1;

        // check for repeat patterns
        if (curIndex >= hold.HoldUnholdPattern.Count && hold.HoldUnholdPatternRepeats)
          curIndex = 0;

      } while (curIndex < hold.HoldUnholdPattern.Count && remainingSpan.Ticks > 0);

      //We are past the end of the pattern, so we are not on hold.
      isOnHold = false;
      nextTransitionUTC = DateTime.MaxValue;
    }
  }
}