/*
 * Copyright(c) 2025 GiR-Zippo
 * Licensed under the GPL v3 license. See https://github.com/GiR-Zippo/LightAmp/blob/main/LICENSE for full license information.
 */

using System;
using System.Net;
using System.Net.Http;

namespace BardMusicPlayer.XIVMIDI.IO
{
    /// <summary>
    /// Represents a single HTTP GET request to be processed by the XIVMIDI worker.
    /// </summary>
    public sealed class GetRequest : IDisposable
    {
        //  Identity
        /// <summary>Unique identifier for this request (auto-generated).</summary>
        public Guid Id { get; } = Guid.NewGuid();

        //  Input
        /// <summary>Full request URL, including query string.</summary>
        public string Url { get; init; } = "";

        /// <summary>Determines how the response body is parsed.</summary>
        public Requester Requester { get; init; } = Requester.NONE;

        /// <summary>User-Agent header sent with the request.</summary>
        public string UserAgent { get; init; } = "BardMusicPlayer XIVMIDI Client/2.0";

        /// <summary>
        /// Accept header. Defaults to JSON.
        /// For MIDI downloads use <c>"audio/midi"</c>.
        /// </summary>
        public string Accept { get; init; } = "application/json";

        //  Output (populated by the worker)

        /// <summary>Raw response body. Disposed together with this object.</summary>
        public HttpContent ResponseBody { get; internal set; } = null;

        /// <summary>HTTP status code of the response.</summary>
        public HttpStatusCode ResponseCode { get; internal set; } = HttpStatusCode.Unused;

        /// <summary>HTTP reason phrase, or an exception message on network failure.</summary>
        public string ResponseMsg { get; internal set; } = "";

        /// <summary>Resolved host name, populated after a successful HTTP call.</summary>
        public string Host { get; internal set; } = "";

        //  Helpers

        /// <summary>Returns <c>true</c> when the server replied with 200 OK.</summary>
        public bool IsSuccess => ResponseCode == HttpStatusCode.OK;

        /// <summary>
        /// Returns a short diagnostic string useful for logging.
        /// </summary>
        public override string ToString() =>
            $"[{Id:N}] {Requester} {ResponseCode} - {Url}";

        //  IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ResponseBody?.Dispose();
        }
    }
}
