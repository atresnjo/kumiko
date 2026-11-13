using System.Text.Json.Serialization;

namespace CRMLite;

public sealed record AuthState(AuthSession? Session = null, string Email = "maya@harbor.app", bool Busy = false, string? Error = null, int Request = 0,
    Screen? ReturnTo = null);

public sealed record BackendState(bool Loaded = true, bool Loading = false, bool Saving = false, bool Dirty = false,
    long Revision = 0, int Request = 0, string? Error = null, int SearchRequest = 0);

public sealed partial record CrmModel
{
    // Data.Initial is the preloaded fixture. CrmApp.Init starts at the sign-in screen.
    public AuthState Auth { get; init; } = new(new("fake-demo-maya", Data.Team[0], true));
    public BackendState Backend { get; init; } = new();
    [JsonIgnore]
    public Member CurrentUser => Auth.Session?.User ?? Data.Team[0];
    [JsonIgnore]
    public bool CanEdit => Auth.Session?.CanEdit == true && Backend.Loaded && !Backend.Loading;
}
