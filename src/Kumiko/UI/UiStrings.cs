namespace Kumiko.UI;

/// <summary>
/// Text built into Kumiko controls. Assign <see cref="Context.Strings"/> per window to translate or
/// rephrase it; composite strings use <see cref="string.Format(IFormatProvider, string, object[])"/>
/// placeholders.
/// </summary>
public sealed partial record UiStrings
{
    /// <summary>The built-in English text.</summary>
    public static UiStrings English { get; } = new();

    public string PreviousMonth { get; init; } = "Previous month";
    public string NextMonth { get; init; } = "Next month";
    public string PickDate { get; init; } = "Pick a date";
    public string PickTime { get; init; } = "Pick a time";
    public string Clear { get; init; } = "Clear";
    /// <summary>Accessible name of a number input's step-up button; {0} is the field label.</summary>
    public string Increase { get; init; } = "Increase {0}";
    /// <summary>Accessible name of a number input's step-down button; {0} is the field label.</summary>
    public string Decrease { get; init; } = "Decrease {0}";
    public string InvalidNumber { get; init; } = "Enter a number.";
    /// <summary>{0} and {1} are the formatted minimum and maximum.</summary>
    public string NumberRange { get; init; } = "Enter a value from {0} to {1}.";
    /// <summary>{0} is the formatted minimum.</summary>
    public string NumberMinimum { get; init; } = "Enter {0} or more.";
    /// <summary>{0} is the formatted maximum.</summary>
    public string NumberMaximum { get; init; } = "Enter {0} or less.";
    public string InvalidValue { get; init; } = "Enter a valid value.";
    public string NoRecords { get; init; } = "No records found";
    public string NoRecordsHint { get; init; } = "Adjust the filters or load records.";
    /// <summary>Accessible name of a grid row; {0} is the record ID.</summary>
    public string Record { get; init; } = "Record {0}";
    /// <summary>A grid group row; {0} is the column header, {1} the value and {2} the row count.</summary>
    public string Group { get; init; } = "{0}: {1} ({2})";
    public string DataGrid { get; init; } = "Data grid";
    /// <summary>A dropdown waiting for search results.</summary>
    public string Searching { get; init; } = "Searching…";
    /// <summary>Accessible name of the button that clears a multiple-selection dropdown.</summary>
    public string ClearSelection { get; init; } = "Clear selection";
    /// <summary>The number of chosen items in a multiple-selection dropdown; {0} is the count.</summary>
    public string SelectedCount { get; init; } = "{0} selected";
    /// <summary>The command that returns to the previous page of a navigation stack.</summary>
    public string Back { get; init; } = "Back";
}
