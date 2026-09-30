namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Thrown when an evaluation exceeds a configured safety limit (recursion depth or iteration count).
/// </summary>
/// <remarks>
/// Unlike ordinary evaluation errors, this exception is never swallowed by <c>can(...)</c> or
/// <c>try(...)</c>, so a hostile expression cannot hide the fact that it was aborted.
/// </remarks>
public sealed class HclEvaluationLimitException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="HclEvaluationLimitException"/> class.</summary>
    /// <param name="message">A description of the limit that was exceeded.</param>
    public HclEvaluationLimitException(string message)
        : base(message)
    {
    }
}
