//-----------------------------------------------------------------------
// <copyright file="FaviconUserUsage.cs" company="aliasvault">
// Copyright (c) aliasvault. All rights reserved.
// Licensed under the AGPLv3 license. See LICENSE.md file in the project root for full license information.
// </copyright>
//-----------------------------------------------------------------------

namespace AliasVault.Api.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// Per-user usage record for <see cref="FaviconRateLimitService"/>. Tracks recent extractions
/// in per-minute buckets so a power-user re-downloading thousands of favicons in quick succession
/// still produces a bounded number of entries.
/// </summary>
internal sealed class FaviconUserUsage
{
    // Bucket entries by the minute they happened in. A power-user re-downloading a 5k-item
    // vault produces at most ~minutes-of-batches entries this way, even though the underlying
    // unit count can be in the thousands — keeps memory bounded without losing accuracy.
    private readonly LinkedList<(DateTime BucketStart, int Count)> _entries = new();

    /// <summary>
    /// Gets the lock guarding mutations to this usage record. Callers must hold this lock
    /// across read-modify-write sequences (Prune + Add) to keep TryConsume atomic.
    /// </summary>
    public object SyncRoot { get; } = new object();

    /// <summary>
    /// Gets the total number of units consumed inside the currently retained window.
    /// </summary>
    public int Total { get; private set; }

    /// <summary>
    /// Records <paramref name="count"/> consumed units at <paramref name="now"/>, merging into
    /// the last bucket when it falls in the same minute.
    /// </summary>
    /// <param name="now">The timestamp to record under (typically <see cref="DateTime.UtcNow"/>).</param>
    /// <param name="count">Number of units consumed.</param>
    public void Add(DateTime now, int count)
    {
        var bucket = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

        if (_entries.Last is { Value.BucketStart: var lastBucket } lastNode && lastBucket == bucket)
        {
            _entries.Last.Value = (lastBucket, lastNode.Value.Count + count);
        }
        else
        {
            _entries.AddLast((bucket, count));
        }

        Total += count;
    }

    /// <summary>
    /// Drops buckets older than <paramref name="cutoff"/> from the front of the list, which is
    /// where the oldest entries live since Add only appends.
    /// </summary>
    /// <param name="cutoff">Buckets strictly older than this are removed.</param>
    public void Prune(DateTime cutoff)
    {
        while (_entries.First is { Value.BucketStart: var first } node && first < cutoff)
        {
            Total -= node.Value.Count;
            _entries.RemoveFirst();
        }
    }
}
