namespace CvPlatform.Web.State;

public sealed class ProfileHeaderState
{
    public event Func<Task>? Changed;

    public async Task NotifyChangedAsync()
    {
        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }
}
