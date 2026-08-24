using Microsoft.AspNetCore.Mvc;

namespace DebugMachineWatchApiServer;

[ApiExplorerSettings(IgnoreApi = true)]
[ApiController]
public sealed class DebugMockScenarioController(MockServerBackend backend) : ControllerBase
{
  [HttpGet("/api/debug-mock/scenario")]
  public IActionResult Status() =>
    backend.ScenarioStatus is { } status
      ? Ok(status)
      : NotFound("No debug-mock scenario is active.");

  [HttpPost("/api/debug-mock/scenario/next")]
  public IActionResult Next() =>
    backend.AdvanceScenario()
      ? Ok(backend.ScenarioStatus)
      : Conflict("The current scenario step does not have exactly one next transition.");

  [HttpPost("/api/debug-mock/scenario/reset")]
  public IActionResult Reset() =>
    backend.ResetScenario()
      ? Ok(backend.ScenarioStatus)
      : NotFound("No debug-mock scenario is active.");

  [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE")]
  [Route("/api/{**path}", Order = int.MaxValue)]
  public IActionResult ScriptedRequest()
  {
    if (
      !backend.TryApplyScenarioRequest(Request.Method, Request.Path.Value ?? "", out var response)
    )
      return NotFound();
    if (response.Json is { } json)
      return new ContentResult
      {
        StatusCode = response.Status,
        ContentType = "application/json; charset=utf-8",
        Content = json.GetRawText(),
      };
    if (response.Body is { } body)
      return new ContentResult
      {
        StatusCode = response.Status,
        ContentType = "text/plain; charset=utf-8",
        Content = body,
      };
    return StatusCode(response.Status);
  }
}
