/* Copyright (c) 2019, John Lenz

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
using System.Runtime.Serialization;
#if !NET35
using System.Threading.Tasks;
#endif

namespace BlackMaple.MachineWatchInterface
{
  [DataContract]
  public class ProgramContent
  {
    [DataMember]
    public string MainProgramContent { get; set; }

    [DataMember]
    public Dictionary<string, string> SubPrograms { get; set; }
  }

  [DataContract]
  public class ProgramVersion
  {
    [DataMember]
    public string ProgramName { get; set; }

    [DataMember]
    public string Author { get; set; }

    [DataMember]
    public string Message { get; set; }

    [DataMember]
    public DateTime TimeUTC { get; set; }
  }

  [DataContract]
  public class ProgramRevision : ProgramVersion
  {
    [DataMember]
    public string RevisionId { get; set; }
  }

  [DataContract]
  public class ProgramFeatureBranch
  {
    [DataMember]
    public string BranchName { get; set; }

    [DataMember]
    public IEnumerable<ProgramRevision> ModifiedPrograms { get; set; }
  }

#if !NET35 // don't include in MachineWatchInterface
  public interface IProgramManagement
  {
    Task<IEnumerable<ProgramRevision>> GetLatestPrograms();
    Task<ProgramRevision> GetLatestProgram(string programName);
    Task<IEnumerable<ProgramRevision>> GetProgramHistory(string programName, int skip, int count);
    Task<ProgramContent> GetProgramContent(string revisionId, string programName);

    Task<IEnumerable<ProgramFeatureBranch>> GetFeatureBranches();
    Task<ProgramFeatureBranch> GetFeatureBranch(string branchName);
    Task<ProgramFeatureBranch> CreateFeatureBranch(string branchName);
    Task DeleteFeatureBranch(string branchName);
    Task<IEnumerable<ProgramRevision>> GetBranchHistory(string branchName, int skip, int count);
    Task ReleaseBranchToProduction(string branchName);

    // The following are the only potentially destructive updates, since multiple users could
    // overwrite each other.
    Task<ProgramRevision> NewRevisionOnBranch(string branchName, ProgramVersion program);
    Task<ProgramRevision> NewProductionRevision(ProgramVersion program);
  }
#endif
}