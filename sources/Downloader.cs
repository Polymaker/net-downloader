using NetDownloader.Collections;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetDownloader
{
    public class Downloader
    {
        internal static readonly Downloader Instance;
        public static bool AutoStartDownloads { get; set; }

        public const int DOWNLOAD_THREAD_COUNT = 6;

        private ConcurrentQueueEx<DownloadRequest> DownloadRequests;
        private Thread[] DownloadThreads;
        private readonly object ThreadInitLock = new object();
        private bool Disposing = false;
        private int RunningThreadCounter = 0;

        static Downloader()
        {
            Instance = new Downloader();
            AutoStartDownloads = true;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            AppDomain.CurrentDomain.DomainUnload += CurrentDomain_ProcessExit;
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            Instance.Unload();
        }

        private Downloader()
        {
            DownloadThreads = new Thread[DOWNLOAD_THREAD_COUNT];
            //WebClients = new ConcurrentQueue<WebClientEx>();
            DownloadRequests = new ConcurrentQueueEx<DownloadRequest>();
            DownloadRequests.CollectionChanged += DownloadRequests_Changed;
        }

        private void Unload()
        {
            Disposing = true;

            if (RunningThreadCounter > 0)
            {
                for (int i = 0; i < DOWNLOAD_THREAD_COUNT; i++)
                {
                    if (DownloadThreads[i] != null && DownloadThreads[i].IsAlive)
                    {
                        DownloadThreads[i].Join();
                        DownloadThreads[i] = null;
                    }
                }
            }

            DownloadRequests.CollectionChanged -= DownloadRequests_Changed;
            DownloadRequests.Clear();
        }

        #region Initialization

        private static WebClientEx CreateWebClient()
        {
            WebClientEx client = new WebClientEx()
            {
                Proxy = null,
                RequestTimeout = TimeSpan.FromSeconds(10)
            };
            client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:25.0) Gecko/20100101 Firefox/25.0");
            return client;
        }

        #endregion

        #region Downloading

        private void ActivateDownloadWorker()
        {
            lock (ThreadInitLock)
            {
                for (int i = 0; i < DOWNLOAD_THREAD_COUNT; i++)
                {
                    if (DownloadThreads[i] == null || !DownloadThreads[i].IsAlive)
                    {
                        DownloadThreads[i] = null;
                        DownloadThreads[i] = new Thread(DownloadWorker) { Name = "WebDownloaded" + (i + 1) };
                        DownloadThreads[i].Start();
                        break;
                    }
                }
            }
        }

        private void DownloadWorker()
        {
            Interlocked.Increment(ref RunningThreadCounter);

            var client = CreateWebClient();
            DownloadRequest request = null;
            Stopwatch timer = new Stopwatch();

            if (Disposing)
                return;

            while (DownloadRequests.TryDequeue(out request))
            {
                if (Disposing)
                    break;

                timer.Restart();
                request.BeginProcessing();

                if (request.Canceled)
                {
                    timer.Stop();
                    request.EndProcessing(new DownloadResult(DownloadResultStatus.Canceled));
                    continue;
                }

                DownloadResult downResult = null;

                try
                {
                    byte[] resultData = new byte[0];

                    if (request.PostData != null)
                        resultData = client.UploadValues(request.Url, request.PostData);
                    else
                        resultData = client.DownloadData(request.Url);

                    timer.Stop();
                    downResult = new DownloadResult(resultData, timer.Elapsed);
                }
                catch (WebException ex)
                {
                    timer.Stop();
                    downResult = new DownloadResult(DownloadResultStatus.Failed, timer.Elapsed);
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    downResult = new DownloadResult(DownloadResultStatus.Failed, timer.Elapsed);
                }

                request.EndProcessing(downResult);
            }

            Interlocked.Decrement(ref RunningThreadCounter);

            client.Dispose();
            client = null;
        }

        #endregion

        internal static DownloadRequest EnqueueRequest(DownloadRequest request)
        {
            if (Instance.Disposing)
                return null;
            Instance.DownloadRequests.Enqueue(request);
            request.Enqueued = true;
            return request;
        }

        internal static bool CancelRequest(DownloadRequest request)
        {
            if (Instance.Disposing)
                return true;

            if (Instance.DownloadRequests.Contains(request))
            {
                return Instance.DownloadRequests.TryRemove(request);
            }
            return false;
        }

        private void DownloadRequests_Changed(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add
                && RunningThreadCounter < DOWNLOAD_THREAD_COUNT)
            {
                ActivateDownloadWorker();
            }
        }

        #region Request functions

        #region Create Request

        public static DownloadRequest CreateRequest(string url)
        {
            return CreateRequest(url, null, null);
        }

        public static DownloadRequest CreateRequest(string url, object tag)
        {
            return CreateRequest(url, null, tag);
        }

        public static DownloadRequest CreateRequest(string url, PostData postData)
        {
            return CreateRequest(url, postData, null);
        }

        public static DownloadRequest CreateRequest(string url, PostData postData, object tag)
        {
            return new DownloadRequest(url, postData, tag);
        }

        #endregion

        #region Download

        public static DownloadRequest Download(string url)
        {
            return Download(url, null, null);
        }

        public static DownloadRequest Download(string url, object tag)
        {
            return Download(url, null, tag);
        }

        public static DownloadRequest Download(string url, PostData postData)
        {
            return Download(url, postData, null);
        }

        public static DownloadRequest Download(string url, PostData postData, object tag)
        {
            var request = CreateRequest(url, postData, tag);
            if (AutoStartDownloads)
                request.Start();
            return request;
        }

        #endregion


        public static DownloadGroup DownloadMany(IEnumerable<string> urls)
        {
            return DownloadMany(urls.Select(u => new Tuple<string, PostData, object>(u, null, null)));
        }

        public static DownloadGroup DownloadMany(IEnumerable<Tuple<string, PostData>> requests)
        {
            return DownloadMany(requests.Select(r => new Tuple<string, PostData, object>(r.Item1, r.Item2, null)));
        }

        public static DownloadGroup DownloadMany(IEnumerable<Tuple<string, object>> requests)
        {
            return DownloadMany(requests.Select(r => new Tuple<string, PostData, object>(r.Item1, null, r.Item2)));
        }

        public static DownloadGroup DownloadMany(IEnumerable<Tuple<string, PostData, object>> requests)
        {
            return DownloadMany(requests.Select(r => new DownloadRequest(r.Item1, r.Item2, r.Item3)));
        }

        public static DownloadGroup DownloadMany(IEnumerable<DownloadRequest> requests)
        {
            var group = new DownloadGroup(requests);
            if (AutoStartDownloads)
                group.Start();
            return group;
        }

        #endregion
    }
}
