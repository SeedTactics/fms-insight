/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System.Globalization;

namespace MazakMachineInterface;

// Experimental, undocumented Smooth/Neo schema. Time and quantity are read from the same row;
// no FCREADDAT01 values are mixed into the snapshot. This does not make Mazak's updates atomic.
public static class InternalToolData
{
  public const string Select =
    @"SELECT
      t.mtdb_id AS RowID,
      t.mtdb_dev AS DeviceNumber,
      t.mtdb_pno AS PocketNumber,
      t.mtdb_sts AS ToolStatus,
      t.mtdb_avf AS ToolInformation,
      t.mtdb_tid AS ToolID,
      t.mtdb_tcd AS ToolNameCode,
      t.mtdb_tno AS ToolNumber,
      t.mtdb_gno AS GroupNumber,
      t.mtdb_cmd AS NominalDiameter,
      t.mtdb_nomDia AS NominalDiameterText,
      t.mtdb_suf AS SuffixCode,
      t.mtdb_dia AS ToolDiameter,
      t.mtdb_itf AS InterferenceType,
      t.mtdb_lif AS LifeSpan,
      t.mtdb_use AS LifeUsed,
      t.mtdb_clif AS LifeCount,
      t.mtdb_cuse AS LifeUsedCount,
      p.t5_pkt_status AS PocketStatus,
      p.t5_pkt_attr1 AS PocketAttribute,
      p.t5_pkt_attr2 AS PocketOccupancy
    FROM Mazak_Tool.dbo.Mazak_ToolData t
    LEFT JOIN PMC_Basic.dbo.T5_Pocket p
      ON p.t5_dev = t.mtdb_dev AND p.t5_pktno = t.mtdb_pno
    WHERE t.mtdb_dev BETWEEN 1 AND 16";

#if NET35
  public sealed class Row
#else
  public sealed record Row
#endif
  {
    public int RowID { get; init; }
    public int DeviceNumber { get; init; }
    public int PocketNumber { get; init; }
    public int ToolStatus { get; init; }
    public int ToolInformation { get; init; }
    public long ToolID { get; init; }
    public int ToolNameCode { get; init; }
    public int ToolNumber { get; init; }
    public int GroupNumber { get; init; }
    public int NominalDiameter { get; init; }
    public string NominalDiameterText { get; init; }
    public int SuffixCode { get; init; }
    public int ToolDiameter { get; init; }
    public int InterferenceType { get; init; }
    public int LifeSpan { get; init; }
    public int LifeUsed { get; init; }
    public int LifeCount { get; init; }
    public int LifeUsedCount { get; init; }
    public int? PocketStatus { get; init; }
    public int? PocketAttribute { get; init; }
    public int? PocketOccupancy { get; init; }
  }

  public static ToolPocketRow ToToolPocket(Row row)
  {
    return new ToolPocketRow()
    {
      MachineNumber = row.DeviceNumber,
      PocketNumber = row.PocketNumber,
      PocketType =
        row.PocketAttribute == 255 ? 2
        : row.PocketAttribute == 5 ? 0
        : row.PocketAttribute,
      ToolID = row.ToolID,
      ToolNameCode = row.ToolNameCode,
      ToolNumber = row.ToolNumber,
      // Pocket validity/presence are described in SQL extended properties. ToolStatus and
      // ToolInformation bits are not; do not guess them. Validate this inclusion rule on hardware.
      IsToolDataValid =
        row.DeviceNumber >= 1
        && row.DeviceNumber <= 16
        && row.PocketNumber >= 1
        && row.PocketNumber <= 999
        && row.PocketStatus == 1
        && row.PocketOccupancy == 1
        && row.PocketAttribute.HasValue
        && row.PocketAttribute != 4
        && row.PocketAttribute != 255,
      InterferenceType = row.InterferenceType,
      LifeSpan = row.LifeSpan,
      LifeUsed = row.LifeUsed,
      LifeCount = row.LifeCount,
      LifeUsedCount = row.LifeUsedCount,
      // The scale/format of mtdb_cmd is unverified. Only use the explicit text when provided.
      NominalDiameter = row.NominalDiameterText,
      SuffixCode = row.SuffixCode,
      ToolDiameter = row.ToolDiameter,
      GroupNo = row.GroupNumber.ToString(CultureInfo.InvariantCulture),
    };
  }
}
