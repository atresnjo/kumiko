namespace CRMLite;

public abstract partial record CrmMsg
{
    public sealed record Authentication(AuthMsg Message) : CrmMsg;
}

public abstract partial record AuthMsg
{
    public sealed record SetAuthEmail(string Email) : AuthMsg;
    public sealed record SignIn : AuthMsg;
    public sealed record SignedIn(int Request, AuthSession Session) : AuthMsg;
    public sealed record AuthFailed(int Request, string Error) : AuthMsg;
    public sealed record SignOut : AuthMsg;
    public sealed record SignedOut(int Request) : AuthMsg;
    public sealed record RefreshWorkspace : AuthMsg;
    public sealed record WorkspaceLoaded(int Request, WorkspaceData Data) : AuthMsg;
    public sealed record WorkspaceFailed(int Request, string Error) : AuthMsg;
    public sealed record WorkspaceSaved(int Request, long Revision) : AuthMsg;
    public sealed record WorkspaceSaveFailed(int Request, long Revision, string Error) : AuthMsg;
    public sealed record RetrySave : AuthMsg;
    public sealed record SessionExpired(int Request) : AuthMsg;
}
