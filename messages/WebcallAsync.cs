using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace messages
{
    // 2013-01-09: created WebcallAsync: http://stackoverflow.com/questions/6644247/simple-custom-event-c-sharp
    // 2013-01-07: added logname everywhere
    class WebcallAsync
    {
        public WebClient webclient;
        public static string ip;   
        public static string backupIp = "10.11.12.13"; // when example.com can't be resolved, this is assigned to the ip
        public string domain = "example.com";
        public static bool domainResolutionAttempted = false;

        public WebcallAsync()
        {
            webclient = new WebClient();
            webclient.Proxy = null; // unbelievable http://stackoverflow.com/questions/4932541/c-sharp-webclient-acting-slow-the-first-time
        }

        public void PopAndKeep(string logname)
        {
            Pop(true, logname);
        }

        public void Pop(string logname)
        {
            Pop(false, logname);
        }

        public void Pop(bool keep = false, string logname = "0")
        {
            var qkeep = keep ? "1" : "0";            
            var url = String.Format(getRoot() + "?action=pop&keep={0}&lognum={1}", qkeep, logname);
            webclient.DownloadStringAsync(new Uri(url));
        }

        public void Submit(string origin, string message, string dest)
        {
            var encodedMessage = Uri.EscapeDataString(message);            
            var url = String.Format(getRoot() + "?action=submit&message={0}&lognum={1}&origin={2}", encodedMessage, dest, origin);
            webclient.DownloadStringAsync(new Uri(url));
        }

        public string getRoot()
        {
            resolveDomain();
            var root = String.Format("http://{0}/messages", ip);
            return root;
        }

        public void resolveDomain()
        {
            // resolve domain here... to avoid the blocking nonsense...
            if (!domainResolutionAttempted)
            {
                try
                {
                    var addresses = Dns.GetHostAddresses(domain);
                    foreach (IPAddress address in addresses)
                    {
                        Trace.WriteLine("resolveDomain: " + address.ToString());
                        ip = address.ToString();
                        break;
                    }
                }
                catch (SocketException)
                {
                    ip = backupIp;
                }
                finally
                {
                    domainResolutionAttempted = true;
                }                
            }
        }
    }

}
