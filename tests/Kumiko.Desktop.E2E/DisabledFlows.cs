using Kumiko.UI;

static class DisabledFlows
{
    public static void Run(Action<bool, string> check)
    {
        var picker = new SearchDropdown();
        var menu = new ContextMenu();
        int invoked = 0;
        Choice[] choices = [new("blocked", "Unavailable", Disabled: true), new("available", "Available")];
        void View(Context c)
        {
            using var root = c.Div(key: "disabled-flow").SizeFull().Padding(24).Gap(16);
            picker.Draw(c, "Choice", choices, "picker");
            if (c.Button("Actions", key: "actions").Clicked)
                menu.Open(new(24, 100), c.Focus,
                [
                    new("blocked", "Unavailable action", () => throw new InvalidOperationException("Disabled action ran"), Disabled: true),
                    new("available", "Available action", () => invoked++)
                ]);
            menu.Draw(c, "menu");
        }
        using var ui = new Tester(View);
        ui.Get(Role.ComboBox, "Choice").Click();
        var disabled = ui.Get(Role.Option, "Unavailable").Rect;
        ui.Click(disabled.X + 10, disabled.Y + 10);
        check(picker.SelectedId is null && picker.IsOpen, "disabled dropdown choices reject pointer activation");
        ui.Key(Key.Down);
        ui.Key(Key.Enter);
        check(picker.SelectedId == "available" && !picker.IsOpen, "keyboard search skips disabled choices");
        ui.Get(Role.Button, "Actions").Click();
        ui.Key(Key.Enter);
        check(invoked == 1 && !menu.IsOpen, "menus initially focus the first enabled action");
        picker.Disabled = true;
        ui.Settle();
        var trigger = ui.Get(Role.ComboBox, "Choice").Rect;
        ui.Click(trigger.X + 10, trigger.Y + 10);
        check(!picker.IsOpen, "a disabled combobox cannot open");
    }
}
