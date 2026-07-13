using System.Globalization;
using System.Windows;
using System.Windows.Input;
using XMacroBridge.Core.Models;
using XMacroBridge.Presentation.Workspace;
using MacroMouseButton = XMacroBridge.Core.Models.MouseButton;

namespace XMacroBridge.App;

public partial class EventEditDialog : Window
{
    private readonly MacroEvent originalEvent;
    private int selectedVirtualKey;
    private readonly InputTransitionOption[] transitionOptions =
    [
        new("按下", InputTransition.Down),
        new("松开", InputTransition.Up),
    ];
    private readonly MouseButtonOption[] mouseButtonOptions =
    [
        new("左键", MacroMouseButton.Left),
        new("右键", MacroMouseButton.Right),
        new("中键", MacroMouseButton.Middle),
        new("侧键 1", MacroMouseButton.XButton1),
        new("侧键 2", MacroMouseButton.XButton2),
        new("滚轮向上", MacroMouseButton.WheelUp),
        new("滚轮向下", MacroMouseButton.WheelDown),
        new("滚轮左倾", MacroMouseButton.TiltLeft),
        new("滚轮右倾", MacroMouseButton.TiltRight),
    ];

    public EventEditDialog(MacroEvent macroEvent)
    {
        originalEvent = macroEvent ?? throw new ArgumentNullException(nameof(macroEvent));
        InitializeComponent();
        DarkWindowAssist.Apply(this);
        KeyTransitionComboBox.ItemsSource = transitionOptions;
        MouseTransitionComboBox.ItemsSource = transitionOptions;
        MouseButtonComboBox.ItemsSource = mouseButtonOptions;
        PopulateEditor();
    }

    public MacroEvent? EditedEvent { get; private set; }

    public int CapturedVirtualKey => selectedVirtualKey;

    private void PopulateEditor()
    {
        switch (originalEvent)
        {
            case DelayMacroEvent delay:
                EventSummaryText.Text = $"事件 {delay.Sequence} · 固定延时";
                DelayTextBox.Text = delay.Milliseconds.ToString(CultureInfo.InvariantCulture);
                DelayEditor.Visibility = Visibility.Visible;
                DelayTextBox.Focus();
                DelayTextBox.SelectAll();
                break;
            case KeyMacroEvent key:
                EventSummaryText.Text = $"事件 {key.Sequence} · 键盘事件";
                selectedVirtualKey = key.VirtualKey;
                VirtualKeyTextBox.Text = InputEventDisplayFormatter.FormatVirtualKey(key.VirtualKey, key.DisplayName);
                KeyTransitionComboBox.SelectedItem = transitionOptions.Single(item => item.Transition == key.Transition);
                ExtendedKeyCheckBox.IsChecked = key.IsExtended;
                KeyboardEditor.Visibility = Visibility.Visible;
                VirtualKeyTextBox.Focus();
                VirtualKeyTextBox.SelectAll();
                break;
            case MouseMacroEvent mouse:
                EventSummaryText.Text = $"事件 {mouse.Sequence} · 鼠标事件";
                MouseButtonComboBox.SelectedItem = mouseButtonOptions.Single(item => item.Button == mouse.Button);
                MouseTransitionComboBox.SelectedItem = transitionOptions.Single(item => item.Transition == mouse.Transition);
                MouseEditor.Visibility = Visibility.Visible;
                MouseButtonComboBox.Focus();
                break;
            default:
                throw new ArgumentException("该事件类型不支持编辑。", nameof(originalEvent));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ClearValidation();
        switch (originalEvent)
        {
            case DelayMacroEvent delay:
                if (!long.TryParse(DelayTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) || milliseconds < 0)
                {
                    ShowValidation("延时必须是大于或等于 0 的整数毫秒。", DelayTextBox);
                    return;
                }

                EditedEvent = delay with { Milliseconds = milliseconds };
                break;
            case KeyMacroEvent key:
                if (selectedVirtualKey is < 1 or > 255)
                {
                    ShowValidation("请点击按键输入框，然后按下要使用的键。", VirtualKeyTextBox);
                    return;
                }

                if (KeyTransitionComboBox.SelectedItem is not InputTransitionOption keyTransition)
                {
                    ShowValidation("请选择键盘事件状态。", KeyTransitionComboBox);
                    return;
                }

                EditedEvent = key with
                {
                    VirtualKey = selectedVirtualKey,
                    Transition = keyTransition.Transition,
                    IsExtended = ExtendedKeyCheckBox.IsChecked == true,
                };
                break;
            case MouseMacroEvent mouse:
                if (MouseButtonComboBox.SelectedItem is not MouseButtonOption mouseButton ||
                    MouseTransitionComboBox.SelectedItem is not InputTransitionOption mouseTransition)
                {
                    ShowValidation("请选择鼠标按钮和事件状态。", MouseButtonComboBox);
                    return;
                }

                EditedEvent = mouse with { Button = mouseButton.Button, Transition = mouseTransition.Transition };
                break;
        }

        DialogResult = true;
    }

    private void VirtualKeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!KeyboardKeyCapture.TryCapture(e, out var captured))
        {
            return;
        }

        selectedVirtualKey = captured.VirtualKey;
        VirtualKeyTextBox.Text = captured.DisplayName;
        ExtendedKeyCheckBox.IsChecked = captured.IsExtended;
        ClearValidation();
        e.Handled = true;
    }

    private void ShowValidation(string message, IInputElement focusTarget)
    {
        ValidationText.Text = message;
        ValidationPanel.Visibility = Visibility.Visible;
        focusTarget.Focus();
    }

    private void ClearValidation()
    {
        ValidationText.Text = string.Empty;
        ValidationPanel.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
