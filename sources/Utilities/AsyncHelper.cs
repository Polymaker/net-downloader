using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetDownloader.Utilities
{
    internal static class AsyncHelper
    {

        public static void GenericBeginInvoke(this Delegate eventDelegate, object sender, object eventArgs)
        {
            if (eventDelegate != null)
            {
                var eventListeners = eventDelegate.GetInvocationList();
                var beginInvokeMethod = eventDelegate.GetType().GetMethod("BeginInvoke");
                foreach (var listener in eventListeners)
                {
                    beginInvokeMethod.Invoke(listener, new object[] { sender, eventArgs, (AsyncCallback)GenericEndInvoke, null });
                }
            }
        }

        public static void GenericBeginInvoke(this Delegate eventDelegate, object sender, object eventArgs, Action beginInvoke, Action endInvoke)
        {
            if (eventDelegate != null)
            {
                var eventListeners = eventDelegate.GetInvocationList();
                var beginInvokeMethod = eventDelegate.GetType().GetMethod("BeginInvoke");

                foreach (var listener in eventListeners)
                {
                    if (beginInvoke != null)
                        beginInvoke();
                    beginInvokeMethod.Invoke(listener, new object[] { sender, eventArgs, (AsyncCallback)GenericEndInvoke, endInvoke });
                }
            }

        }

        private static void GenericEndInvoke(IAsyncResult iar)
        {
            var ar = (System.Runtime.Remoting.Messaging.AsyncResult)iar;
            try
            {
                var handlerType = ar.AsyncDelegate.GetType();
                var endInvokeMethod = handlerType.GetMethod("EndInvoke");
                endInvokeMethod.Invoke(ar.AsyncDelegate, new object[] { iar });
            }
            catch { }

            if (iar.AsyncState is Action)
            {
                (iar.AsyncState as Action).Invoke();
            }
        }

    }
}
