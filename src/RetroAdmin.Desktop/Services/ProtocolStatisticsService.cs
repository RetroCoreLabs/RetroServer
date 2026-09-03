//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using System;
using System.Collections.Generic;
using System.Threading;

namespace RetroAdmin.Desktop.Services;

/// <summary>
/// Rolling sample history behind the monitor charts.
/// </summary>
/// <remarks>
/// It used to implement IEconetStatisticsTracker so the in-process Econet server could
/// push individual events into it. A management client never sees those events -- it polls
/// the server's totals through RecordSample -- so the interface, and with it the last
/// reason for this project to reference a protocol assembly, is gone.
/// </remarks>
public class ProtocolStatisticsService
{
    private long _totalRequests;
    private long _totalBytesTransferred;
    private long _totalFilesTransferred;
    private readonly object _lock = new();
    private readonly List<StatisticsSnapshot> _snapshots = new();
    private readonly Timer _snapshotTimer;

    public event EventHandler<StatisticsSnapshot>? StatisticsUpdated;

    public ProtocolStatisticsService()
    {
        _snapshotTimer = new Timer(TakeSnapshot, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void RecordRequest(int bytesTransferred, bool isFileOperation)
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalBytesTransferred, bytesTransferred);

            if (isFileOperation)
                Interlocked.Increment(ref _totalFilesTransferred);
        }
    }

    /// <summary>
    /// Records the server's cumulative counters, as read from the management API.
    /// </summary>
    /// <param name="totalRequests">Requests served since the server started.</param>
    /// <param name="totalBytes">Bytes transferred since the server started.</param>
    /// <param name="totalFiles">Files transferred since the server started.</param>
    /// <remarks>
    /// RecordRequest above is for a server running in THIS process, counting one event at a time.
    /// A management client never sees the individual events -- it polls totals -- so it needs to
    /// set the counters rather than add to them. The snapshot history, and therefore the charts,
    /// work the same either way.
    ///
    /// Counters that went backwards mean the server restarted, and the totals began again from
    /// zero. That is recorded as-is; the charts plot deltas between snapshots, so the restart
    /// shows up as one gap rather than corrupting everything after it.
    /// </remarks>
    public void RecordSample(long totalRequests, long totalBytes, long totalFiles)
    {
        Interlocked.Exchange(ref _totalRequests, totalRequests);
        Interlocked.Exchange(ref _totalBytesTransferred, totalBytes);
        Interlocked.Exchange(ref _totalFilesTransferred, totalFiles);
    }

    private void TakeSnapshot(object? state)
    {
        StatisticsSnapshot snapshot = new StatisticsSnapshot
        {
            Timestamp = DateTime.Now,
            TotalRequests = Interlocked.Read(ref _totalRequests),
            TotalBytesTransferred = Interlocked.Read(ref _totalBytesTransferred),
            TotalFilesTransferred = Interlocked.Read(ref _totalFilesTransferred)
        };

        lock (_lock)
        {
            _snapshots.Add(snapshot);

            int maxSnapshots = (int)TimeSpan.FromDays(30).TotalSeconds / 30;
            while (_snapshots.Count > maxSnapshots)
                _snapshots.RemoveAt(0);
        }

        StatisticsUpdated?.Invoke(this, snapshot);
    }

    public List<StatisticsSnapshot> GetSnapshots(TimeSpan duration)
    {
        DateTime cutoff = DateTime.Now - duration;

        lock (_lock)
        {
            List<StatisticsSnapshot> result = new List<StatisticsSnapshot>();
            for (int i = 0; i < _snapshots.Count; i++)
            {
                if (_snapshots[i].Timestamp >= cutoff)
                    result.Add(_snapshots[i]);
            }

            return result;
        }
    }

    public StatisticsSnapshot GetCurrentSnapshot()
    {
        return new StatisticsSnapshot
        {
            Timestamp = DateTime.Now,
            TotalRequests = Interlocked.Read(ref _totalRequests),
            TotalBytesTransferred = Interlocked.Read(ref _totalBytesTransferred),
            TotalFilesTransferred = Interlocked.Read(ref _totalFilesTransferred)
        };
    }
}

public sealed class TnfsStatisticsService : ProtocolStatisticsService
{
}

public sealed class EconetStatisticsService : ProtocolStatisticsService
{
}

public class StatisticsSnapshot
{
    public DateTime Timestamp { get; set; }
    public long TotalRequests { get; set; }
    public long TotalBytesTransferred { get; set; }
    public long TotalFilesTransferred { get; set; }
}
