using System.Windows;
using System.Windows.Controls;

namespace XMacroBridge.App;

public partial class MacroRenameDialog : Window
{
    public MacroRenameDialog(string currentName)
    {
        InitializeComponent();
        DarkWindowAssist.Apply(this);
        MacroNameTextBox.Text = currentName ?? string.Empty;
        Loaded += (_, _) =>
        {
            MacroNameTextBox.Focus();
            MacroNameTextBox.SelectAll();
        };
    }

    public string MacroName => MacroNameTextBox.Text.Trim();

    private void MacroNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (RenameButton is not null)
        {
            RenameButton.IsEnabled = !string.IsNullOrWhiteSpace(MacroNameTextBox.Text);
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(MacroNameTextBox.Text))
        {
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
