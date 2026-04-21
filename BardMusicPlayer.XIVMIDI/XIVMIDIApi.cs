/*
 * Copyright(c) 2025 GiR-Zippo
 * Licensed under the GPL v3 license. See https://github.com/GiR-Zippo/LightAmp/blob/main/LICENSE for full license information.
 */

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using Newtonsoft.Json;

using BardMusicPlayer.XIVMIDI.IO;

namespace BardMusicPlayer.XIVMIDI;

/*
 * USAGE GUIDE
 * ═══════════════════════════════════════════════════════════════════════════
 *
 *  Lifecycle
 *   XIVMIDI.Instance.Start();
 *   XIVMIDI.Instance.Stop();
 *
 *  Subscribe
 *   XIVMIDI.Instance.OnRequestFinished += OnResult;
 *
 *   private void OnResult(object sender, object e)
 *   {
 *       switch (e)
 *       {
 *           case ResponseContainer.ApiResponse api:
 *               // api.docs → List<MidiEntry>
 *               break;
 *           case ResponseContainer.MidiFile file:
 *               // file.Filename, file.data (byte[])
 *               break;
 *           case GetRequest failed:
 *               // failed.ResponseCode, failed.ResponseMsg
 *               break;
 *       }
 *   }
 *
 *  Search
 *   XIVMIDI.Instance.AddToQueue(new GetRequest
 *   {
 *       Url = new RequestBuilder
 *       {
 *           Search   = "song name",
 *           Editor   = "editor name",
 *           Ensemble = Misc.EnsembleSize[4],   // "quartet"
 *           Source   = Misc.Sources[1],         // "xivmidi.com"
 *           Sort     = Misc.SortOptions[0],     // "-createdAt"
 *           Page     = 1
 *       }.BuildRequest(),
 *       Requester = Requester.JSON
 *   });
 *
 *  Download
 *   // Use the `url` field from a MidiEntry returned by a search result.
 *   XIVMIDI.Instance.AddToQueue(new GetRequest
 *   {
 *       Url       = midiEntry.url,
 *       Accept    = "audio/midi",
 *       Requester = Requester.DOWNLOAD
 *   });
 */

public sealed partial class XIVMIDI
{
    //  Private state

    private HttpClient httpClient = null;
    private HttpClientHandler httpClientHandler = null;

    private ConcurrentQueue<GetRequest> downloadQueue = new();
    private CancellationTokenSource cancelTokenSource;

    //  Service bootstrap

    private void StartService()
    {
        httpClientHandler = new HttpClientHandler
        {
            UseCookies = true,
            UseProxy = true,
            MaxAutomaticRedirections = 2,
            MaxConnectionsPerServer = 4   // bumped: search + concurrent downloads
        };

        httpClient = new HttpClient(httpClientHandler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        StartWorkerThread();
    }

    //  Worker thread

    private void StartWorkerThread()
    {
        downloadQueue = new ConcurrentQueue<GetRequest>();
        cancelTokenSource = new CancellationTokenSource();

        Task.Factory.StartNew(
            () => RunEventsHandler(cancelTokenSource.Token),
            cancelTokenSource.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void StopWorkerThread()
    {
        cancelTokenSource?.Cancel();
        while (downloadQueue.TryDequeue(out _)) { }
    }

    private void CancelDownloadQueue()
    {
        StopWorkerThread();
        httpClient?.CancelPendingRequests();
        StartWorkerThread();
        Interlocked.Exchange(ref _requestRunning, 0);
    }

    //  Event loop

    private async Task RunEventsHandler(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            while (downloadQueue.TryDequeue(out var request))
            {
                if (ct.IsCancellationRequested) break;

                // Fire-and-forget: each request runs concurrently.
                // Exceptions are caught inside GetAsync so nothing leaks here.
                _ = GetAsync(request, ct);
            }

            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    //  HTTP execution

    private async Task GetAsync(GetRequest request, CancellationToken ct)
    {
        Interlocked.Exchange(ref _requestRunning, 1);

        try
        {
            // Expire stale cookies for this host to avoid caching issues.
            var uri = new Uri(request.Url);
            foreach (Cookie co in httpClientHandler.CookieContainer.GetCookies(uri))
                co.Expires = DateTime.UtcNow.AddDays(-1);

            // Use a scoped HttpRequestMessage so we never mutate shared
            // DefaultRequestHeaders from concurrent tasks.
            using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
            message.Headers.TryAddWithoutValidation("User-Agent", request.UserAgent);
            if (!string.IsNullOrWhiteSpace(request.Accept))
                message.Headers.TryAddWithoutValidation("Accept", request.Accept);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseContentRead,
                    ct);
            }
            catch (OperationCanceledException)
            {
                return; // Cancelled – silently exit.
            }
            catch (HttpRequestException ex)
            {
                request.ResponseCode = HttpStatusCode.ServiceUnavailable;
                request.ResponseMsg = ex.InnerException?.Message ?? ex.Message;
                RaiseFinished(request);
                return;
            }

            using (response)
            {
                request.Host = uri.DnsSafeHost;
                request.ResponseCode = response.StatusCode;
                request.ResponseMsg = response.ReasonPhrase ?? "";

                if (!request.IsSuccess)
                {
                    RaiseFinished(request);
                    return;
                }

                // Read all bytes while the response is still open.
                var bytes = await response.Content.ReadAsByteArrayAsync();

                switch (request.Requester)
                {
                    case Requester.JSON:
                        ParseJson(request, bytes);
                        break;
                    case Requester.DOWNLOAD:
                        ParseMidi(request, bytes);
                        break;
                    default:
                        RaiseFinished(request);
                        break;
                }
            }
        }
        finally
        {
            if (downloadQueue.IsEmpty)
                Interlocked.Exchange(ref _requestRunning, 0);
        }
    }

    //  Parsers

    private void ParseJson(GetRequest request, byte[] bytes)
    {
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        ResponseContainer.ApiResponse resp;
        try
        {
            resp = JsonConvert.DeserializeObject<ResponseContainer.ApiResponse>(json)
                   ?? new ResponseContainer.ApiResponse();
        }
        catch (JsonException ex)
        {
            request.ResponseCode = HttpStatusCode.UnprocessableEntity;
            request.ResponseMsg = $"JSON parse error: {ex.Message}";
            RaiseFinished(request);
            return;
        }

        RaiseFinished(resp);
    }

    private void ParseMidi(GetRequest request, byte[] bytes)
    {
        var filename = Uri.UnescapeDataString(request.Url).Split('/').Last();
        RaiseFinished(new ResponseContainer.MidiFile { Filename = filename, data = bytes });
    }

    //  Event helper

    /// <summary>
    /// Raises <see cref="OnRequestFinished"/> safely; swallows handler exceptions
    /// so a buggy subscriber cannot crash the worker thread.
    /// </summary>
    private void RaiseFinished(object payload)
    {
        try { OnRequestFinished?.Invoke(this, payload); }
        catch { /* subscriber errors must not crash the worker */ }
    }
}
