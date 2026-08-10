using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlackMaple.MachineFramework;

namespace DebugMachineWatchApiServer;

public sealed record DebugMockScenarioManifest
{
  [JsonPropertyName("initial")]
  public required string Initial { get; init; }

  [JsonPropertyName("steps")]
  public required ImmutableDictionary<string, DebugMockScenarioStep> Steps { get; init; }
}

public sealed record DebugMockScenarioStep
{
  [JsonPropertyName("status")]
  public required string Status { get; init; }

  [JsonPropertyName("on")]
  public ImmutableList<DebugMockScenarioTransition> On { get; init; } = [];
}

public sealed record DebugMockScenarioTransition
{
  [JsonPropertyName("method")]
  public required string Method { get; init; }

  [JsonPropertyName("path")]
  public required string Path { get; init; }

  [JsonPropertyName("response")]
  public required DebugMockScenarioResponse Response { get; init; }

  [JsonPropertyName("next")]
  public required string Next { get; init; }
}

public sealed record DebugMockScenarioResponse
{
  [JsonPropertyName("status")]
  public required int Status { get; init; }

  [JsonPropertyName("json")]
  public JsonElement? Json { get; init; }

  [JsonPropertyName("body")]
  public string Body { get; init; }
}

public sealed record DebugMockScenarioStatus
{
  public required string InitialStep { get; init; }
  public required string CurrentStep { get; init; }
  public required ImmutableList<string> Steps { get; init; }
}

public sealed class DebugMockScenarioPlayer
{
  private readonly DebugMockScenarioManifest _manifest;
  private readonly ImmutableDictionary<string, CurrentStatus> _statuses;
  private string _currentStep;

  private DebugMockScenarioPlayer(
    DebugMockScenarioManifest manifest,
    ImmutableDictionary<string, CurrentStatus> statuses
  )
  {
    _manifest = manifest;
    _statuses = statuses;
    _currentStep = manifest.Initial;
  }

  public CurrentStatus CurrentStatus => _statuses[_currentStep];

  public DebugMockScenarioStatus Status =>
    new()
    {
      InitialStep = _manifest.Initial,
      CurrentStep = _currentStep,
      Steps = _manifest.Steps.Keys.Order(StringComparer.Ordinal).ToImmutableList(),
    };

  public static DebugMockScenarioPlayer Load(
    string manifestPath,
    JsonSerializerOptions jsonSettings,
    TimeSpan offset
  )
  {
    if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
      throw new InvalidOperationException(
        $"BMS_CURRENT_STATUS_SCENARIO does not identify a scenario manifest: '{manifestPath}'."
      );

    DebugMockScenarioManifest manifest;
    try
    {
      using var file = File.OpenRead(manifestPath);
      manifest =
        JsonSerializer.Deserialize<DebugMockScenarioManifest>(file, jsonSettings)
        ?? throw new InvalidOperationException("The scenario manifest contains null.");
    }
    catch (JsonException ex)
    {
      throw new InvalidOperationException(
        $"BMS_CURRENT_STATUS_SCENARIO is not valid scenario JSON: '{manifestPath}'.",
        ex
      );
    }

    Validate(manifest, manifestPath);
    var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
    var statuses = manifest
      .Steps.Select(pair =>
        KeyValuePair.Create(
          pair.Key,
          DebugMockStatusLoader.LoadExternal(
            Path.GetFullPath(pair.Value.Status, directory),
            jsonSettings,
            offset
          )
        )
      )
      .ToImmutableDictionary(StringComparer.Ordinal);
    return new DebugMockScenarioPlayer(manifest, statuses);
  }

  public bool TryTransition(string method, string path, out DebugMockScenarioResponse response)
  {
    var transition = _manifest
      .Steps[_currentStep]
      .On.SingleOrDefault(candidate =>
        string.Equals(candidate.Method, method, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Path, path, StringComparison.Ordinal)
      );
    if (transition is null)
    {
      response = null;
      return false;
    }

    _currentStep = transition.Next;
    response = transition.Response;
    return true;
  }

  public bool Next()
  {
    var transitions = _manifest.Steps[_currentStep].On;
    if (transitions.Count != 1)
      return false;
    _currentStep = transitions[0].Next;
    return true;
  }

  public void Reset() => _currentStep = _manifest.Initial;

  private static void Validate(DebugMockScenarioManifest manifest, string manifestPath)
  {
    if (
      string.IsNullOrWhiteSpace(manifest.Initial) || !manifest.Steps.ContainsKey(manifest.Initial)
    )
      throw new InvalidOperationException(
        $"Scenario '{manifestPath}' does not contain its initial step '{manifest.Initial}'."
      );
    foreach (var (name, step) in manifest.Steps)
    {
      if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(step.Status))
        throw new InvalidOperationException(
          $"Scenario '{manifestPath}' contains a step without a name or status file."
        );
      if (step.On.Count > 1)
        throw new InvalidOperationException(
          $"Scenario step '{name}' has multiple transitions; debug-mock currently supports linear scenarios."
        );
      foreach (var transition in step.On)
      {
        if (
          string.IsNullOrWhiteSpace(transition.Method)
          || string.IsNullOrWhiteSpace(transition.Path)
          || !transition.Path.StartsWith("/api/", StringComparison.Ordinal)
          || transition.Path.StartsWith("/api/debug-mock/", StringComparison.Ordinal)
        )
          throw new InvalidOperationException(
            $"Scenario step '{name}' contains an invalid transition method or path."
          );
        if (!manifest.Steps.ContainsKey(transition.Next))
          throw new InvalidOperationException(
            $"Scenario step '{name}' transitions to missing step '{transition.Next}'."
          );
        if (transition.Response.Status is < 100 or > 599)
          throw new InvalidOperationException(
            $"Scenario step '{name}' contains invalid HTTP status {transition.Response.Status}."
          );
      }
    }
  }
}
