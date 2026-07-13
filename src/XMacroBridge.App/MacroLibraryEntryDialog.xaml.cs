using System.Windows;
using System.Windows.Controls;

namespace XMacroBridge.App;

public partial class MacroLibraryEntryDialog : Window
{
    private const string UngroupedLabel = "未分组";
    private readonly bool requiresText;

    public MacroLibraryEntryDialog(
        string heading,
        string initialName,
        IEnumerable<string> groups,
        string selectedGroup,
        bool showText,
        string? initialText = null)
    {
        InitializeComponent();
        DarkWindowAssist.Apply(this);
        Title = heading;
        DialogHeading.Text = heading;
        requiresText = showText;
        EntryNameTextBox.Text = initialName;
        GroupComboBox.Items.Add(UngroupedLabel);
        foreach (var group in groups.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            GroupComboBox.Items.Add(group);
        }

        GroupComboBox.SelectedItem = string.IsNullOrWhiteSpace(selectedGroup) ? UngroupedLabel : selectedGroup;
        if (GroupComboBox.SelectedIndex < 0)
        {
            GroupComboBox.SelectedIndex = 0;
        }

        MacroTextBox.Text = initialText ?? string.Empty;
        TextLabel.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
        MacroTextBox.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
        if (!showText)
        {
            Height = 280;
            MinHeight = 280;
        }

        Loaded += (_, _) =>
        {
            EntryNameTextBox.Focus();
            EntryNameTextBox.SelectAll();
            UpdateConfirmState();
        };
    }

    public string EntryName => EntryNameTextBox.Text.Trim();

    public string GroupName => string.Equals(GroupComboBox.SelectedItem as string, UngroupedLabel, StringComparison.Ordinal)
        ? string.Empty
        : GroupComboBox.SelectedItem as string ?? string.Empty;

    public string MacroText => MacroTextBox.Text;

    private void Input_Changed(object sender, EventArgs e) => UpdateConfirmState();

    private void UpdateConfirmState()
    {
        if (ConfirmButton is not null)
        {
            ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(EntryNameTextBox.Text) &&
                                      (!requiresText || !string.IsNullOrWhiteSpace(MacroTextBox.Text));
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmButton.IsEnabled)
        {
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
