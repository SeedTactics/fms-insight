/* Copyright (c) 2024, John Lenz

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
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Makino
{
  public class Jobs
  {
    private MakinoDB _db;
    private Func<IRepository> _openJobDB;
    private Action _onJobCommentChange;
    private string _xmlPath;
    private bool _onlyOrders;
    public event NewJobsDelegate OnNewJobs;
    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    public Jobs(
      MakinoDB db,
      Func<IRepository> jdb,
      string xmlPath,
      bool onlyOrders,
      Action onJobCommentChange
    )
    {
      _db = db;
      _openJobDB = jdb;
      _xmlPath = xmlPath;
      _onlyOrders = onlyOrders;
      _onJobCommentChange = onJobCommentChange;
    }

    public void AddJobs(NewJobs newJ, string expectedPreviousScheduleId)
    {
      var newJobs = new List<Job>();
      using (var jdb = _openJobDB())
      {
        newJ = newJ with
        {
          Jobs = newJ
            .Jobs.Select(j =>
              j.AdjustAllPaths(path =>
                path with
                {
                  Stops = path.Stops.Select(s => s with { StationGroup = "MC" }).ToImmutableList()
                }
              )
            )
            .ToImmutableList(),
        };
        foreach (var j in newJ.Jobs)
        {
          newJobs.Add(j);
        }

        jdb.AddJobs(newJ, expectedPreviousScheduleId, addAsCopiedToSystem: true);
      }
      OrderXML.WriteOrderXML(System.IO.Path.Combine(_xmlPath, "sail.xml"), newJobs, _onlyOrders);
      OnNewJobs?.Invoke(newJ);
    }
  }
}
