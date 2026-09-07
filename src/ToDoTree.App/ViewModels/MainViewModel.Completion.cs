using ToDoTree.Core.Graph;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public event Action<CompletionImpact>? CompletionRequested;

    public void PlayCompletion(CompletionImpact impact)
    {
        if (impact.Sources.Count > 0) CompletionRequested?.Invoke(impact);
    }
}
