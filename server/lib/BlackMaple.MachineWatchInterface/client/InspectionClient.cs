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
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace BlackMaple.MachineWatchInterface
{
    public class InspectionClient : MachineWatchHttpClient
    {
        public InspectionClient(string host, string token) : base(host, token)
        {}

        public async Task<List<InspectCount>> LoadCounts()
        {
            return await RecvJson<List<InspectCount>>(HttpMethod.Get, "/api/inspection/counts");
        }

        public async Task SetCounts(List<InspectCount> counts)
        {
            await SendJson(HttpMethod.Put, "/api/inspection/counts", counts);
        }

        public async Task ForceInspection(long matID, string inspType)
        {
            await Send(HttpMethod.Put, "/api/inspection/material/" + matID.ToString() + "/" +
                WebUtility.UrlEncode(inspType));
        }

        public async Task NextPieceInspection(int pallet, PalletLocationTypeEnum loc, string inspType)
        {
            await Send(HttpMethod.Put, "/api/inspection/pallet/" + pallet.ToString() + "/" +
                loc.ToString() + "/" + WebUtility.UrlEncode(inspType));
        }

        public async Task<List<string>> GetInspectionTypes()
        {
            return await RecvJson<List<string>>(HttpMethod.Get, "/api/inspection/types");
        }

        public async Task<InspectionType> GetInspectionType(string ty)
        {
            return await RecvJson<InspectionType>(HttpMethod.Get, "/api/inspection/type/" +
              WebUtility.UrlEncode(ty));
        }

        public async Task SetInspectionType(InspectionType ty)
        {
            await SendJson<InspectionType>(HttpMethod.Post, "/api/inspection/type/" +
              WebUtility.UrlEncode(ty.Name), ty);
        }

        public async Task DeleteInspectionType(string ty)
        {
            await Send(HttpMethod.Delete, "/api/inspection/type/" + WebUtility.UrlEncode(ty));
        }
    }
}
