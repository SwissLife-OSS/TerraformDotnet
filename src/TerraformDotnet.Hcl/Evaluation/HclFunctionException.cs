namespace TerraformDotnet.Hcl.Evaluation;

/// <summary>
/// Thrown by an <see cref="IHclFunctionResolver"/> when a function is called with invalid arguments.
/// </summary>
/// <remarks>
/// This is an ordinary evaluation error: <c>can(...)</c> turns it into <c>false</c> and
/// <c>try(...)</c> moves on to its next argument.
/// </remarks>
public sealed class HclFunctionException : InvalidOperationException
{
    /// <summary>Gets the name of the function that failed.</summary>
    public string FunctionName { get; }

    /// <summary>Initializes a new instance of the <see cref="HclFunctionException"/> class.</summary>
    /// <param name="functionName">The name of the function that failed.</param>
    /// <param name="message">A description of why the call is invalid.</param>
    public HclFunctionException(string functionName, string message)
        : base($"Error in function call '{functionName}': {message}")
    {
        FunctionName = functionName;
    }
}
