/*
 * Copyright(c) 2025 GiR-Zippo
 * Licensed under the GPL v3 license. See https://github.com/GiR-Zippo/LightAmp/blob/main/LICENSE for full license information.
 */

using System;
using System.Threading;

using BardMusicPlayer.XIVMIDI.IO;

namespace BardMusicPlayer.XIVMIDI;

/// <summary>
/// Singleton façade for the BardMusicPlayer MIDI search/download service.
/// </summary>
public sealed partial class XIVMIDI : IDisposable
{
    //  Singleton

    private static readonly Lazy<XIVMIDI> _lazy = new(static () => new XIVMIDI());
    public static XIVMIDI Instance => _lazy.Value;

    private XIVMIDI() { }

    //  Events

    /// <summary>
    /// Raised on the thread-pool when a queued request completes (success or failure).
    /// The event argument is one of:
    /// <list type="bullet">
    ///   <item><see cref="ResponseContainer.ApiResponse"/> – for <see cref="Requester.JSON"/> requests.</item>
    ///   <item><see cref="ResponseContainer.MidiFile"/>    – for <see cref="Requester.DOWNLOAD"/> requests.</item>
    ///   <item><see cref="GetRequest"/>                    – when the server returned a non-200 status.</item>
    /// </list>
    /// </summary>
    public event EventHandler<object> OnRequestFinished;

    //  State

    /// <summary><c>true</c> after <see cref="Start"/> and before <see cref="Stop"/>.</summary>
    public bool Started { get; private set; }

    /// <summary>
    /// <c>true</c> while at least one HTTP call is in flight.
    /// Written with <see cref="Interlocked"/> so reads from any thread are safe.
    /// </summary>
    public bool IsRequestRunning => Interlocked.CompareExchange(ref _requestRunning, 0, 0) != 0;
    private int _requestRunning; // 0 = idle, 1 = running

    //  Lifecycle

    /// <summary>Starts the HTTP client and background worker.</summary>
    public void Start()
    {
        if (Started) return;
        StartService();
        Started = true;
    }

    /// <summary>Drains the queue, cancels in-flight requests and releases resources.</summary>
    public void Stop()
    {
        if (!Started) return;
        Started = false;
        StopWorkerThread();
        httpClient?.Dispose();
        httpClient = null;
        httpClientHandler = null;
    }

    //  Queue API

    /// <summary>
    /// Enqueues a <see cref="GetRequest"/> for processing.
    /// </summary>
    /// <param name="request">The request to enqueue.</param>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="InvalidOperationException">Thrown if the service has not been started.</exception>
    public void AddToQueue(GetRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (!Started) throw new InvalidOperationException("Call Start() before enqueuing requests.");
        downloadQueue.Enqueue(request);
    }

    /// <summary>
    /// Cancels all pending and in-flight requests, then restarts the worker.
    /// </summary>
    public void CancelDownloads() => CancelDownloadQueue();

    //  IDisposable

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    // Finaliser as safety net – Dispose() guards against double-free.
    ~XIVMIDI() => Dispose();
}
