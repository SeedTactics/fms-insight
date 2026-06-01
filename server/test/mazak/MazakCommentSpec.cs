using MazakMachineInterface;
using Shouldly;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public class MazakCommentSpec
  {
    [Test]
    [Arguments("uniq-1-1-InsightS", "uniq", 1)]
    [Arguments("uniq-2-1-InsightS", "uniq", 2)]
    [Arguments("uniq-3-1-InsightS", "uniq", 3)]
    [Arguments("uniq-with-dash-2-1-InsightS", "uniq-with-dash", 2)]
    public void ParsesSplitComments(string comment, string expectedUnique, int expectedProcess)
    {
      MazakPart.IsSplitComment(comment).ShouldBeTrue();
      MazakPart.UniqueFromComment(comment).ShouldBe(expectedUnique);
      MazakPart.ProcessFromComment(comment).ShouldBe(expectedProcess);
    }

    [Test]
    public void RecognizesInsightComments()
    {
      MazakPart.IsSailPart("part:3:1", "uniq-Insight").ShouldBeTrue();
      MazakPart.IsSailPart("part:3:1", "uniq-2-1-InsightS").ShouldBeTrue();
      MazakPart.IsSailPart("part:3:1", "uniq-Path1-1-0").ShouldBeTrue();
      MazakPart.IsSailPart("part:3:1", "uniq-Path1").ShouldBeFalse();
    }

    [Test]
    public void PreservesLegacyCommentParsing()
    {
      MazakPart.UniqueFromComment("uniq-Insight").ShouldBe("uniq");
      MazakPart.UniqueFromComment("uniq-Path1").ShouldBe("uniq");
      MazakPart.ProcessFromComment("uniq-Insight").ShouldBe(1);
      MazakPart.IsSplitComment("uniq-Insight").ShouldBeFalse();
    }

    [Test]
    public void ParsesCommentInfoForCombinedAndSplitComments()
    {
      MazakPart
        .ParseCommentInfo(null)
        .ShouldBe(new MazakPart.MazakCommentInfo(Unique: "", IsSplit: false, FmsProcess: 1));

      MazakPart
        .ParseCommentInfo("")
        .ShouldBe(new MazakPart.MazakCommentInfo(Unique: "", IsSplit: false, FmsProcess: 1));

      MazakPart
        .ParseCommentInfo("combined-uniq-Insight")
        .ShouldBe(new MazakPart.MazakCommentInfo(Unique: "combined-uniq", IsSplit: false, FmsProcess: 1));

      MazakPart
        .ParseCommentInfo("split-uniq-2-1-InsightS")
        .ShouldBe(new MazakPart.MazakCommentInfo(Unique: "split-uniq", IsSplit: true, FmsProcess: 2));
    }
  }
}
