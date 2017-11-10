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
