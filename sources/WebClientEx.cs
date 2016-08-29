using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NetDownloader
{
    public class WebClientEx : WebClient
    {
        private TimeSpan _RequestTimeout;

        public TimeSpan RequestTimeout
        {
            get { return _RequestTimeout; }
            set
            {
                _RequestTimeout = value;
            }
        }

        public WebClientEx()
        {
            _RequestTimeout = TimeSpan.Zero;
        }

        protected override WebResponse GetWebResponse(WebRequest request)
        {
            if (RequestTimeout != TimeSpan.Zero)
            {
                request.Timeout = (int)RequestTimeout.TotalMilliseconds;
            }
            return base.GetWebResponse(request);
        }
    }
}
