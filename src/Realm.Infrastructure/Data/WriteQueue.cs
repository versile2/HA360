namespace Realm.Infrastructure.Data;

/// <summary>
/// The bounded queue behind the single writer (02 section 7.3, 03 section 2.12). Many producers add, one consumer takes everything at once.
/// When the queue is full the <c>track = 0</c> diagnostic rows give way first: a new diagnostic row is dropped, and a new row that matters evicts the
/// oldest diagnostic row. Only when no diagnostic row is left is a new row dropped. Every drop is counted.
/// </summary>
internal sealed class WriteQueue
{
    private readonly object _gate = new();
    private readonly LinkedList<WriteCommand> _items = new();
    private readonly int _capacity;
    private long _dropped;
    private bool _closed;

    public WriteQueue(int capacity)
    {
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Adds a command; false when it was not accepted (dropped for lack of room, or the queue is closed). Either way a refusal counts as a drop.</summary>
    public bool TryAdd(WriteCommand command)
    {
        lock (_gate)
        {
            if (_closed)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }

            if (_items.Count >= _capacity && !command.MustAccept)
            {
                if (command.IsDiagnostic || !EvictOldestDiagnostic())
                {
                    Interlocked.Increment(ref _dropped);
                    return false;
                }
            }
            else if (_items.Count >= _capacity)
            {
                EvictOldestDiagnostic();
            }

            _items.AddLast(command);
            return true;
        }
    }

    /// <summary>Removes and returns everything queued, oldest first.</summary>
    public List<WriteCommand> TakeAll()
    {
        lock (_gate)
        {
            var all = new List<WriteCommand>(_items);
            _items.Clear();
            return all;
        }
    }

    /// <summary>Puts commands that could not be written back at the front, in their original order. They were counted when first accepted, so the bound is not applied.</summary>
    public void PutBack(IReadOnlyList<WriteCommand> commands, int from)
    {
        lock (_gate)
        {
            for (var i = commands.Count - 1; i >= from; i--)
            {
                _items.AddFirst(commands[i]);
            }
        }
    }

    /// <summary>Refuses every command from now on.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
        }
    }

    // Caller holds the lock. Scans from the oldest: the queue is only full in a failure, so the scan is rare.
    private bool EvictOldestDiagnostic()
    {
        for (var node = _items.First; node is not null; node = node.Next)
        {
            if (node.Value.IsDiagnostic)
            {
                _items.Remove(node);
                Interlocked.Increment(ref _dropped);
                return true;
            }
        }

        return false;
    }
}
