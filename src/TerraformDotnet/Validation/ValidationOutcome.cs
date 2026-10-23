namespace TerraformDotnet.Validation;

/// <summary>The verdict of evaluating one <c>validation</c> block.</summary>
public enum ValidationOutcome
{
    /// <summary>The condition evaluated to <c>true</c>.</summary>
    Passed,

    /// <summary>The condition definitely evaluated to <c>false</c>.</summary>
    Failed,

    /// <summary>
    /// The condition could not be evaluated offline, for example because it depends on a value that
    /// is not set yet, a resource or data source, or a function that is not supported. It is
    /// never reported as <see cref="Failed"/>.
    /// </summary>
    Indeterminate,
}
