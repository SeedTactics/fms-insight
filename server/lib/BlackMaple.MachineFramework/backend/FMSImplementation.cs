/* Copyright (c) 2022, John Lenz

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

namespace BlackMaple.MachineFramework
{
  public interface IBackgroundWorker : IDisposable { }

  public delegate string CustomizeInstructionPath(string part, int? process, string type, long? materialID, string operatorName, string pallet);
  public delegate void PrintLabelForMaterial(long materialId, int process, int? loadStation, string queue);

  public enum BarcodeType
  {
    BarcodeFromScan,
    DirectlyEnteredSerial
  }

  public delegate MaterialDetails ParseBarcode(string barcode, BarcodeType type);

  public enum AddToQueueType
  {
    RequireSerialAndExistingMaterial,
    AllowNewMaterialBySpecifyingJobAndSerial,
    AllowNewMaterialBySpecifyingJobWithoutSerial,
  }

  public class FMSImplementation
  {
    public string Name { get; set; }
    public string Version { get; set; }
    public Func<DateTime> LicenseExpires { get; set; } = null;

    public IFMSBackend Backend { get; set; }
    public IList<IBackgroundWorker> Workers { get; set; } = new List<IBackgroundWorker>();
    public IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> ExtraApplicationParts { get; set; } = null;

    public CustomizeInstructionPath InstructionPath { get; set; } = null;

    public bool UsingLabelPrinterForSerials { get; set; } = false;
    public PrintLabelForMaterial PrintLabel { get; set; } = null;
    public ParseBarcode ParseBarcode { get; set; } = null;

    public string AllowEditJobPlanQuantityFromQueuesPage { get; set; } = null;
    public string CustomStationMonitorDialogUrl { get; set; } = null;
    public bool AddRawMaterialAsUnassigned { get; set; } = true;
    public AddToQueueType AddToQueueType { get; set; } = AddToQueueType.RequireSerialAndExistingMaterial;
  }
}
