using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

sealed record ComplexModel(NavigationHistory<string> Navigation,
    NavigationPath<string, NavigationVisit<string>> Left, NavigationPath<string, NavigationVisit<string>> Right,
    bool Outer = false, bool Inner = false, bool DrawerOutside = true, bool Menu = false,
    DropdownState? Picker = null, bool Siblings = false, bool Nested = false, bool Block = false,
    bool ModalStack = false, string Draft = "", int BackCount = 0, bool HideLeft = false);
abstract partial record ComplexMsg
{
    public sealed record Move(HistoryRequest Request) : ComplexMsg;
    public sealed record Back(bool Right, BackRequest Request) : ComplexMsg;
    public sealed record Outer(bool Open) : ComplexMsg;
    public sealed record Inner(bool Open) : ComplexMsg;
    public sealed record Menu(bool Open) : ComplexMsg;
    public sealed record Picker(DropdownState State) : ComplexMsg;
    public sealed record Draft(string Text) : ComplexMsg;
}
sealed partial class ComplexApp : IKumiko<ComplexModel, ComplexMsg>
{
    public static ComplexModel Init => new(NavigationHistory<string>.Start("home").Push("workspace"),
        NavigationPath<string, NavigationVisit<string>>.Start("left-root").Push("left-detail"),
        NavigationPath<string, NavigationVisit<string>>.Start("right-root").Push("right-detail"));
    public static NavigationRoot<ComplexMsg> Root(ComplexModel model) => NavigationRoot<ComplexMsg>.Create(model.Navigation,
        "app", route => new ComplexScreen(new(model, route)).Map<ComplexMsg>(static message => message),
        static request => new ComplexMsg.Move(request));
    public static (ComplexModel, Cmd<ComplexMsg>) Update(ComplexModel model, ComplexMsg message) => (message switch
    {
        ComplexMsg.Move move => model with { Navigation = model.Navigation.Move(move.Request, "app") },
        ComplexMsg.Back back => Back(model, back),
        ComplexMsg.Outer outer => model with { Outer = outer.Open },
        ComplexMsg.Inner inner => model with { Inner = inner.Open },
        ComplexMsg.Menu menu => model with { Menu = menu.Open },
        ComplexMsg.Picker picker => model with { Picker = picker.State },
        ComplexMsg.Draft draft => model with { Draft = draft.Text },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<ComplexMsg>.None);

    static ComplexModel Back(ComplexModel model, ComplexMsg.Back back)
    {
        model = model with
        {
            BackCount = model.BackCount + 1
        };
        if (model.Block)
            return model;
        var path = back.Right ? model.Right : model.Left;
        var result = path.Back(back.Request, back.Right ? "right" : "left");
        return result is null ? model : back.Right ? model with
        {
            Right = result
        } : model with
        {
            Left = result
        };
    }
}
sealed record ComplexProps(ComplexModel State, string Route);
sealed class ComplexScreen(ComplexProps props) : Screen<ComplexProps, ComplexMsg>(props)
{
    protected override void Render()
    {
        using (C.Div(key: "page").SizeFull().Padding(Space.S4).Gap(Space.S3))
        {
            C.Text("Root: " + Model.Route, Typography.Title, key: "title");
            Ui.Button("Open outer", new ComplexMsg.Outer(true), key: "open-outer");
            if (Model.Route == "workspace")
                Ui.Child(new ComplexStacks(Model.State), "stacks", static message => message);
        }
        if (Model.State.Outer)
            Ui.Child(new ComplexOuter(Model.State), "outer-view", static message => message);
    }
}
sealed class ComplexStacks(ComplexModel props) : View<ComplexModel, ComplexMsg>(props)
{
    protected override void Render()
    {
        using var row = C.Div(key: "stacks").FillWidth().Grow().Row().Gap(Space.S3);
        using (C.Div(key: "left").Flex().FillHeight().Column().Display(Model.HideLeft ? DisplayMode.None : DisplayMode.Flex))
        {
            Ui.Stack(Model.Left, Model, static (ui, state, entry) =>
            {
                ui.Context.Text(entry.Route, key: "title");
                ui.TextField("draft", "Left draft", state.Draft, static text => new ComplexMsg.Draft(text));
                if (state.Nested)
                    ui.Child(new ComplexRight(state), "nested", static message => message);
            }, onBack: static request => new ComplexMsg.Back(false, request), stack: "left");
        }
        if (Model.Siblings)
        {
            using var right = C.Div(key: "right").Flex().FillHeight().Column();
            Ui.Child(new ComplexRight(Model), "sibling", static message => message);
        }
    }
}
sealed class ComplexRight(ComplexModel props) : View<ComplexModel, ComplexMsg>(props)
{
    protected override void Render()
    {
        using var host = C.Div(key: "host").FillWidth().Height(130).Column();
        Ui.Stack(Model.Right, 0, static (ui, _, entry) =>
        {
            ui.Context.Text(entry.Route, key: "title");
            ui.Button("Right action", new ComplexMsg.Inner(false), key: "action");
        }, onBack: static request => new ComplexMsg.Back(true, request), stack: "right");
    }
}
sealed class ComplexOuter(ComplexModel props) : View<ComplexModel, ComplexMsg>(props)
{
    protected override void Render()
    {
        var ui = Ui;
        using var outer = Model.DrawerOutside
            ? C.Kit.Drawer("Outer drawer", () => ui.Dispatch(new ComplexMsg.Outer(false)), new(Size: 500), key: "outer")
            : C.Kit.Dialog("Outer dialog", () => ui.Dispatch(new ComplexMsg.Outer(false)), key: "outer");
        Ui.Button("Open inner", new ComplexMsg.Inner(true), key: "open-inner");
        Ui.TextField("draft", "Modal draft", Model.Draft, static text => new ComplexMsg.Draft(text));
        Ui.Dropdown("picker", "Fruit", Model.Picker ?? new(), [new("apple", "Apple"), new("pear", "Pear")],
            static state => new ComplexMsg.Picker(state));
        Ui.Button("Open menu", new ComplexMsg.Menu(true), key: "open-menu");
        if (Model.Menu)
        {
            using var menu = C.Kit.Popover(null, () => ui.Dispatch(new ComplexMsg.Menu(false)), new(Name: "Inner menu"), key: "menu");
            Ui.Button("Menu action", new ComplexMsg.Menu(false), key: "action");
        }
        if (Model.ModalStack)
            Ui.Child(new ComplexRight(Model), "modal-stack", static message => message);
        if (Model.Inner)
            Ui.Child(new ComplexInner(Model.DrawerOutside), "inner-view", static message => message);
    }
}
sealed class ComplexInner(bool drawerOutside) : View<bool, ComplexMsg>(drawerOutside)
{
    protected override void Render()
    {
        var ui = Ui;
        using var inner = Model
            ? C.Kit.Dialog("Inner dialog", () => ui.Dispatch(new ComplexMsg.Inner(false)), key: "inner")
            : C.Kit.Drawer("Inner drawer", () => ui.Dispatch(new ComplexMsg.Inner(false)), new(Size: 360), key: "inner");
        Ui.Button("Inner action", new ComplexMsg.Inner(false), key: "action");
    }
}
