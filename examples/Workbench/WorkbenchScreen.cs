using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Examples.Workbench;

sealed class WorkbenchScreen(WorkbenchModel model) : Screen<WorkbenchModel, WorkbenchMsg>(model)
{
    protected override void Render()
    {
        var c = C;
        var m = Model;
        var send = Ui.BindDispatcher();
        var Grid = WorkbenchApp.Grid(m, send);
        var SupplierPicker = new SearchDropdown
        {
            Placeholder = "All suppliers"
        };
        SupplierPicker.Restore(m.Supplier ?? new());
        SupplierPicker.StateChanged += state => send(new WorkbenchMsg.Supplier(state));
        var theme = WorkbenchApp.ThemeFor(m.Theme);
        c.Theme = theme;
        var t = theme;
        using var root = c.Div(key: "workbench").SizeFull().Padding(t.Space.S5).Gap(t.Space.S4).Background(t.Background);
        using (c.Div(key: "heading").Row().FillWidth().ItemsCenter().Gap(t.Space.S2))
        {
            using (c.Div(key: "identity").Grow().Gap(t.Space.S1))
            {
                c.Heading("Inventory", key: "title");
                c.Text("Review stock, update records, and keep suppliers in view.", t.Typography.Body with
                {
                    Color = t.Muted
                }, key: "description");
            }

            if (c.Button("Light", new(ButtonVariant.Ghost), key: "light").Clicked)
                send(new WorkbenchMsg.Theme(WorkbenchTheme.Light));
            if (c.Button("Dark", new(ButtonVariant.Ghost), key: "dark").Clicked)
                send(new WorkbenchMsg.Theme(WorkbenchTheme.Dark));
            if (c.Button("Violet", new(ButtonVariant.Ghost), key: "violet").Clicked)
                send(new WorkbenchMsg.Theme(WorkbenchTheme.Violet));
        }

        using (c.Div(key: "toolbar").Row().FillWidth().ItemsCenter().Gap(t.Space.S3))
        {
            Ui.TextField(key: "search", label: "Search inventory", value: m.Search, onChange: static text => new WorkbenchMsg.Search(text), options: new(Placeholder: "Search inventory…", Width: 260, Name: "Search inventory"));
            SupplierPicker.Draw(c, "Supplier", InventoryWorkbench.Suppliers, "supplier", 240, "No matching suppliers");
            c.Spacer(key: "space");
            if (m.IsLoading)
            {
                c.Text("Loading…", t.Typography.Body with
                {
                    Color = t.Muted
                }, key: "loading");
                if (c.Button("Cancel", key: "cancel").Clicked)
                    send(new WorkbenchMsg.Cancel());
            }
            else if (c.Button("Load inventory", new(ButtonVariant.Primary), key: "load").Clicked)
                send(new WorkbenchMsg.Load());
        }

        if (m.LoadError is not null)
        {
            using var error = c.Div(key: "load-error").Row().FillWidth().ItemsCenter().Gap(t.Space.S3).Padding(t.Space.S3).Background(t.ErrorSurface).Rounded(t.Radius.Md).Semantics(Role.Alert, m.LoadError);
            c.Text(m.LoadError, t.Typography.Body with
            {
                Color = t.Danger
            }, key: "message");
            if (c.Button("Retry", key: "retry").Clicked)
                send(new WorkbenchMsg.Load());
        }

        using (c.Div(key: "records").FillWidth().Grow())
            Grid.Draw(c, "inventory-grid");
        using (c.Div(key: "footer").Row().FillWidth().ItemsCenter().Gap(t.Space.S3))
        {
            c.Text($"{Grid.Count:N0} records", t.Typography.Caption with
            {
                Color = t.Muted
            }, key: "count");
            c.Spacer(key: "space");
            c.Text("F2 edit · Enter save · Esc cancel · Shift+F10 actions", t.Typography.Caption with
            {
                Color = t.Muted
            }, key: "shortcuts");
        }

        c.Text(m.Notice, t.Typography.Caption with
        {
            Color = t.Muted
        }, key: "notice");
        var gridState = Grid.Capture();
        var supplierState = SupplierPicker.Capture();
        if (gridState != m.Grid)
            send(new WorkbenchMsg.Grid(gridState));
        if (supplierState != m.Supplier)
            send(new WorkbenchMsg.Supplier(supplierState));
    }
}
