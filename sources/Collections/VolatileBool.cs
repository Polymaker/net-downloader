using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetDownloader.Collections
{
    internal struct VolatileBool
    {
        public volatile bool m_value;

        public VolatileBool(bool value)
        {
            this.m_value = value;
        }
    }
}
