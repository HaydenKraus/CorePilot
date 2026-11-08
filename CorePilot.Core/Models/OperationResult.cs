namespace CorePilot.Core.Models;

/// <summary>Result of a native operation: success flag plus human readable error.</summary>
public readonly record struct OperationResult(bool Success, string? Error)
{
    public static OperationResult Ok => new(true, null);

    public static OperationResult Fail(string error) => new(false, error);

    public static OperationResult From(bool value, string error) => value ? Ok : Fail(error);
}
