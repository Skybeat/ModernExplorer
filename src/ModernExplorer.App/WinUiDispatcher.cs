using Microsoft.UI.Dispatching;
using ModernExplorer.Core.ViewModels;

namespace ModernExplorer.App;

public sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;

    public void Post(Action action)
    {
        if (queue.HasThreadAccess)
            action();
        else
            queue.TryEnqueue(() => action());
    }
}

