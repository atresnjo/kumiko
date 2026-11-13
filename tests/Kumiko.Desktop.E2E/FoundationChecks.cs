using Kumiko.UI;

static class FoundationChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var text = new TextBuffer("مرحبا שלום नमस्ते café 👩‍💻");
        var password = new TextBuffer("private-password");
        float amount = 20;
        bool enabled = false;
        void View(Context c)
        {
            using var root = c.Div(key: "foundation").SizeFull().Padding(Space.S5).Gap(Space.S3);
            c.Heading("Desktop foundations", key: "title");
            c.Text(text.Text, new(Size: 24, Wrap: true), key: "shaped");
            c.TextField(text, new(Width: 540, Name: "Multilingual text"), key: "text");
            c.TextField(password, new(Password: true, Name: "Password"), key: "password");
            c.Slider(ref amount, new(Min: 10, Max: 80, Width: 320), key: "range", name: "Amount");
            c.Checkbox("Enabled", ref enabled, key: "enabled");
            c.Button("Disabled", new(Disabled: true), key: "disabled");
        }
        using var ui = new Tester(View, new(Width: 640, Height: 420));
        var field = ui.Context.Accessibility.Single(n => n.Name == "Multilingual text");
        check(ui.Context.PerformAccessibilityAction(field.Id, new(AccessibilityAction.SetValue, Text: "日本語 مرحبا")), "accessibility can edit a text field");
        ui.Settle();
        check(text.Text == "日本語 مرحبا", "accessibility edit reaches the displayed buffer");
        check(ui.Context.PerformAccessibilityAction(field.Id, new(AccessibilityAction.SetSelection, Start: 0, End: 3)), "accessibility can select text");
        ui.Settle();
        var selection = ui.Context.Accessibility.Single(n => n.Id == field.Id).Properties;
        check(selection?.SelectionStart == 0 && selection.SelectionEnd == 3, "selection is exposed in immutable semantics");
        var secret = ui.Context.Accessibility.Single(n => n.Name == "Password");
        check(secret.Value is null && secret.Properties?.Password == true, "password text is excluded from accessible values");
        var slider = ui.Context.Accessibility.Single(n => n.Name == "Amount");
        check(ui.Context.PerformAccessibilityAction(slider.Id, new(AccessibilityAction.SetRange, Number: 65)), "accessibility can change a range");
        ui.Settle();
        check(amount == 65, "range action applies on the UI input pipeline");
        var disabled = ui.Context.Accessibility.Single(n => n.Name == "Disabled" && n.Role == Role.Button);
        check(!ui.Context.PerformAccessibilityAction(disabled.Id, new(AccessibilityAction.Invoke)), "disabled elements reject accessibility actions");
        var toggle = ui.Context.Accessibility.Single(n => n.Name == "Enabled" && n.Role == Role.Checkbox);
        check(ui.Context.PerformAccessibilityAction(toggle.Id, new(AccessibilityAction.Toggle)), "accessibility can toggle a checkbox");
        ui.Settle();
        check(enabled, "native toggle changes application state");
        ui.Screenshot(Path.Combine(output, "text-and-accessibility.png"));
        File.WriteAllText(Path.Combine(output, "text-and-accessibility.txt"), ui.Dump());
    }
}
