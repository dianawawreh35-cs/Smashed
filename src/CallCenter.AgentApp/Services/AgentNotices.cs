namespace CallCenter.AgentApp.Services;

/// <summary>
/// A line across the top of the main window telling the agent something went
/// wrong, without stopping them (F-02).
/// </summary>
/// <remarks>
/// Not a message box. A modal box raised during a call sits over the pop-up and
/// can take Hang up away from the agent until they deal with it, and a fault
/// that repeats would stack one box per repeat. The bar shows the latest notice
/// and stays until dismissed or replaced.
/// </remarks>
public class AgentNotices
{
    /// <summary>
    /// Raised with a label key (A-80) when there is something to tell the
    /// agent. May be raised on any thread; the window marshals it.
    /// </summary>
    public event EventHandler<string>? Posted;

    /// <param name="labelKey">What to say, as a key into the language files.</param>
    public void Post(string labelKey) => Posted?.Invoke(this, labelKey);
}
