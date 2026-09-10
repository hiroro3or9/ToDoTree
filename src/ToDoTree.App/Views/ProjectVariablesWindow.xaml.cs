using System.Windows;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Views;

/// <summary>
/// プロジェクトごとの変数を決める画面。
///
/// 開いているあいだ、実プロジェクトには触れない。保存を押したときだけ
/// <see cref="Definitions"/> と <see cref="Renames"/> を持ち帰る。
/// キャンセルと Esc は何も変えない。
/// </summary>
public partial class ProjectVariablesWindow : Window
{
    private readonly ProjectVariablesViewModel _draft;

    public ProjectVariablesWindow(TodoProject project, IEnumerable<ProjectVariable> definitions)
    {
        InitializeComponent();
        _draft = new ProjectVariablesViewModel(project, definitions);
        DataContext = _draft;
    }

    /// <summary>保存する定義。</summary>
    public IReadOnlyList<ProjectVariable> Definitions { get; private set; } = [];

    /// <summary>変更前の名前から変更後の名前への対応。</summary>
    public IReadOnlyDictionary<string, string> Renames { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!_draft.CanSave) return;

        Definitions = _draft.Definitions;
        Renames = _draft.Renames;
        DialogResult = true;
    }
}
