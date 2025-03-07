using System.Runtime.CompilerServices;
using DiffEngine;
using VerifyTests;

namespace BlackMaple.FMSInsight.Tests;

public static class VerifySetup
{
  [ModuleInitializer]
  public static void InitVerify()
  {
    DiffTools.UseOrder(DiffTool.VisualStudioCode);
    VerifyDiffPlex.Initialize();
  }
}
