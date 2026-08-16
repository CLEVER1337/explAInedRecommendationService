public enum SourceOutcome
{
    Ok,
    Empty,
    Timeout,
    Faulted,
    Unavailable,
    Disabled,
}

public static class SourceOutcomeExtensions
{
    public static string ToWire(this SourceOutcome outcome) => outcome.ToString().ToLowerInvariant();
}
