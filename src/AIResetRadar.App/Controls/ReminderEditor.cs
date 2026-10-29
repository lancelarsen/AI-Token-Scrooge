using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class ReminderEditor : VerticalStackLayout
{
    readonly List<TimedReminder> reminders;
    readonly VerticalStackLayout rows = new() { Spacing = 6 };
    readonly Label error = new() { TextColor = Color.FromArgb("#F05B69"), FontSize = 12, IsVisible = false };
    public event EventHandler? Changed;
    public IReadOnlyList<TimedReminder> Value => reminders.ToArray();
    public ReminderEditor(IReadOnlyList<TimedReminder> initial)
    {
        reminders = initial.ToList(); Spacing = 8;
        Children.Add(rows); Children.Add(error);
        var add = new ActionButton { Text = "Add Reminder", HorizontalOptions = LayoutOptions.Start };
        add.Clicked += (_, _) => { var item = new TimedReminder(Guid.NewGuid().ToString("N"), 1, "Days", ReminderSeverity.Info); reminders.Add(item); AddRow(item); Changed?.Invoke(this, EventArgs.Empty); };
        Children.Add(add);
        foreach (var item in reminders.ToArray()) AddRow(item);
    }
    void AddRow(TimedReminder item)
    {
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(80), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        var severity = new Picker { ItemsSource = new[] { "Info", "Warning", "Critical" }, SelectedIndex = (int)item.Severity, BackgroundColor = Color.FromArgb("#232429") };
        var amount = new Entry { Text = item.Amount.ToString(), Keyboard = Keyboard.Numeric, BackgroundColor = Color.FromArgb("#232429") };
        var unit = new Picker { ItemsSource = new[] { "Days", "Hours" }, SelectedIndex = item.Unit == "Days" ? 0 : 1, BackgroundColor = Color.FromArgb("#232429") };
        var remove = new ActionButton { Text = "Remove Reminder", IconOnly = true, WidthRequest = 44, Padding = 8 };
        SemanticProperties.SetDescription(severity, "Reminder Severity"); SemanticProperties.SetDescription(amount, "Time Before Event"); SemanticProperties.SetDescription(unit, "Days or Hours");
        row.Add(severity); row.Add(amount, 1); row.Add(unit, 2); row.Add(remove, 3); rows.Children.Add(row);
        void Save()
        {
            if (!int.TryParse(amount.Text, out var value) || value <= 0) { error.Text = "Enter a Positive Whole Number"; error.IsVisible = true; return; }
            error.IsVisible = false;
            var index = reminders.FindIndex(r => r.Id == item.Id); if (index < 0) return;
            reminders[index] = item with { Amount = value, Unit = unit.SelectedIndex == 0 ? "Days" : "Hours", Severity = (ReminderSeverity)Math.Max(0, severity.SelectedIndex) };
            severity.TextColor = Color.FromArgb(severity.SelectedIndex == 2 ? "#F05B69" : severity.SelectedIndex == 1 ? "#EAAA51" : "#C3ADF8");
            Changed?.Invoke(this, EventArgs.Empty);
        }
        severity.TextColor = Color.FromArgb(item.Severity == ReminderSeverity.Critical ? "#F05B69" : item.Severity == ReminderSeverity.Warning ? "#EAAA51" : "#C3ADF8");
        amount.TextChanged += (_, _) => Save(); severity.SelectedIndexChanged += (_, _) => Save(); unit.SelectedIndexChanged += (_, _) => Save();
        remove.Clicked += (_, _) => { reminders.RemoveAll(r => r.Id == item.Id); rows.Children.Remove(row); error.IsVisible = false; Changed?.Invoke(this, EventArgs.Empty); };
    }
}
