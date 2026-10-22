#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Framework.Tests
{
    public sealed class HttpProgressTests
    {
        private HttpListener listener;
        private Task server;
        private Task<HttpResponse> client;
        private CancellationTokenSource cancellation;
        private string url;

        [SetUp]
        public void Setup()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            int port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            url = "http://127.0.0.1:" + port + "/";
            listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();
            cancellation = new CancellationTokenSource();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            cancellation.Cancel();
            listener.Close();
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while ((server != null && !server.IsCompleted || client != null && !client.IsCompleted) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.IsTrue(server == null || server.IsCompleted);
            Assert.IsTrue(client == null || client.IsCompleted);
            cancellation.Dispose();
            if (server != null) server.GetAwaiter().GetResult();
            if (client?.Exception != null) throw client.Exception;
        }

        [UnityTest]
        public IEnumerator SuccessReportsMonotonicDownloadProgressAndPreservesActualBody()
        {
            var values = new List<float>();
            server = Respond();
            client = new HttpService().SendWithProgressAsync(url, new Reporter(values.Add), timeoutSeconds: 5, cancellation: cancellation.Token).AsTask();
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (!client.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.IsTrue(client.IsCompleted);
            Assert.AreEqual(HttpOutcome.Success, client.Result.Outcome);
            Assert.AreEqual(4096, client.Result.Body.Length);
            Assert.AreEqual(0, values[0]);
            Assert.AreEqual(1, values[values.Count - 1]);
            for (int i = 1; i < values.Count; i++) Assert.Greater(values[i], values[i - 1]);
        }

        [UnityTest]
        public IEnumerator CancellationFromInitialProgressDoesNotSendARequest()
        {
            client = new HttpService().SendWithProgressAsync(url, new Reporter(_ => cancellation.Cancel()), cancellation: cancellation.Token).AsTask();
            while (!client.IsCompleted) yield return null;
            Assert.AreEqual(HttpOutcome.Cancelled, client.Result.Outcome);
            Assert.AreEqual(0, client.Result.StatusCode);
        }

        private async Task Respond()
        {
            var context = await listener.GetContextAsync();
            context.Response.ContentLength64 = 4096;
            try
            {
                var bytes = new byte[2048];
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                await context.Response.OutputStream.FlushAsync();
                await Task.Delay(50);
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            finally { context.Response.Close(); }
        }

        private sealed class Reporter : IProgress<float>
        {
            private readonly Action<float> report;
            public Reporter(Action<float> report) { this.report = report; }
            public void Report(float value) => report(value);
        }
    }
}
#endif
