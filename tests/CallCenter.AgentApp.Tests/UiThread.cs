using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// Runs a test on a thread of its own with a running dispatcher, as the app's
/// UI thread is. The view models marshal onto the dispatcher they are given,
/// and one that nothing pumps would hang the test instead of failing it.
/// </summary>
public static class UiThread
{
    public static void Run(Func<Dispatcher, Task> test)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await test(dispatcher);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The test did not finish on its UI thread");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    /// <summary>Waits, pumping, until <paramref name="condition"/> holds.</summary>
    public static async Task Until(Func<bool> condition, TimeSpan? limit = null)
    {
        var deadline = DateTime.UtcNow + (limit ?? TimeSpan.FromSeconds(5));

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition never became true");
            }

            await Task.Delay(10);
        }
    }
}
