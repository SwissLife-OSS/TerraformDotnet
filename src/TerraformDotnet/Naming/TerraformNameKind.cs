namespace TerraformDotnet.Naming;

/// <summary>
/// The kinds of names a <see cref="TerraformNamingConvention"/> covers. They are the block types of
/// the tflint rule <c>terraform_naming_convention</c>.
/// </summary>
public enum TerraformNameKind
{
    /// <summary>The name of a <c>resource "type" "name"</c> block.</summary>
    Resource,

    /// <summary>The name of a <c>data "type" "name"</c> block.</summary>
    Data,

    /// <summary>The name of a <c>module "name"</c> block.</summary>
    Module,

    /// <summary>The name of a <c>variable "name"</c> block.</summary>
    Variable,

    /// <summary>The name of an <c>output "name"</c> block.</summary>
    Output,

    /// <summary>The name of an entry of a <c>locals</c> block.</summary>
    Local,

    /// <summary>The name of a <c>check "name"</c> block.</summary>
    Check,
}
