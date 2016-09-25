using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace messages
{
    public class MessageParts
    {
        public string TimeStamp = "";
        public string Origin = "";
        public string Message = "";

        public MessageParts(string data)
        {
            if (String.IsNullOrEmpty(data))
            {
                // pass
            }
            else
            {
                var split = data.Split('-');
                if (split.Length >= 3)
                {
                    TimeStamp = split[0].Trim();
                    Origin = split[1].Trim();
                    Message = String.Join("-", split.Skip(2)).Trim();
                    Message = HttpUtility.UrlDecode(Message);
                }
            }
        }

        public bool IsValid()
        {
            return !String.IsNullOrEmpty(Origin) && !String.IsNullOrEmpty(Message);
        }
    }
}
