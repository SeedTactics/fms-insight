using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using FluentAssertions;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("MachineWatchTest.InsightTestFramework", "BlackMaple.MachineFramework.Tests")]

namespace MachineWatchTest
{
  public class InsightTestFramework : XunitTestFramework
  {
    public InsightTestFramework(IMessageSink messageSink) : base(messageSink)
    {
      AssertionOptions.AssertEquivalencyUsing(
          options => options
          .ComparingByMembers<HistoricData>()
          .ComparingByMembers<Job>()
          .ComparingByMembers<HistoricJob>()
          .ComparingByMembers<HoldPattern>()
          .ComparingByMembers<ProcessInfo>()
          .ComparingByMembers<ProcPathInfo>()
          .ComparingByMembers<MachiningStop>()
          .ComparingByMembers<PlannedSchedule>()
          .ComparingByMembers<PartWorkorder>()
          .ComparingByMembers<WorkorderProgram>()
          .ComparingByMembers<MaterialDetails>()
      );
    }
  }
}