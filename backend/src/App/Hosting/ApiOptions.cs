using System.ComponentModel.DataAnnotations;

namespace App.Api.Hosting;

// The "Api" settings section. Validated at start-up: a missing value stops the host, and
// the error names the setting, never its value.
public sealed class ApiOptions
{
    public const string Section = "Api";

    [Required]
    public string Title { get; set; } = "";

    [Required]
    public string Version { get; set; } = "";
}
