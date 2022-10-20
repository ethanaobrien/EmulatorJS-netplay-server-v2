using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace main
{
    public class Server
    {
        private Socket _listener;
        private readonly NetplayManager _netplay;
        private bool _running;
        private Set _settings;
        private struct Set
        {
            public readonly int Port;
            public bool LocalNetwork;
            public Set(int port, bool localNetwork)
            {
                Port = port;
                LocalNetwork = localNetwork;
            }
        }
        public Server(int port, bool localNetwork)
        {
            _netplay = new NetplayManager();
            _listener = null;
            _running = true;
            _settings = new Set(port, localNetwork);
            var mainThread = new Thread(ServerMain);
            mainThread.Start();
        }
        public void SetLocalNetwork(bool yes)
        {
            _settings.LocalNetwork = yes;
        }
        public void Terminate()
        {
            if (_listener == null) return;
            _running = false;
            try
            {
                _listener.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
                // ignored
            }

            try
            {
                _listener.Close();
            }
            catch (Exception)
            {
                // ignored
            }

            _listener = null;
        }
        private void ServerMain()
        {
            try
            {
                var listenHost = _settings.LocalNetwork ? "0.0.0.0" : "127.0.0.1";
                var ipAddress = IPAddress.Parse(listenHost);
                var localEndPoint = new IPEndPoint(ipAddress, _settings.Port);
                _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(localEndPoint);
                _listener.Listen(100); //connection limit
                Console.WriteLine("Listening on http://{0}:{1}", listenHost, _settings.Port);
                while (_running)
                {
                    try
                    {
                        var handler = _listener.Accept();
                        var t = new Thread(OnRequest);
                        t.Start(handler);
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
                _listener.Shutdown(SocketShutdown.Both);
                _listener.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        public string[] GetUrLs()
        {
            if (_settings.LocalNetwork)
            {
                var ipEntry = Dns.GetHostEntry(Dns.GetHostName());
                var addr = ipEntry.AddressList;
                var rv = new string[addr.Length];
                rv[0] = "127.0.0.1";
                for (int i = 0, j = 1; i < addr.Length; i++)
                {
                    if (addr[i].AddressFamily != AddressFamily.InterNetwork) continue;
                    rv[j] = addr[i].ToString();
                    j++;
                }
                return rv;
            }
            else
            {
                return new [] { "127.0.0.1" };
            }
        }
        
        private void OnRequest(object obj)
        {
            var handler = new SocketManager((Socket)obj);
            try
            {
                var consumed = false;
                var data = "";
                while (!consumed)
                {
                    var bytes = new byte[1];
                    var bytesRec = handler.Receive(bytes);
                    if (bytesRec == 0) break;
                    data += Encoding.UTF8.GetString(bytes, 0, bytesRec);
                    consumed = (data.Contains("\r\n\r\n"));
                }

                if (!data.Contains("Sec-WebSocket-Key: ") && consumed)
                {
                    var url = Uri.UnescapeDataString(data.Split(' ')[1].Split('?')[0]);
                    if (url.Equals("/list")) {
                        var msg = Encoding.UTF8.GetBytes(_netplay.ListRooms(data.Split(' ')[1]));
                        WriteHeader(handler, 200, "OK", msg.Length, "application/json", "");
                        handler.Send(msg);
                    } else {
                        var msg = Encoding.UTF8.GetBytes("You shouldn't be here");
                        WriteHeader(handler, 403, "Forbidden", msg.Length, "", "");
                        handler.Send(msg);
                    }
                }
                else if (consumed)
                {
                    //Websocket connection
                    //Console.WriteLine("Websocket connection");
                    //First, We need to do a handshake
                    var response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nAccess-Control-Allow-Origin: *\r\nSec-WebSocket-Accept: " + Convert.ToBase64String(
                        System.Security.Cryptography.SHA1.Create().ComputeHash(
                            Encoding.UTF8.GetBytes(
                                new System.Text.RegularExpressions.Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
                            )
                        )
                    ) + "\r\n\r\n");

                    handler.Send(response);
                    //We are now connected
                
                    var connection = new NetplayUser(handler);
                    connection.Listen(_netplay, connection);
                
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e);
            }
            try {
                handler.Shutdown(SocketShutdown.Both);
                handler.Close();
            }
            catch (Exception)
            {
                // ignored
            }
        }
        private void WriteHeader(SocketManager handler, int httpCode, string code, long cl, string ct, string extra)
        {
            //Note: We cannot use Keep alive connection because this is a WebSocket server
            var header = "HTTP/1.1 " + httpCode + " " + code + "\r\nConnection: close\r\n";
            if (ct.Length > 0)
            {
                header += "Content-type: " + ct + "\r\n";
            }
            if (extra.Length > 0)
            {
                header += extra;
            }
            header += "Access-Control-Allow-Origin: *\r\n";
            header += "Content-Length: " + cl + "\r\n\r\n";
            var msg = Encoding.UTF8.GetBytes(header);
            handler.Send(msg);
        }
    }
}