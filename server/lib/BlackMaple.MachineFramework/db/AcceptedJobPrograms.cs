using System.Collections.Immutable;
using System.Data;
using System.Linq;

namespace BlackMaple.MachineFramework;

internal sealed partial class Repository
{
  private long? ResolveProgramRevision(
    IDbTransaction transaction,
    string? program,
    long? revision,
    ImmutableDictionary<(string prog, long rev), long> negativeRevisions,
    string context
  )
  {
    if (!revision.HasValue || revision.Value == 0)
      return string.IsNullOrEmpty(program) ? null : LatestRevisionForProgram(transaction, program);
    if (revision.Value > 0)
      return revision;
    if (
      program != null
      && negativeRevisions.TryGetValue((program, revision.Value), out var accepted)
    )
      return accepted;
    throw new BadRequestException(
      context
        + "has a negative program revision but no matching negative program revision exists in the downloaded ProgramEntry list"
    );
  }

  private Job ResolveJobPrograms(
    IDbTransaction transaction,
    Job job,
    ImmutableDictionary<(string prog, long rev), long> negativeRevisions
  ) =>
    job with
    {
      Processes = job
        .Processes.Select(
          (process, processIndex) =>
            process with
            {
              Paths = process
                .Paths.Select(
                  (path, pathIndex) =>
                    path with
                    {
                      Stops = path
                        .Stops.Select(
                          (stop, stopIndex) =>
                            stop with
                            {
                              ProgramRevision = ResolveProgramRevision(
                                transaction,
                                stop.Program,
                                stop.ProgramRevision,
                                negativeRevisions,
                                $"Part {job.PartName}, process {processIndex + 1}, path {pathIndex + 1}, stop {stopIndex}, "
                              ),
                            }
                        )
                        .ToImmutableList(),
                    }
                )
                .ToImmutableList(),
            }
        )
        .ToImmutableList(),
    };

  private Workorder ResolveWorkorderPrograms(
    IDbTransaction transaction,
    Workorder workorder,
    ImmutableDictionary<(string prog, long rev), long> negativeRevisions
  ) =>
    workorder with
    {
      Programs = workorder
        .Programs?.Select(program =>
          program with
          {
            Revision = ResolveProgramRevision(
              transaction,
              program.ProgramName,
              program.Revision,
              negativeRevisions,
              $"Workorder {workorder.WorkorderId} "
            ),
          }
        )
        .ToImmutableList(),
    };
}
