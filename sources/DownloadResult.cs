using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetDownloader
{
    public class DownloadResult
    {
        private byte[] _Data;
        private TimeSpan _DownloadTime;
        private DownloadResultStatus _Status;

        public DownloadResultStatus Status
        {
            get { return _Status; }
        }

        public byte[] Data
        {
            get { return _Data; }
        }

        public TimeSpan DownloadTime
        {
            get { return _DownloadTime; }
        }

        internal DownloadResult()
        {
            _DownloadTime = TimeSpan.Zero;
            _Status = DownloadResultStatus.Succeeded;
            _Data = new byte[0];
        }

        internal DownloadResult(TimeSpan downloadTime)
        {
            _DownloadTime = downloadTime;
            _Status = DownloadResultStatus.Succeeded;
            _Data = new byte[0];
        }

        internal DownloadResult(byte[] data, TimeSpan downloadTime)
        {
            _DownloadTime = downloadTime;
            _Status = DownloadResultStatus.Succeeded;
            _Data = data;
        }

        internal DownloadResult(DownloadResultStatus failReason)
        {
            _DownloadTime = TimeSpan.Zero;
            _Status = failReason;
        }

        internal DownloadResult(DownloadResultStatus failReason, TimeSpan downloadTime)
        {
            _DownloadTime = downloadTime;
            _Status = failReason;
        }
    }
}
