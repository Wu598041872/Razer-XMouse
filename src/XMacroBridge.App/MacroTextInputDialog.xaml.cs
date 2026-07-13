using System.Windows;
using System.Windows.Controls;

namespace XMacroBridge.App;

public partial class MacroTextInputDialog : Window
{
    public MacroTextInputDialog(string? initialText = null, bool isEdit = false)
    {
        InitializeComponent();
        DarkWindowAssist.Apply(this);
        if (isEdit)
        {
            Title = "修改 X-Mouse 宏文本";
            DialogHeading.Text = "修改 X-Mouse 宏文本";
            ImportButton.Content = "应用修改";
        }

        MacroTextBox.Text = initialText ?? string.Empty;
        Loaded += (_, _) =>
        {
            MacroTextBox.Focus();
            if (isEdit)
            {
                MacroTextBox.SelectAll();
            }
        };
    }

    public string MacroText => MacroTextBox.Text;

    private void MacroTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ImportButton is not null)
        {
            ImportButton.IsEnabled = !string.IsNullOrWhiteSpace(MacroTextBox.Text);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MacroTextBox.Text))
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
