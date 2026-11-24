using System;
using System.Collections.Generic;

namespace RevitWorkerPool.Supervisor;

/// <summary>
/// Per-run fair round-robin queue that prevents head-of-line starvation when a single-fixture run
/// arrives concurrently with a multi-shard run, while allowing solo multi-shard runs to burst
/// across all active worker slots. Caller must synchronize access externally.
/// </summary>
public sealed class FairRoundRobinScheduler<TItem> where TItem : class
{
    private readonly Dictionary<Guid, RunBucket> _runsById = new();
    private long _sequence;
    private int _pendingCount;

    public int PendingCount => _pendingCount;

    public int ActiveRunCount
    {
        get
        {
            int count = 0;
            foreach (RunBucket bucket in _runsById.Values)
            {
                if (bucket.Items.Count > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Registers an active caller run so its dispatch history is preserved while shards are in-flight.
    /// </summary>
    public void RegisterRun(Guid runId)
    {
        if (!_runsById.TryGetValue(runId, out RunBucket? bucket))
        {
            bucket = new RunBucket(runId, ++_sequence);
            _runsById[runId] = bucket;
        }

        bucket.IsRegistered = true;
    }

    /// <summary>
    /// Marks a caller run completed and removes its tracking state once its pending items are empty.
    /// </summary>
    public void UnregisterRun(Guid runId)
    {
        if (_runsById.TryGetValue(runId, out RunBucket? bucket))
        {
            bucket.IsRegistered = false;
            CleanupBucketIfEmpty(bucket);
        }
    }

    /// <summary>
    /// Records an immediate dispatch (such as a ready-standby fast path) for <paramref name="runId"/>
    /// so newly arriving concurrent runs receive fair priority ahead of trailing queued shards.
    /// </summary>
    public void RecordDispatch(Guid runId)
    {
        if (!_runsById.TryGetValue(runId, out RunBucket? bucket))
        {
            bucket = new RunBucket(runId, ++_sequence);
            _runsById[runId] = bucket;
        }

        bucket.LastServedSequence = ++_sequence;
    }

    /// <summary>
    /// Appends a queued shard for <paramref name="runId"/> targeting Revit <paramref name="version"/>.
    /// </summary>
    public void Enqueue(Guid runId, int version, TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!_runsById.TryGetValue(runId, out RunBucket? bucket))
        {
            bucket = new RunBucket(runId, ++_sequence);
            _runsById[runId] = bucket;
        }

        bucket.Items.Add(new QueuedEntry(version, item));
        _pendingCount++;
    }

    /// <summary>
    /// Removes a specific queued item belonging to <paramref name="runId"/> (for cancellation/timeout).
    /// </summary>
    public bool Remove(Guid runId, TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_runsById.TryGetValue(runId, out RunBucket? bucket))
        {
            for (int i = 0; i < bucket.Items.Count; i++)
            {
                if (IsSameItem(bucket.Items[i].Item, item))
                {
                    bucket.Items.RemoveAt(i);
                    _pendingCount--;
                    CleanupBucketIfEmpty(bucket);
                    return true;
                }
            }

            return false;
        }

        return Remove(item);
    }

    /// <summary>
    /// Removes a specific queued item across all runs.
    /// </summary>
    public bool Remove(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (RunBucket bucket in _runsById.Values)
        {
            for (int i = 0; i < bucket.Items.Count; i++)
            {
                if (IsSameItem(bucket.Items[i].Item, item))
                {
                    bucket.Items.RemoveAt(i);
                    _pendingCount--;
                    CleanupBucketIfEmpty(bucket);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if any run currently has a queued item targeting <paramref name="version"/>.
    /// </summary>
    public bool HasPendingForVersion(int version)
    {
        foreach (RunBucket bucket in _runsById.Values)
        {
            foreach (QueuedEntry entry in bucket.Items)
            {
                if (entry.Version == version)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the distinct set of Revit versions currently queued across all runs.
    /// </summary>
    public HashSet<int> GetPendingVersions()
    {
        var versions = new HashSet<int>();
        foreach (RunBucket bucket in _runsById.Values)
        {
            foreach (QueuedEntry entry in bucket.Items)
            {
                versions.Add(entry.Version);
            }
        }

        return versions;
    }

    /// <summary>
    /// Peeks the next queued item in fair round-robin order without mutating turn state.
    /// </summary>
    public bool TryPeekNextBootCandidate(out int version, out TItem? item)
    {
        RunBucket? best = null;
        foreach (RunBucket bucket in _runsById.Values)
        {
            if (bucket.Items.Count == 0)
            {
                continue;
            }

            if (best is null || ComparePriority(bucket, best) < 0)
            {
                best = bucket;
            }
        }

        if (best is null)
        {
            version = 0;
            item = null;
            return false;
        }

        version = best.Items[0].Version;
        item = best.Items[0].Item;
        return true;
    }

    /// <summary>
    /// Simulates a full fair round-robin pass across all currently queued items without mutating turn state,
    /// allowing multi-slot boot scheduling to allocate capacity fairly across active runs.
    /// </summary>
    public IReadOnlyList<(Guid RunId, int Version, TItem Item)> GetRoundRobinPlan()
    {
        if (_pendingCount == 0)
        {
            return Array.Empty<(Guid, int, TItem)>();
        }

        var cursors = new List<SimulatedRunCursor>();
        foreach (RunBucket bucket in _runsById.Values)
        {
            if (bucket.Items.Count > 0)
            {
                cursors.Add(new SimulatedRunCursor(
                    bucket,
                    bucket.LastServedSequence,
                    bucket.ArrivalSequence));
            }
        }

        var plan = new List<(Guid RunId, int Version, TItem Item)>(_pendingCount);
        long simSeq = _sequence;

        while (true)
        {
            SimulatedRunCursor? best = null;
            foreach (SimulatedRunCursor cursor in cursors)
            {
                if (cursor.NextIndex >= cursor.Bucket.Items.Count)
                {
                    continue;
                }

                if (best is null ||
                    cursor.SimLastServed < best.SimLastServed ||
                    (cursor.SimLastServed == best.SimLastServed && cursor.Arrival < best.Arrival))
                {
                    best = cursor;
                }
            }

            if (best is null)
            {
                break;
            }

            QueuedEntry entry = best.Bucket.Items[best.NextIndex++];
            plan.Add((best.Bucket.RunId, entry.Version, entry.Item));
            best.SimLastServed = ++simSeq;
        }

        return plan;
    }

    /// <summary>
    /// Dequeues the next queued item matching <paramref name="version"/> in fair round-robin order.
    /// </summary>
    public bool TryDequeueForVersion(int version, out Guid runId, out TItem? item)
    {
        return TryDequeueMatching(v => v == version, out runId, out item);
    }

    /// <summary>
    /// Dequeues the next queued item whose version satisfies <paramref name="versionPredicate"/>
    /// in fair round-robin order and advances the served run's turn sequence.
    /// </summary>
    public bool TryDequeueMatching(Func<int, bool> versionPredicate, out Guid runId, out TItem? item)
    {
        ArgumentNullException.ThrowIfNull(versionPredicate);

        if (_pendingCount == 0)
        {
            runId = Guid.Empty;
            item = null;
            return false;
        }

        var orderedBuckets = new List<RunBucket>();
        foreach (RunBucket bucket in _runsById.Values)
        {
            if (bucket.Items.Count > 0)
            {
                orderedBuckets.Add(bucket);
            }
        }

        orderedBuckets.Sort(ComparePriority);

        foreach (RunBucket bucket in orderedBuckets)
        {
            for (int i = 0; i < bucket.Items.Count; i++)
            {
                QueuedEntry entry = bucket.Items[i];
                if (versionPredicate(entry.Version))
                {
                    bucket.Items.RemoveAt(i);
                    _pendingCount--;
                    bucket.LastServedSequence = ++_sequence;
                    runId = bucket.RunId;
                    item = entry.Item;
                    CleanupBucketIfEmpty(bucket);
                    return true;
                }
            }
        }

        runId = Guid.Empty;
        item = null;
        return false;
    }

    /// <summary>
    /// Clears all queued items and run tracking state (used on supervisor drain) and returns removed items.
    /// </summary>
    public IReadOnlyList<TItem> ClearAll()
    {
        var drained = new List<TItem>(_pendingCount);
        foreach (RunBucket bucket in _runsById.Values)
        {
            foreach (QueuedEntry entry in bucket.Items)
            {
                drained.Add(entry.Item);
            }
        }

        _runsById.Clear();
        _pendingCount = 0;
        return drained;
    }

    private void CleanupBucketIfEmpty(RunBucket bucket)
    {
        if (bucket.Items.Count == 0 && !bucket.IsRegistered)
        {
            _runsById.Remove(bucket.RunId);
        }
    }

    private static int ComparePriority(RunBucket left, RunBucket right)
    {
        int servedCmp = left.LastServedSequence.CompareTo(right.LastServedSequence);
        if (servedCmp != 0)
        {
            return servedCmp;
        }

        return left.ArrivalSequence.CompareTo(right.ArrivalSequence);
    }

    private static bool IsSameItem(TItem candidate, TItem target)
    {
        return ReferenceEquals(candidate, target) ||
               EqualityComparer<TItem>.Default.Equals(candidate, target);
    }

    private sealed record QueuedEntry(int Version, TItem Item);

    private sealed class RunBucket
    {
        public RunBucket(Guid runId, long arrivalSequence)
        {
            RunId = runId;
            ArrivalSequence = arrivalSequence;
        }

        public Guid RunId { get; }

        public long ArrivalSequence { get; }

        public long LastServedSequence { get; set; }

        public bool IsRegistered { get; set; }

        public List<QueuedEntry> Items { get; } = new();
    }

    private sealed class SimulatedRunCursor
    {
        public SimulatedRunCursor(RunBucket bucket, long simLastServed, long arrival)
        {
            Bucket = bucket;
            SimLastServed = simLastServed;
            Arrival = arrival;
        }

        public RunBucket Bucket { get; }

        public long SimLastServed { get; set; }

        public long Arrival { get; }

        public int NextIndex { get; set; }
    }
}
