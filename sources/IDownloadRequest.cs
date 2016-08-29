using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetDownloader
{
    interface IDownloadRequest
    {
        bool Started { get; }

        bool Canceled { get; }

        bool Completed { get; }

        void Start();

        void Cancel();

        void Wait();

        void Wait(TimeSpan timeout);

        void Wait(int millisecondsTimeout);

    }
}
