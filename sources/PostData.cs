using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NetDownloader
{
    [Serializable]
    public class PostData : NameValueCollection
    {

        public PostData()
        {

        }

        public PostData(string[,] values)
        {
            if (values.Rank == 2 && values.GetLength(1) == 2)
            {
                for (int i = 0; i < values.GetLength(0); i++)
                {
                    Add(values[i, 0], values[i, 1]);
                }
            }
        }

        public PostData(params Tuple<string, string>[] values)
        {
            for (int i = 0; i < values.Length; i++)
                Add(values[i].Item1, values[i].Item2);
        }

        public PostData(params string[] values)
        {
            if (values.Length % 2 != 0)
                throw new RankException("Incorrect number of key/value pair");

            for (int i = 0; i < values.Length; i += 2)
                Add(values[i], values[i + 1]);
        }

        public override string ToString()
        {
            string txt = "PostData\r\n{\r\n";
            for (int i = 0; i < Count; i++)
            {
                txt += string.Format("    {0} = {1}\r\n", GetKey(i), this[i]);
            }
            txt += "}";
            return txt;
        }

        public string ToGetUrl()
        {
            string txt = string.Empty;
            for (int i = 0; i < Count; i++)
            {
                txt += string.Format("{0}={1}", HttpUtility.UrlEncode(GetKey(i)), HttpUtility.UrlEncode(this[i]));
                if (i < Count - 1)
                    txt += "&";
            }
            return txt;
        }
    }
}
