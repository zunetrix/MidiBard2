/*
 * Copyright(c) 2026 GiR-Zippo
 * Licensed under the GPL v3 license. See https://github.com/GiR-Zippo/LightAmp/blob/main/LICENSE for full license information.
 */

using BardMusicPlayer.XIVMIDI.Events;
using BardMusicPlayer.XIVMIDI.IO;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BardMusicPlayer.XIVMIDI.WebApi
{
    public class HTTPWorker : IDisposable
    {
        private string UserAgent { get; } = "XIVMIDI CLIENT V2 (LightAmp)";
        private HttpClient _HttpClient { get; set; } = null;
        private HttpClientHandler _HttpClientHandler { get; set; } = null;
        private int _inFlight;


        public bool IsRequestRunning => Volatile.Read(ref _inFlight) > 0;

        /// <summary>
        /// Setup the HTTPWorker
        /// </summary>
        public HTTPWorker()
        {
            _HttpClientHandler = new HttpClientHandler
            {
                UseCookies = true,
                UseProxy = true,
                MaxAutomaticRedirections = 2,
                MaxConnectionsPerServer = 2
            };
            _HttpClient = new HttpClient(handler: _HttpClientHandler);
            _HttpClient.Timeout = TimeSpan.FromMinutes(5);
            _HttpClient.DefaultRequestHeaders.Clear();
            _HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        /// <summary>
        /// Dispose everything
        /// </summary>
        public void Dispose()
        {
            _HttpClient.Dispose();
            _HttpClientHandler.Dispose();
        }

        /// <summary>
        /// Cancel pending requests from HTTP Client
        /// </summary>
        public void CancelPendingRequests()
        {
            _HttpClient.CancelPendingRequests();
        }

        /// <summary>
        /// Get the song list
        /// </summary>
        /// <param name="request"></param>
        public async Task RequestSongList(object request, bool dynamic, object args = null)
        {
            string url = "";
            if (request is BMPAPIRequestBuilder)
                url = ((BMPAPIRequestBuilder)request).BuildRequest();
            else if (request is XIVMIDIRequestBuilder)
                url = ((XIVMIDIRequestBuilder)request).BuildRequest();

            if (url.Length < 1)
                return;

            RequestKind requestKind;
            if (request is BMPAPIRequestBuilder)
            {
                requestKind = RequestKind.SongListBmp;
            }
            else
            {
                requestKind = RequestKind.SongListXivmidi;
            }

            foreach (Cookie co in _HttpClientHandler.CookieContainer.GetCookies(new Uri(url)))
                co.Expires = DateTime.Now.Subtract(TimeSpan.FromDays(1));

            using var http_request = new HttpRequestMessage(HttpMethod.Get, url);
            http_request.Headers.Accept.ParseAdd("application/json;q=0.8");

            Interlocked.Increment(ref _inFlight);
            try
            {
                HttpResponseMessage response = await _HttpClient.SendAsync(http_request);
                HttpContent ResponseBody = response.Content;
                if (response.IsSuccessStatusCode)
                {
                    if (request is BMPAPIRequestBuilder)
                        XIVMidiApi.Instance.PublishEvent(new XIVMidiBMPSongsEvent(JsonConvert.DeserializeObject<BMPResponseContainer.Root>(ResponseBody.ReadAsStringAsync().Result), dynamic, args));
                    else if (request is XIVMIDIRequestBuilder)
                        XIVMidiApi.Instance.PublishEvent(new XIVMidiXIVSongsEvent(JsonConvert.DeserializeObject<XIVMIDIResponseContainer.ApiResponse>(ResponseBody.ReadAsStringAsync().Result), dynamic, args));
                    return;
                }
                else
                    XIVMidiApi.Instance.PublishEvent(new XIVMidiApiErrorEvent((int)response.StatusCode, response.StatusCode.ToString(), args, requestKind));
            }
            catch (HttpRequestException e)
            {
                XIVMidiApi.Instance.PublishEvent(new XIVMidiApiErrorEvent((int)HttpStatusCode.ServiceUnavailable, e.InnerException?.Message ?? e.Message, args, requestKind));
            }
            catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
            {
                XIVMidiApi.Instance.PublishEvent(new XIVMidiApiErrorEvent(408, "Request timed out", args, requestKind));
            }
            catch(TaskCanceledException)
            {
                // no-op for XIVMidiApi.CancelDownloads
            }
            catch (JsonException)
            {
                XIVMidiApi.Instance.PublishEvent(new XIVMidiApiErrorEvent(502, "Invalid response", args, requestKind));
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        /// <summary>
        /// Download a song
        /// </summary>
        /// <param name="url"></param>
        /// <param name="args"></param>
        /// <param name="fromBMP"></param>
        public async Task DownloadSong(string url, object args, bool fromBMP)
        {
            if (url.Length < 1)
                return;

            foreach (Cookie co in _HttpClientHandler.CookieContainer.GetCookies(new Uri(url)))
                co.Expires = DateTime.Now.Subtract(TimeSpan.FromDays(1));

            using var http_request = new HttpRequestMessage(HttpMethod.Get, url);
            http_request.Headers.Accept.ParseAdd("audio/mid");

            Interlocked.Increment(ref _inFlight);
            try
            {
                HttpResponseMessage response = await _HttpClient.SendAsync(http_request);
                HttpContent ResponseBody = response.Content;
                if (response.IsSuccessStatusCode)
                {
                    XIVMIDIResponseContainer.MidiFile midi = new()
                    {
                        Filename = ResolveFileName(response, url),
                        data = ResponseBody.ReadAsByteArrayAsync().Result
                    };
                    XIVMidiApi.Instance.PublishEvent(new XIVMidiFileEvent(midi, args));
                }
                else
                    XIVMidiApi.Instance.PublishEvent(new XIVMidiApiErrorEvent((int)response.StatusCode, response.StatusCode.ToString(), args, RequestKind.MidiDownload));
            }
            catch (HttpRequestException e)
            {
                XIVMidiApi.Instance.PublishEvent(new XIVMidiApiErrorEvent((int)HttpStatusCode.ServiceUnavailable, e.InnerException?.Message ?? e.Message, args, RequestKind.MidiDownload));
            }
            catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
            {
                XIVMidiApi.Instance.PublishEvent(new XIVMidiApiErrorEvent(408, "Request timed out", args, RequestKind.MidiDownload));
            }
            catch(TaskCanceledException)
            {
                // no-op for XIVMidiApi.CancelDownloads
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        /// <summary>
        /// Upload a song
        /// </summary>
        /// <param name="upload"></param>
        public async Task UploadSong(BMPUploadBuilder upload)
        {
            string boundary = "----" + Guid.NewGuid().ToString("N");
            string payload = JsonConvert.SerializeObject(upload);

            byte[] Part(string headers, byte[] body) =>
                Encoding.ASCII.GetBytes($"--{boundary}\r\n{headers}\r\n\r\n")
                    .Concat(body).Concat(Encoding.ASCII.GetBytes("\r\n")).ToArray();

            var body = Part(
                    $"Content-Disposition: form-data; name=\"file\"; filename=\"{Path.GetFileName(upload.FileName)}\"\r\nContent-Type: audio/mid",
                    upload.MidiFile)
                .Concat(Part("Content-Disposition: form-data; name=\"_payload\"", Encoding.UTF8.GetBytes(payload)))
                .Concat(Encoding.ASCII.GetBytes($"--{boundary}--\r\n"))
                .ToArray();

            using var request = new HttpRequestMessage(HttpMethod.Post, upload.ApiBaseUrl);
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data")
            {
                Parameters = { new NameValueHeaderValue("boundary", boundary) }
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("users", "API-Key " + upload.ApiKey);

            try
            {
                var response = await _HttpClient.SendAsync(request);
                //var responseBody = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($"Status: {response.StatusCode} | Body: {responseBody}");
                XIVMidiApi.Instance.PublishEvent(new XIVMidiUploadResponseEvent(response.StatusCode));
            }
            catch (Exception)
            {
                XIVMidiApi.Instance.PublishEvent(new XIVMidiUploadResponseEvent(HttpStatusCode.ServiceUnavailable));
            }
        }


        private static string ResolveFileName(HttpResponseMessage response, string url)
        {
            var cd = response.Content.Headers.ContentDisposition;
            if (!string.IsNullOrEmpty(cd?.FileNameStar))
            {
                return SanitizeFileName(cd.FileNameStar);
            }
            if (!string.IsNullOrEmpty(cd?.FileName))
            {
                return SanitizeFileName(cd.FileName.Trim('"'));
            }
            return SanitizeFileName(Uri.UnescapeDataString(new Uri(url).Segments.Last()));

        }
        private static string SanitizeFileName(string name)
        {
            var str = Path.GetFileName(name);

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                str = str.Replace(invalidChar, '_');
            }

            if (string.IsNullOrEmpty(str.Trim('.', ' ')))
            {
                str = "song.mid";
            }

            if (!(string.Equals(Path.GetExtension(str), ".mid", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(str), ".midi", StringComparison.OrdinalIgnoreCase)))
            {
                str += ".mid";
            }


            return str;
        }
    }
}
