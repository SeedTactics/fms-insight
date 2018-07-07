/* Copyright (c) 2018, John Lenz

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
using Xunit;
using MazakMachineInterface;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MachineWatchTest {

	public class HoldTests
	{
		[Fact]
		public void HoldDb()
		{
			var holdConn = BlackMaple.MachineFramework.SqliteExtensions.ConnectMemory();
			holdConn.Open();
			var hold = new HoldPattern(holdConn, null, null, new System.Diagnostics.TraceSource("temp"), false);
			hold.CreateTables();

			var job1 = new JobPlan("Job1", 1, new int[] {2});
			job1.HoldEntireJob.HoldUnholdPatternRepeats = false;
			job1.HoldEntireJob.HoldUnholdPatternStartUTC = DateTime.Parse("6/2/2010 6:43 AM").ToUniversalTime();
			job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(15));
			job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(25));
			job1.HoldMachining(1, 1).HoldUnholdPatternRepeats = true;
			job1.HoldMachining(1, 1).HoldUnholdPatternStartUTC = DateTime.Parse("2/18/2010 9:34 PM").ToUniversalTime();
			job1.HoldMachining(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(153));
			job1.HoldMachining(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(85));
			job1.HoldMachining(1, 2).HoldUnholdPatternRepeats = true;
			job1.HoldMachining(1, 2).HoldUnholdPatternStartUTC = DateTime.Parse("8/2/2010 3:08 PM").ToUniversalTime();
			job1.HoldMachining(1, 2).HoldUnholdPattern.Add(TimeSpan.FromMinutes(22));
			job1.HoldMachining(1, 2).HoldUnholdPattern.Add(TimeSpan.FromMinutes(42));

			hold.SaveHoldMode(2, job1, 1);
			hold.SaveHoldMode(4, job1, 2);

			var job2 = new JobPlan("Job2", 1);
			job2.HoldEntireJob.HoldUnholdPatternRepeats = true;
			job2.HoldEntireJob.HoldUnholdPatternStartUTC = DateTime.Parse("4/22/2010 8:53 AM").ToUniversalTime();
			job2.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(84));
			job2.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(12));
			job2.HoldMachining(1, 1).HoldUnholdPatternRepeats = false;
			job2.HoldMachining(1, 1).HoldUnholdPatternStartUTC = DateTime.Parse("10/13/2010 12:34 PM").ToUniversalTime();
			job2.HoldMachining(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(1112));
			job2.HoldMachining(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(64));

			hold.SaveHoldMode(6, job2, 1);

			var load1 = new JobPlan("Job1", 1, new int[] {2});
			hold.LoadHoldIntoJob(2, load1, 1);
			hold.LoadHoldIntoJob(4, load1, 2);

			AssertHold(job1.HoldEntireJob, load1.HoldEntireJob);
			AssertHold(job1.HoldMachining(1, 1), load1.HoldMachining(1, 1));
			AssertHold(job1.HoldMachining(1, 2), load1.HoldMachining(1, 2));

			var load2 = new JobPlan("Job2", 1);
			hold.LoadHoldIntoJob(6, load2, 1);
			AssertHold(job2.HoldEntireJob, load2.HoldEntireJob);
			AssertHold(job2.HoldMachining(1, 1), load2.HoldMachining(1, 1));

			var hDict = GetHoldPatterns(hold);

			Assert.Equal(3, hDict.Count);
			Assert.True(hDict.ContainsKey(2), "Key 2");
			Assert.True(hDict.ContainsKey(4), "Key 4");
			Assert.True(hDict.ContainsKey(6), "Key 6");

			AssertHold(job1.HoldEntireJob,       hDict[2].HoldEntireJob);
			AssertHold(job1.HoldMachining(1, 1), hDict[2].HoldMachining);
			AssertHold(job1.HoldEntireJob,       hDict[4].HoldEntireJob);
			AssertHold(job1.HoldMachining(1, 2), hDict[4].HoldMachining);
			AssertHold(job2.HoldEntireJob,       hDict[6].HoldEntireJob);
			AssertHold(job2.HoldMachining(1, 1), hDict[6].HoldMachining);

			DeleteHold(hold, 6);

			hDict = GetHoldPatterns(hold);

			Assert.Equal(2, hDict.Count);
			Assert.True(hDict.ContainsKey(2), "Key 2");
			Assert.True(hDict.ContainsKey(4), "Key 4");

			AssertHold(job1.HoldEntireJob,       hDict[2].HoldEntireJob);
			AssertHold(job1.HoldMachining(1, 1), hDict[2].HoldMachining);
			AssertHold(job1.HoldEntireJob,       hDict[4].HoldEntireJob);
			AssertHold(job1.HoldMachining(1, 2), hDict[4].HoldMachining);
		}

		private void AssertHold(JobHoldPattern h1, JobHoldPattern h2)
		{
			Assert.Equal(h1.HoldUnholdPatternStartUTC, h2.HoldUnholdPatternStartUTC);
			Assert.Equal(h1.HoldUnholdPatternRepeats, h2.HoldUnholdPatternRepeats);
			Assert.Equal(h1.HoldUnholdPattern.Count, h2.HoldUnholdPattern.Count);
			for (int i = 0; i < h1.HoldUnholdPattern.Count - 1; i++)
				Assert.Equal(h1.HoldUnholdPattern[i], h2.HoldUnholdPattern[i]);
		}

		//we use reflection to test private methods
		private void DeleteHold(HoldPattern hold, int schID)
		{
			var method = typeof(HoldPattern).GetMethod("DeleteHold",
			                                           System.Reflection.BindingFlags.NonPublic |
			                                           System.Reflection.BindingFlags.Instance);
			method.Invoke(hold, new object[] {schID});
		}

		private IDictionary<int, HoldPattern.JobHold> GetHoldPatterns(HoldPattern hold)
		{
			var method = typeof(HoldPattern).GetMethod("GetHoldPatterns",
			                                           System.Reflection.BindingFlags.NonPublic |
			                                           System.Reflection.BindingFlags.Instance);
			return (IDictionary<int, HoldPattern.JobHold>) method.Invoke(hold, new object[] {});
		}
	}
}
