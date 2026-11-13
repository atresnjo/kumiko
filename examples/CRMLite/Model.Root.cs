using System.Text.Json.Serialization;
using Kumiko.Runtime;

namespace CRMLite;

public sealed partial record CrmModel
{
    /// <summary>
    /// The history of the single application screen; pages within it use <see cref="Navigation"/>. A one-visit
    /// history never changes, so it is omitted from JSON and recordings made before it existed replay unchanged.
    /// </summary>
    [JsonIgnore] public NavigationHistory<string> RootHistory { get; init; } = NavigationHistory<string>.Start("crm");
}

public sealed partial class CrmApp
{
    /// <summary>The application screen's navigation host key; history requests from any other host are ignored.</summary>
    internal const string RootHost = "app";
}
