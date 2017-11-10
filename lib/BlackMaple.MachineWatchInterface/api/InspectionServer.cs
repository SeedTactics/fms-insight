using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
    public interface IInspectionControl
    {
        //Forces the given materialID to be inspected
        void ForceInspection(long materialID, string inspectionType);

        //Forces the next piece that runs on the given location to be inspected
        void NextPieceInspection(MachineWatchInterface.PalletLocation palLoc, string inspType);

        //allow editing of counts.  Uses lists of InspectCount structure
        List<InspectCount> LoadInspectCounts();
        void SetInspectCounts(IEnumerable<InspectCount> countUpdates);

        List<string> LoadGlobalInspectionTypes();

        InspectionType LoadGlobalInspectionType(string ty);

        void SetGlobalInspectionType(InspectionType ty);

        void DeleteGlobalInspectionType(string ty);
    }
}
