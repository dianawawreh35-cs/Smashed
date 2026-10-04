namespace CallCenter.AgentApp.Services.Websites;

/// <summary>
/// The website tab that opens the caller's cart (A-85, A-88), while the
/// Websites screen of a signed-in agent is there to hold it.
/// </summary>
/// <remarks>
/// One for the process, as <see cref="Calls.PosCart"/> is; the screen that has
/// the tab is made at each sign-in and goes at sign-out, so it registers here
/// and leaves again. With nobody registered, the cart opens in the default
/// browser as it did before the tabs.
/// </remarks>
public sealed class CartTab
{
    /// <summary>Opens the cart for a caller's number, in its local form.</summary>
    public interface ITarget
    {
        /// <summary>False when no tab takes the cart, or the engine is missing.</summary>
        bool TryOpen(string localNumber);
    }

    public ITarget? Current { get; private set; }

    public void Register(ITarget target) => Current = target;

    public void Unregister(ITarget target)
    {
        if (ReferenceEquals(Current, target))
        {
            Current = null;
        }
    }
}
