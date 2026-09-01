using System.Windows;
using System.Windows.Input;

namespace ToDoTree.App.Views;

/// <summary>箇条書きをまとめて貼り付けるための入力欄。</summary>
public partial class OutlineInputWindow : Window
{
    public OutlineInputWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus();
    }

    /// <summary>入力されたアウトライン。</summary>
    public string OutlineText => InputBox.Text;

    /// <summary>見出しやボタンの文言を、用途に合わせて差し替える。</summary>
    public void Configure(string windowTitle, string header, string hint, string buttonLabel)
    {
        Title = windowTitle;
        HeaderText.Text = header;
        HintText.Text = hint;
        ImportButton.Content = buttonLabel;
    }

    private void OnImportClick(object sender, RoutedEventArgs e) => Accept();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Accept();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text))
        {
            DialogResult = false;
            return;
        }

        DialogResult = true;
    }
}
