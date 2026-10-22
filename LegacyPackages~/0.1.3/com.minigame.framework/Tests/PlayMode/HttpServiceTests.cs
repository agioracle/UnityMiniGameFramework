#if UNITY_EDITOR
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniGame.Framework.Tests
{
    public sealed class HttpServiceTests
    {
        private HttpListener listener;
        private string url;
        [SetUp]
        public void StartServer()
        {
            var portProbe = new TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();
            url = "http://127.0.0.1:" + port + "/";
            listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();
        }
        [TearDown]
        public void StopServer() => listener?.Close();

        private async Task Respond(int status, string body)
        {
            HttpListenerContext context = await listener.GetContextAsync();
            byte[] data = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = data.Length;
            try { await context.Response.OutputStream.WriteAsync(data, 0, data.Length); }
            finally { context.Response.Close(); }
        }

        [UnityTest]
        public IEnumerator ReturnsActualHttpStatusAndBody()
        {
            Task response = Respond(409, "conflict");
            var task = new HttpService().SendAsync(url).AsTask();
            while (!task.IsCompleted) yield return null;
            HttpResponse result = task.GetAwaiter().GetResult();
            Assert.AreEqual(HttpOutcome.HttpError, result.Outcome);
            Assert.AreEqual(409, result.StatusCode);
            Assert.AreEqual("conflict", Encoding.UTF8.GetString(result.Body));
            while (!response.IsCompleted) yield return null;
            response.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator ResponseSizeIsBounded()
        {
            Task response = Respond(200, new string('x', 4096));
            var task = new HttpService().SendAsync(url, maximumResponseBytes: 128).AsTask();
            while (!task.IsCompleted) yield return null;
            Assert.AreEqual(HttpOutcome.TooLarge, task.GetAwaiter().GetResult().Outcome);
            while (!response.IsCompleted) yield return null;
            response.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator InFlightCancellationAbortsWaitingWithoutRetry()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                Task<HttpListenerContext> accepted = listener.GetContextAsync();
                var task = new HttpService().SendAsync(url, cancellation: cancellation.Token).AsTask();
                float deadline = UnityEngine.Time.realtimeSinceStartup + 5;
                while (!accepted.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(accepted.IsCompleted);
                cancellation.Cancel();
                while (!task.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(task.IsCompleted);
                Assert.AreEqual(HttpOutcome.Cancelled, task.GetAwaiter().GetResult().Outcome);
                accepted.Result.Response.Close();
            }
        }

        [UnityTest]
        public IEnumerator UnresponsiveServerProducesTimeout()
        {
            Task<HttpListenerContext> accepted = listener.GetContextAsync();
            var task = new HttpService().SendAsync(url, timeoutSeconds: 1).AsTask();
            float deadline = UnityEngine.Time.realtimeSinceStartup + 5;
            while (!task.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(HttpOutcome.Timeout, task.GetAwaiter().GetResult().Outcome);
            if (accepted.IsCompleted) accepted.Result.Response.Close();
        }

        [UnityTest]
        public IEnumerator PreCancelledRequestDoesNotUseNetwork()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var task = new HttpService().SendAsync(url, cancellation: cancellation.Token).AsTask();
                while (!task.IsCompleted) yield return null;
                Assert.AreEqual(HttpOutcome.Cancelled, task.GetAwaiter().GetResult().Outcome);
            }
        }
    }
}
#endif
