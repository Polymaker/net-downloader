using NetDownloader.Collections;
using NetDownloader.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetDownloader
{
    public class DownloadGroup : IDownloadRequest
    {
        //private bool _FinishedAddingRequests;
        private bool _SynchronizedCompletion;
        private ThreadSafeList<DownloadRequest> _Requests;
        private bool _Started;
        private bool _Canceled;
        internal long ProcessingCount = 0;
        internal long RequestsInUse = 0; // number of request still in the DownloadCompleted handler
        internal long ProcessedCount = 0;
        private ManualResetEvent CompleteWait;
        private bool _Enqueued;
        public object Tag { get; set; }

        public IList<DownloadRequest> Requests
        {
            get { return _Requests.AsReadOnly(); }
        }

        public int TotalDownloaded
        {
            get
            {
                return (int)Interlocked.Read(ref ProcessedCount);
            }
        }

        public bool Canceled
        {
            get { return _Canceled; }
        }

        public bool Completed
        {
            get
            {
                //>= because a request may have been engaged and downloading when it was canceled
                return TotalDownloaded >= _Requests.CountTS(r => !r.Canceled);
            }
        }

        public bool Started
        {
            get { return _Started; }
        }

        public bool Enqueued
        {
            get { return _Enqueued/*_Requests.Any(r => r.Enqueued)*/; }
        }

        public bool SynchronizedCompletion
        {
            get { return _SynchronizedCompletion; }
            set
            {
                if (Started)
                    return;
                _SynchronizedCompletion = value;
            }
        }

        //public bool FinishedAddingRequests
        //{
        //    get { return _FinishedAddingRequests; }
        //    set
        //    {
        //        SetFinishedAddingRequests(value);
        //    }
        //}
        

        #region Events...

        /// <summary>
        /// Occur when one request has finished downloading. 
        /// </summary>
        public event DownloadResultHandler RequestDownloaded;
        ///// <summary>
        ///// Occur when all requests has finished downloading.
        ///// </summary>
        //public event DownloadHandler DownloadsCompleted;
        /// <summary>
        /// Occur after all requests downloads handlers have returned.
        /// </summary>
        public event EventHandler RequestsCompleted;
        /// <summary>
        /// Occur after the first request has started downloading.
        /// </summary>
        public event EventHandler DownloadStarted;

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadGroup"/> class.
        /// </summary>
        public DownloadGroup()
        {
            _SynchronizedCompletion = true;
            _Requests = new ThreadSafeList<DownloadRequest>();
            _Started = false;
            _Canceled = false;
            CompleteWait = null;
            Tag = null;
        }

        public DownloadGroup(IEnumerable<DownloadRequest> requests)
        {
            _SynchronizedCompletion = true;
            _Requests = new ThreadSafeList<DownloadRequest>(requests.Where(r => r.Group == null && !r.Enqueued));
            if (_Requests.Count == 0)
                throw new Exception("");
            _Requests.ForEach(r => 
            {
                r.Group = this;
                r.DownloadCompleted += OnRequestDownloaded;
            });
        }

        public void Start()
        {
            if (!Enqueued)
            {
                if (_Requests.Count == 0)
                {
                    throw new Exception("Group must have requests!");
                }
                _Requests.ForEach(r => Downloader.EnqueueRequest(r));
                _Enqueued = true;
            }
        }

        public bool AddRequest(DownloadRequest request)
        {
            if (!SynchronizedCompletion && Completed)
                return false;

            if (Completed && Interlocked.Read(ref RequestsInUse) == 0)
                return false;

            if (request.Group == null && !request.Started && !_Requests.Contains(request))
            {
                request.Group = this;
                request.DownloadCompleted += OnRequestDownloaded;
                _Requests.Add(request);
                if (Enqueued)
                    request.Start();
                return true;
            }
            return false;
        }

        public void Cancel()
        {
            if (Enqueued && !Completed && !Canceled)
            {
                _Canceled = true;
                _Requests.ForEach(r => r.Cancel());
            }
        }

        public void Wait()
        {
            if (Enqueued && !Completed && CompleteWait == null)
            {
                CompleteWait = new ManualResetEvent(false);
                CompleteWait.WaitOne();
                CompleteWait.Dispose();
                CompleteWait = null;
            }
        }

        public void Wait(TimeSpan timeout)
        {
            if (Enqueued && !Completed && CompleteWait == null)
            {
                CompleteWait = new ManualResetEvent(false);
                CompleteWait.WaitOne(timeout);
                CompleteWait.Dispose();
                CompleteWait = null;
            }
        }

        public void Wait(int millisecondsTimeout)
        {
            Wait(TimeSpan.FromMilliseconds(millisecondsTimeout));
        }

        #region Internal Download management

        internal void RequestEngaged()
        {
            //Trace.WriteLine("RequestEngaged");
            Interlocked.Increment(ref ProcessingCount);

            if (!_Started)
            {
                _Started = true;
                AsyncHelper.GenericBeginInvoke(DownloadStarted, this, EventArgs.Empty);
            }
        }

        internal void RequestProcessed()
        {
            //Trace.WriteLine("RequestProcessed");
            Interlocked.Decrement(ref ProcessingCount);
            Interlocked.Increment(ref ProcessedCount);

            if (!SynchronizedCompletion && Completed)
                OnRequestsCompleted();
        }

        internal void RequestDelegateCalled()
        {
            //Trace.WriteLine("RequestDelegateCalled");
            Interlocked.Increment(ref RequestsInUse);
        }

        internal void RequestDelegateReleased()
        {
            //Trace.WriteLine("RequestDelegateReleased");
            Interlocked.Decrement(ref RequestsInUse);
            if (Completed && Interlocked.Read(ref RequestsInUse) == 0)
                OnRequestsCompleted();
        }

        //private void SetFinishedAddingRequests(bool value)
        //{

        //}

        private void OnRequestsCompleted()
        {
            if (CompleteWait != null)
                CompleteWait.Set();
            OnRequestsCompleted(EventArgs.Empty);
        }

        #endregion

        #region Event handlers

        private void OnRequestDownloaded(object sender, DownloadResult result)
        {
            var handler = RequestDownloaded;
            if (handler != null)
            {
                handler(sender, result);
            }
        }

        private void OnRequestsCompleted(EventArgs ea)
        {
            var handler = RequestsCompleted;
            if (handler != null)
            {
                handler(this, ea);
            }
        }

        #endregion
    }
}
