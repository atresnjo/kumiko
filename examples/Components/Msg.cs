namespace Components;

public abstract partial record ShowcaseMsg
{
    public sealed record SetDark(bool On) : ShowcaseMsg;
    public sealed record SetAccent(string Accent) : ShowcaseMsg;
    public sealed record SetRadius(float Radius) : ShowcaseMsg;
    public sealed record Navigate(string Section) : ShowcaseMsg;
    public sealed record SelectTab(string Tab) : ShowcaseMsg;
    public sealed record SetPeriod(string Period) : ShowcaseMsg;
    public sealed record SetFilter(string Filter) : ShowcaseMsg;
    public sealed record Click(string Label) : ShowcaseMsg;
    public sealed record SetName(string Text) : ShowcaseMsg;
    public sealed record SetEmail(string Text) : ShowcaseMsg;
    public sealed record SetTerms(bool On) : ShowcaseMsg;
    public sealed record SetMarketing(bool On) : ShowcaseMsg;
    public sealed record SetAirplane(bool On) : ShowcaseMsg;
    public sealed record SetWifi(bool On) : ShowcaseMsg;
    public sealed record SetPlan(string Plan) : ShowcaseMsg;
    public sealed record SetVolume(float Value) : ShowcaseMsg;
    public sealed record SetLayout(string Layout) : ShowcaseMsg;
    public sealed record Expand(string? Id) : ShowcaseMsg;
    public sealed record OpenDialog : ShowcaseMsg;
    public sealed record CloseDialog : ShowcaseMsg;
    public sealed record ConfirmDelete : ShowcaseMsg;
    public sealed record SetPage(int Page) : ShowcaseMsg;
    public sealed record SetRating(int Stars) : ShowcaseMsg;
    public sealed record SetAccountOpen(bool Open) : ShowcaseMsg;
    public sealed record AccountAction(string Label) : ShowcaseMsg;
    public sealed record SetSearchOpen(bool Open) : ShowcaseMsg;
    public sealed record SetQuery(string Text) : ShowcaseMsg;
    public sealed record SearchResult(string Section) : ShowcaseMsg;
    public sealed record SetDue(DateOnly? Date) : ShowcaseMsg;
    public sealed record SetDueOpen(bool Open) : ShowcaseMsg;
    public sealed record SetStart(TimeOnly? Time) : ShowcaseMsg;
    public sealed record SetStartOpen(bool Open) : ShowcaseMsg;
    public sealed record SetBudget(double? Amount) : ShowcaseMsg;
    public sealed record SetFolder(string Id, bool Open) : ShowcaseMsg;
    public sealed record SelectFile(string Id) : ShowcaseMsg;
    public sealed record SetPane(float Width) : ShowcaseMsg;
}
