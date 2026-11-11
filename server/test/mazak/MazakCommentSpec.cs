using System.Threading.Tasks;
using MazakMachineInterface;
using Shouldly;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public class MazakCommentSpec
  {
    [Test]
    [Arguments("-InsightS")]
    [Arguments("x-InsightS")]
    [Arguments("x-1-InsightS")]
    [Arguments("-1-1-InsightS")]
    [Arguments("x-x-1-InsightS")]
    [Arguments("x-0-1-InsightS")]
    [Arguments("x-1-0-InsightS")]
    [Arguments("x-1-x-InsightS")]
    [Arguments("x-999999999999-1-InsightS")]
    public async Task MalformedSplitCommentsHaveNoManufacturingOwner(string comment)
    {
      await Assert.That(MazakPart.UniqueFromComment(comment)).IsEqualTo("");
      await Assert.That(MazakPart.ParseCommentInfo(comment).HasUnique).IsFalse();
      await Assert.That(MazakPart.ProcessFromComment(comment)).IsEqualTo(1);
    }

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
        .ShouldBe(
          new MazakPart.MazakCommentInfo(Unique: "combined-uniq", IsSplit: false, FmsProcess: 1)
        );

      MazakPart
        .ParseCommentInfo("split-uniq-2-1-InsightS")
        .ShouldBe(
          new MazakPart.MazakCommentInfo(Unique: "split-uniq", IsSplit: true, FmsProcess: 2)
        );
    }
  }
}
