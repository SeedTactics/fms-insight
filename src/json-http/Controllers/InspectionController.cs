using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BlackMaple.MachineWatchInterface;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/[controller]")]
    public class InspectionController : Controller
    {
        private IInspectionControl _server;

        public InspectionController(IServerBackend backend)
        {
            _server = backend.InspectionControl();
        }

        [HttpGet("counts")]
        public List<InspectCount> GetCounts()
        {
            return _server.LoadInspectCounts();
        }

        [HttpPut("counts")]
        public void SetCounts([FromBody] List<InspectCount> newCounts)
        {
            _server.SetInspectCounts(newCounts);
        }

        [HttpPut ("material/{materialID}/{inspectionType}")]
        public void ForceInspection(long materialID, string inspectionType)        
        {
            _server.ForceInspection(materialID, inspectionType);
        }

        [HttpPut ("pallet/{pallet}/{location}/{inspectionType}")]
        public void NextPieceInspection(int pallet, PalletLocationTypeEnum location, string inspectionType)
        {
            _server.NextPieceInspection(new PalletLocation(location, pallet), inspectionType);
        }

        [HttpGet ("types")]
        public List<string> GetGlobalInspectionTypes()
        {
            return _server.LoadGlobalInspectionTypes();
        }

        [HttpGet ("type/{ty}")]
        public InspectionType GetGlobalInspectionType(string ty)
        {
            return _server.LoadGlobalInspectionType(ty);
        }

        [HttpPut ("type/{ty}")]
        public void SetGlobalInspection(string ty, [FromBody] InspectionType ity)
        {
            _server.SetGlobalInspectionType(ity);
        }

        [HttpDelete ("type/{ty}")]
        public void DeleteGlobalInspection(string ty)
        {
            _server.DeleteGlobalInspectionType(ty);
        }

    }
}
