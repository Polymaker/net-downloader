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
    public class DownloadRequest : IDownloadRequest
    {
        private PostData _PostData;
        private string _Url;
        private bool _Started;
        private bool _Completed;
        private bool _Canceled;
        private bool _Enqueued;
        private DownloadGroup _Group;
        private DownloadResult _Result;
        private ManualResetEvent CompleteWait;
        private long CompletedDelegateCounter;

        public string Url
        {
            get { return _Url; }
        }

        public PostData PostData
        {
            get { return _PostData; }
        }

        public object Tag { get; set; }

        public DownloadGroup Group
        {
            get { return _Group; }
            internal set { _Group = value; }
        }

        public bool Started
        {
            get { return _Started; }
        }

        public bool Completed
        {
            get { return _Completed; }
        }

        public bool Canceled
        {
            get { return _Canceled; }
        }

        public bool Enqueued
        {
            get { return _Enqueued; }
            internal set { _Enqueued = value; }
        }

        public DownloadResult Result
        {
            get { return _Result; }
        }

        public event DownloadResultHandler DownloadCompleted;

        public event EventHandler DownloadStarted;

        #region Ctors

        public DownloadRequest(string url)
        {
            _Url = url;
            _PostData = null;
            _Group = null;
        }

        public DownloadRequest(string url, PostData postData = null, object tag = null)
        {
            _Url = url;
            _PostData = postData;
            _Group = null;
            Tag = tag;
        }

        internal DownloadRequest(string url, DownloadGroup group, PostData postData = null, object tag = null)
        {
            _Url = url;
            _PostData = postData;
            _Group = group;
            Tag = tag;
        }

        #endregion

        public void Start()
        {
            if (!Enqueued)
            {
                Downloader.EnqueueRequest(this);
            }
        }

        public void Cancel()
        {
            if (Enqueued && !Completed)
            {
                _Canceled = true;
                if (Downloader.CancelRequest(this))
                    EndProcessing(new DownloadResult(DownloadResultStatus.Canceled));
            }
        }

        public void Wait()
        {
            if (!Completed && CompleteWait == null)
            {
                CompleteWait = new ManualResetEvent(false);
                CompleteWait.WaitOne();
                CompleteWait.Dispose();
                CompleteWait = null;
            }
        }

        public void Wait(TimeSpan timeout)
        {
            if (!Completed && CompleteWait == null)
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

        #region Internal download

        internal void BeginProcessing()
        {
            _Started = true;
            //Trace.WriteLine("BeginProcessing on " + Thread.CurrentThread.Name + " at " + DateTime.Now.TimeOfDay);
            if (Group != null)
                Group.RequestEngaged();

            OnDownloadStarted();
        }

        internal void EndProcessing(DownloadResult result)
        {
            //Trace.WriteLine("EndProcessing on " + Thread.CurrentThread.Name + " at " + DateTime.Now.TimeOfDay);
            _Completed = true;
            _Result = result;
            if (Group != null && Started)
                Group.RequestProcessed();

            if (CompleteWait != null)
                CompleteWait.Set();

            OnDownloadCompleted(result);
        }

        #endregion

        #region Events

        private void OnDownloadCompleted(DownloadResult result)
        {
            //Trace.WriteLine("OnDownloadCompleted");
            if (Group == null || !Group.SynchronizedCompletion)
            {
                AsyncHelper.GenericBeginInvoke(DownloadCompleted, this, result);
            }
            else
            {
                CompletedDelegateCounter = -1;
                AsyncHelper.GenericBeginInvoke(DownloadCompleted, this, result,
                    () =>
                    {
                        if (Interlocked.Read(ref CompletedDelegateCounter) == -1)
                        {
                            CompletedDelegateCounter = 0;
                            if (Group != null)
                                Group.RequestDelegateCalled();
                        }
                        Interlocked.Increment(ref CompletedDelegateCounter);
                    },
                    () =>
                    {
                        Interlocked.Decrement(ref CompletedDelegateCounter);
                        if (Group != null && Interlocked.Read(ref CompletedDelegateCounter) == 0)
                            Group.RequestDelegateReleased();
                    });
            }
        }

        private void OnDownloadStarted()
        {
            AsyncHelper.GenericBeginInvoke(DownloadStarted, this, EventArgs.Empty);
        }

        #endregion


    }
}
