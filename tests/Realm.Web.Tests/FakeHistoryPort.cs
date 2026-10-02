using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The browser end of <see cref="HistorySync"/>, faked: a model of the <c>setDepth(n)</c> contract of 03 section 3.7 and nothing else, so the sync itself runs as the product runs.
/// With <c>tokens</c> it pushes the missing entries or goes back by the surplus; without them it only records the target and never touches the history, so the Android Back
/// simply leaves the app. <see cref="Calls"/> counts every push and every go, so a test can assert that a transition pushed or popped nothing.
/// </summary>
internal sealed class FakeHistoryPort(bool tokens) : IHistoryPort
{
    /// <summary>The entries this document has pushed above its base entry.</summary>
    public int Entries { get; private set; }

    /// <summary>The depth the last <see cref="SetDepthAsync"/> recorded.</summary>
    public int Recorded { get; private set; }

    /// <summary>The number of <c>pushState</c> and <c>history.go</c> calls so far.</summary>
    public int Calls { get; private set; }

    /// <summary>The number of times the sync asked for a depth, whatever came of it.</summary>
    public int Requests { get; private set; }

    /// <summary>Whether the lease was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <summary>A fault to raise from the next <see cref="SetDepthAsync"/> calls (a dropped circuit), or null.</summary>
    public Exception? Fault { get; set; }

    /// <inheritdoc />
    public ValueTask SetDepthAsync(int depth)
    {
        Requests++;
        if (Fault is { } fault)
        {
            throw fault;
        }

        if (tokens && depth > Entries)
        {
            Calls += depth - Entries;
            Entries = depth;
        }
        else if (tokens && depth < Entries)
        {
            Calls++;
            Entries = depth;
        }

        Recorded = depth;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The Back gesture popped one entry (only with the tokens on; without them nothing was pushed to pop). Returns the depth the browser is at now, which is what
    /// <see cref="HistorySync.HandleBackAsync"/> is told.
    /// </summary>
    public int UserPop()
    {
        Assert.True(tokens);
        Assert.True(Entries > 0);
        Entries--;
        return Entries;
    }

    /// <summary>The Back gesture popped <paramref name="count"/> entries at once (a long press on Back). Returns the depth the browser is at now.</summary>
    public int UserPop(int count)
    {
        Assert.True(tokens);
        Assert.InRange(count, 1, Entries);
        Entries -= count;
        return Entries;
    }

    /// <summary>The Forward gesture re-entered <paramref name="count"/> entries. Returns the depth the browser is at now.</summary>
    public int UserForward(int count)
    {
        Assert.True(tokens);
        Entries += count;
        return Entries;
    }
}
