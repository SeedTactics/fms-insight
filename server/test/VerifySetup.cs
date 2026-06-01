using VerifyTests;
using VerifyTUnit;

namespace BlackMaple.FMSInsight.Tests;

public static class VerifySetup
{
  [Before(Assembly)]
  public static void InitVerify()
  {
    if (System.Environment.GetEnvironmentVariable("CI") == "true")
    {
      VerifyDiffPlex.Initialize();
    }
    else
    {
      DiffEngine.DiffTools.UseOrder(DiffEngine.DiffTool.VisualStudioCode);
      VerifierSettings.OmitContentFromException();
    }
  }
}
