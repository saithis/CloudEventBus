using System.Diagnostics;

namespace Ratatoskr.Core;

/// <summary>
/// Provides access to the ActivitySource used for OpenTelemetry tracing in Ratatoskr.
/// </summary>
public static class RatatoskrDiagnostics
{
    public const string ActivitySourceName = "Ratatoskr";

    /// <summary>
    /// The ActivitySource for Ratatoskr.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
