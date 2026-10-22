using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace MiniGame.Framework
{
    public enum HttpOutcome { Success, HttpError, TransportError, Timeout, Cancelled, TooLarge }

    public readonly struct HttpResponse
    {
        public readonly HttpOutcome Outcome;
        public readonly long StatusCode;
        public readonly byte[] Body;
        public bool Success => Outcome == HttpOutcome.Success;
        public HttpResponse(HttpOutcome outcome, long statusCode, byte[] body) { Outcome = outcome; StatusCode = statusCode; Body = body; }
    }

    public sealed class HttpService
    {
        private sealed class BoundedDownload : DownloadHandlerScript
        {
            private readonly MemoryStream stream = new MemoryStream();
            private readonly int maximum;
            public bool TooLarge { get; private set; }
            public BoundedDownload(int maximum) : base(new byte[8192]) { this.maximum = maximum; }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (length <= 0) return true;
                if (stream.Length + length > maximum) { TooLarge = true; return false; }
                stream.Write(data, 0, length);
                return true;
            }
            public byte[] CopyBody() => stream.ToArray();
            public void CloseBuffer() => stream.Dispose();
        }

        public UniTask<HttpResponse> SendAsync(string url, string method = "GET", byte[] body = null,
            int maximumResponseBytes = 1048576, int timeoutSeconds = 15, CancellationToken cancellation = default,
            System.Collections.Generic.IReadOnlyDictionary<string, string> headers = null, string contentType = "application/octet-stream")
            => SendWithProgressAsync(url, null, method, body, maximumResponseBytes, timeoutSeconds, cancellation, headers, contentType);

        public async UniTask<HttpResponse> SendWithProgressAsync(string url, IProgress<float> progress, string method = "GET", byte[] body = null,
            int maximumResponseBytes = 1048576, int timeoutSeconds = 15, CancellationToken cancellation = default,
            System.Collections.Generic.IReadOnlyDictionary<string, string> headers = null, string contentType = "application/octet-stream")
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                throw new ArgumentException("An absolute HTTP(S) URL is required.", nameof(url));
            if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("Method is required.", nameof(method));
            if (maximumResponseBytes < 1 || timeoutSeconds < 1) throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
            if (body != null && body.Length > 1048576) throw new ArgumentException("Request body exceeds 1 MiB.", nameof(body));
            if (cancellation.IsCancellationRequested) return new HttpResponse(HttpOutcome.Cancelled, 0, Array.Empty<byte>());
            var download = new BoundedDownload(maximumResponseBytes);
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = download;
                request.timeout = timeoutSeconds;
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.SetRequestHeader("Content-Type", contentType);
                }
                if (headers != null)
                    foreach (var header in headers) request.SetRequestHeader(header.Key, header.Value);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var reports = new SceneProgress(progress);
                    reports.Report(0);
                    if (cancellation.IsCancellationRequested) return new HttpResponse(HttpOutcome.Cancelled, 0, Array.Empty<byte>());
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (cancellation.IsCancellationRequested)
                        {
                            request.Abort();
                            return new HttpResponse(HttpOutcome.Cancelled, request.responseCode, Array.Empty<byte>());
                        }
                        if (watch.Elapsed.TotalSeconds >= timeoutSeconds)
                        {
                            request.Abort();
                            return new HttpResponse(HttpOutcome.Timeout, request.responseCode, Array.Empty<byte>());
                        }
                        reports.Report(Math.Min(0.999999f, request.downloadProgress));
                        await UniTask.Yield();
                    }
                    if (cancellation.IsCancellationRequested) return new HttpResponse(HttpOutcome.Cancelled, request.responseCode, Array.Empty<byte>());
                    if (download.TooLarge) return new HttpResponse(HttpOutcome.TooLarge, request.responseCode, Array.Empty<byte>());
                    HttpOutcome outcome = request.result == UnityWebRequest.Result.Success ? HttpOutcome.Success :
                        request.result == UnityWebRequest.Result.ProtocolError ? HttpOutcome.HttpError :
                        watch.Elapsed.TotalSeconds >= timeoutSeconds ? HttpOutcome.Timeout : HttpOutcome.TransportError;
                    if (outcome == HttpOutcome.Success) reports.Report(1);
                    if (cancellation.IsCancellationRequested) return new HttpResponse(HttpOutcome.Cancelled, request.responseCode, Array.Empty<byte>());
                    return new HttpResponse(outcome, request.responseCode, download.CopyBody());
                }
                finally { download.CloseBuffer(); }
            }
        }
    }
}
