using System;
using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
    public interface IPalletServer
    {
        // Update pallet status
        void SetHold(string pal, bool hold);
        void MarkMaterialLoadedOntoPallet(string pal, string face, long matID);
        void MarkLoadUnloadInstructionsCompleted(string pal);
        void RejectLoadUnloadInstructions(string pal);
        void StartManualPalletMove(string pal, PalletLocation target);
        void ScrapMaterialFromPallet(string pal, int materialID);

        // Override pallet status
        void OverridePalletLocation(string pal, PalletLocation currentLocation);
        void OverrideFixtureOnPallet(string pal, string fixture);
    }
}
