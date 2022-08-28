using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;

public class main
{
    public static void Main()
    {
        var server = new Server(8887, true);
    }
}


public class Server
{
    private Socket listener = null;
    private Thread mainThread;
    private NetplayManager netplay = new NetplayManager();
    private struct SET
    {
        public int port;
        public bool LocalNetwork;
        public SET(int port, bool LocalNetwork)
        {
            this.port = port;
            this.LocalNetwork = LocalNetwork;
        }
    }
    private SET Settings;
    public Server(int port, bool localNetwork)
    {
        this.Settings = new SET(port, localNetwork);
        this.mainThread = new Thread(ServerMain);
        this.mainThread.Start();
    }
    bool running = true;
    public void setLocalNetwork(bool yes)
    {
        this.Settings.LocalNetwork = yes;
    }
    public void Terminate()
    {
        if (this.listener == null) return;
        running = false;
        try
        {
            this.listener.Shutdown(SocketShutdown.Both);
        }
        catch (Exception e) { }
        try
        {
            this.listener.Close();
        }
        catch (Exception e) { }
        this.listener = null;
    }
    private void ServerMain()
    {
        try
        {
            string ListenHost = this.Settings.LocalNetwork ? "0.0.0.0" : "127.0.0.1";
            IPAddress ipAddress = IPAddress.Parse(ListenHost);
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, this.Settings.port);
            this.listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            this.listener.Bind(localEndPoint);
            this.listener.Listen(100); //connection limit
            Console.WriteLine("Listening on http://{0}:{1}", ListenHost, this.Settings.port);
            while (running)
            {
                try
                {
                    Socket handler = this.listener.Accept();
                    Thread t = new Thread(onRequest);
                    t.Start(handler);
                }
                catch (Exception e)
                {
                    break;
                }
            }
            this.listener.Shutdown(SocketShutdown.Both);
            this.listener.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }
    public string[] GetURLs()
    {
        if (this.Settings.LocalNetwork)
        {
            IPHostEntry ipEntry = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress[] addr = ipEntry.AddressList;
            string[] rv = new string[addr.Length];
            rv[0] = "127.0.0.1";
            for (int i = 0, j = 1; i < addr.Length; i++)
            {
                if (addr[i].AddressFamily == AddressFamily.InterNetwork)
                {
                    rv[j] = addr[i].ToString();
                    j++;
                }
            }
            return rv;
        }
        else
        {
            return new string[] { "127.0.0.1" };
        }
    }
    private void onRequest(Object obj)
    {
        Socket handler = (Socket)obj;
        try
        {
            bool consumed = false;
            string data = "";
            while (!consumed)
            {
                byte[] bytes = new byte[1];
                int bytesRec = handler.Receive(bytes);
                if (bytesRec == 0) break;
                data += Encoding.UTF8.GetString(bytes, 0, bytesRec);
                consumed = (data.IndexOf("\r\n\r\n") != -1);
            }

            if (data.IndexOf("Sec-WebSocket-Key: ") == -1 && consumed)
            {
                string url = Uri.UnescapeDataString(data.Split(' ')[1].Split('?')[0]);
                if (url.Equals("/list")) {
                    byte[] msg = Encoding.UTF8.GetBytes(this.netplay.ListRooms(data.Split(' ')[1]));
                    writeHeader(handler, 200, "OK", (long)msg.Length, "application/json", "");
                    handler.Send(msg);
                } else {
                    byte[] msg = Encoding.UTF8.GetBytes("You shouldn't be here");
                    writeHeader(handler, 403, "Forbidden", (long)msg.Length, "", "");
                    handler.Send(msg);
                }
            }
            else if (consumed)
            {
                //Websocket connection
                //Console.WriteLine("Websocket connection");
                //First, We need to do a handshake
                Byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nAccess-Control-Allow-Origin: *\r\nSec-WebSocket-Accept: " + Convert.ToBase64String(
                        System.Security.Cryptography.SHA1.Create().ComputeHash(
                            Encoding.UTF8.GetBytes(
                                new System.Text.RegularExpressions.Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
                            )
                        )
                    ) + "\r\n\r\n");

                handler.Send(response);
                //We are now connected
                
                NetplayUser connection = new NetplayUser(handler);
                connection.listen(this.netplay, connection);
                
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: {0}", e.ToString());
        }
        try {
            handler.Shutdown(SocketShutdown.Both);
            handler.Close();
        } catch (Exception e) {}
    }
    private void writeHeader(Socket handler, int httpCode, string code, long cl, string ct, string extra)
    {
        //Note: We cannot use Keep alive connection because this is a WebSocket server
        string header = "HTTP/1.1 " + httpCode + " " + code + "\r\nConnection: close\r\n";
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
        byte[] msg = Encoding.UTF8.GetBytes(header);
        handler.Send(msg);
    }
}
