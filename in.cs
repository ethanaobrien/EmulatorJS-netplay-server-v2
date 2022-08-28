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

struct WebSocketInfo {
    public byte[] mask = new byte[4];
    public bool Error = false;
    public ulong length = 0;
    private ulong consumed = 0;
    private ulong speed = 1024;
    private Socket handler = null;
    public bool inIsString = false;
    public WebSocketInfo(Socket handler) {
        this.handler = handler;
    }
    public void streamBytes(ArrayList dest) {
        if (dest.Count == 0) return;
        for (int i=0; i<dest.Count; i++) {
            ((WebSocketInfo) dest[i]).writeHeader(this.length-this.consumed, this.inIsString);
        }
        while (this.length != this.consumed) {
            ulong len = this.speed;
            if (this.length-this.consumed < len) {
                len = this.length-this.consumed;
            }
            byte[] data = this.readBytes(len);
            for (int i=0; i<dest.Count; i++) {
                ((WebSocketInfo) dest[i]).writeBytes(data, this.inIsString, false);
            }
        }
    }
    public String readBytesAsString() {
        byte[] res = this.readBytes(this.length);
        return Encoding.UTF8.GetString(res, 0, res.Length);
    }
    public byte[] readBytes() {
        return this.readBytes(this.length);
    }
    public byte[] readBytes(ulong len) {
        if (this.length-this.consumed < len) {
            len = this.length-this.consumed;
        }
        byte[] decoded = new byte[len];
        byte[] bytes = new byte[len];
        handler.Receive(bytes, 0, (int)len, SocketFlags.None);
        for (ulong i=0; i < len; ++i, ++this.consumed) {
            decoded[i] = (byte)(bytes[i] ^ mask[this.consumed % 4]);
        }
        return decoded;
    }
    public void writeString(String data) {
        this.writeBytes(Encoding.UTF8.GetBytes(data), true);
    }
    public void writeBytes(byte[] data) {
        this.writeBytes(data, false);
    }
    public void writeBytes(byte[] data, bool isString) {
        this.writeBytes(data, false);
    }
    public void writeHeader(ulong len, bool isString) {
        int indexStartRawData = -1;
        byte[] frame = new byte[10];

        frame[0] = (byte)(128 + (isString?1:2));
        if (len <= 125)
        {
            frame[1] = (byte)len;
            indexStartRawData = 2;
        }
        else if (len >= 126 && len <= 65535)
        {
            frame[1] = (byte)126;
            frame[2] = (byte)((len >> 8) & 255);
            frame[3] = (byte)(len & 255);
            indexStartRawData = 4;
        }
        else
        {
            frame[1] = (byte)127;
            frame[2] = (byte)((len >> 56) & 255);
            frame[3] = (byte)((len >> 48) & 255);
            frame[4] = (byte)((len >> 40) & 255);
            frame[5] = (byte)((len >> 32) & 255);
            frame[6] = (byte)((len >> 24) & 255);
            frame[7] = (byte)((len >> 16) & 255);
            frame[8] = (byte)((len >> 8) & 255);
            frame[9] = (byte)(len & 255);
            indexStartRawData = 10;
        }
        if (indexStartRawData == -1) return;
        byte[] response = new byte[indexStartRawData];
        for (int i=0; i < indexStartRawData; i++) {
            response[i] = frame[i];
        }
        handler.Send(response);
    }
    public void writeBytes(byte[] data, bool isString, bool header) {
        if (header) {
            writeHeader((ulong)data.Length, isString);
        }
        handler.Send(data);
    }
    public void init() {
        if (handler.Available < 6) {
            this.Error = true;
            return;
        }
        byte[] head = new byte[2];
        handler.Receive(head, 0, 2, SocketFlags.None);
        this.inIsString = ((head[0]-128)==1);
        bool mask = (head[1] & 0b10000000) != 0;
        ulong msglen = (ulong)(head[1] & 0b01111111);
        
        if (msglen == 126) {
            byte[] size = new byte[2];
            handler.Receive(size, 0, 2, SocketFlags.None);
            msglen = (ulong)BitConverter.ToInt16(new byte[] { size[1], size[0] }, 0);
        } else if (msglen == 127) {
            byte[] size = new byte[8];
            handler.Receive(size, 0, 8, SocketFlags.None);
            msglen = BitConverter.ToUInt64(new byte[] { size[7], size[6], size[5], size[4], size[3], size[2], size[1], size[0] }, 0);
        }
        this.length = msglen;
        
        if (mask && msglen != 0) {
            byte[] masks = new byte[4];
            handler.Receive(masks, 0, 4, SocketFlags.None);
            this.mask[0] = masks[0];
            this.mask[1] = masks[1];
            this.mask[2] = masks[2];
            this.mask[3] = masks[3];
        } else {
            this.Error = true;
        }
    }
}

public class Server
{
    private int readChunkSize = 1024 * 1024 * 8;
    private Socket listener = null;
    private Thread mainThread;
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
        try
        {
            running = false;
            this.listener.Shutdown(SocketShutdown.Both);
        }
        catch (Exception e) { }
        try
        {
            this.listener.Close();
        }
        catch (Exception e) { }
        try
        {
            //this.listener.Dispose();
        }
        catch (Exception e) { }
        try
        {
            this.listener = null;
        }
        catch (Exception e) { }
        try
        {
            this.mainThread.Abort();
        }
        catch (Exception e) { }
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
    private void openRoom() {
        
    }
    
    private void onRequest(Object obj)
    {
        Socket handler = (Socket)obj;
       // try
       // {
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

            if (data.IndexOf("Upgrade: websocket") == -1 && consumed)
            {
                string method = data.Split(' ')[0];
                GetHead(handler, method, data);
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
                while (true)
                {
                    while (handler.Available == 0 && handler.Connected) {
                        Thread.Sleep(10);
                    };
                    if (!handler.Connected) break;
                    var connection = new WebSocketInfo(handler);
                    connection.init();
                    if (connection.Error) {
                        handler.Shutdown(SocketShutdown.Both);
                        handler.Close();
                        continue;
                    }
                    var socket = new ArrayList();
                    socket.Add(connection);
                    connection.streamBytes(socket);
                    //connection.writeString("I like the number \"W\" -- "+connection.readBytesAsString());
                    
                    
                   // handler.Send(EncodeWebSocketData(DecodeWebSocketData(bytes), false));
                    //handler.Send(EncodeWebSocketData(Encoding.UTF8.GetBytes("\""+text+"\"? What are you, a booger?"), true));
                    
                }
            }
       // }
       // catch (Exception e)
       // {
       //     Console.WriteLine("Error: {0}", e.ToString());
       // }
        handler.Shutdown(SocketShutdown.Both);
        handler.Close();
    }
    /*
    //https://github.com/MazyModz/CSharp-WebSocket-Server/blob/master/Library/Helpers.cs
    private byte[] DecodeWebSocketData(byte[] bytes) {
        bool mask = (bytes[1] & 0b10000000) != 0;
        ulong msglen = (ulong)(bytes[1] & 0b01111111),
              offset = 2;
        if (msglen == 126)
        {
            msglen = BitConverter.ToUInt64(new byte[] { bytes[3], bytes[2] }, 0);
            offset = 4;
        }
        else if (msglen == 127)
        {
            msglen = BitConverter.ToUInt64(new byte[] { bytes[9], bytes[8], bytes[7], bytes[6], bytes[5], bytes[4], bytes[3], bytes[2] }, 0);
            offset = 10;
        }
        
        if (mask && msglen != 0)
        {
            byte[] decoded = new byte[msglen];
            byte[] masks = new byte[4] { bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3] };
            offset += 4;
            for (ulong i = 0; i < msglen; ++i)
            {
                decoded[i] = (byte)(bytes[offset + i] ^ masks[i % 4]);
            }
            return decoded;
        }
        return new byte[0];
    }
    private byte[] EncodeWebSocketData(byte[] bytesRaw, bool Text) {
        byte[] response;
        byte[] frame = new byte[10];

        int indexStartRawData = -1;
        int length = bytesRaw.Length;

        frame[0] = (byte)(128 + (Text?1:2));
        if (length <= 125)
        {
            frame[1] = (byte)length;
            indexStartRawData = 2;
        }
        else if (length >= 126 && length <= 65535)
        {
            frame[1] = (byte)126;
            frame[2] = (byte)((length >> 8) & 255);
            frame[3] = (byte)(length & 255);
            indexStartRawData = 4;
        }
        else
        {
            frame[1] = (byte)127;
            frame[2] = (byte)((length >> 56) & 255);
            frame[3] = (byte)((length >> 48) & 255);
            frame[4] = (byte)((length >> 40) & 255);
            frame[5] = (byte)((length >> 32) & 255);
            frame[6] = (byte)((length >> 24) & 255);
            frame[7] = (byte)((length >> 16) & 255);
            frame[8] = (byte)((length >> 8) & 255);
            frame[9] = (byte)(length & 255);

            indexStartRawData = 10;
        }

        response = new byte[indexStartRawData + length];

        int i, reponseIdx = 0;

        //Add the frame bytes to the reponse
        for (i = 0; i < indexStartRawData; i++)
        {
            response[reponseIdx] = frame[i];
            reponseIdx++;
        }

        //Add the data bytes to the response
        for (i = 0; i < length; i++)
        {
            response[reponseIdx] = bytesRaw[i];
            reponseIdx++;
        }

        return response;
    }*/
    private void writeHeader(Socket handler, int httpCode, string code, long cl, string ct, string extra)
    {
        //Note: We cannot use Keep alive connection because this is a WebSocket server
        string header = "HTTP/1.1 " + httpCode + " " + code + "\r\nConnection: keep-alive\r\n";
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
    private void error(Socket handler)
    {
        try
        {
            byte[] msg = Encoding.UTF8.GetBytes("500 - Internal Server Error");
            writeHeader(handler, 500, "INTERNAL SERVER ERROR", (long)msg.Length, "", "");
            handler.Send(msg);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error2: {0}", e.ToString());
        }
    }
    private void GetHead(Socket handler, string method, string data)
    {
        string path = "index.html";
        try
        {
            if (File.Exists(path))
            {
                string range = "";
                string d = data.ToLower();
                string delim = "\r\nrange: ";
                int i = d.IndexOf(delim);
                if (i != -1)
                {
                    range = data.Substring(i + delim.Length).Split('\r')[0];
                }
                var file = new FileInfo(path);

                string rheader = "";
                long fileOffset = 0, fileEndOffset = file.Length, len = file.Length + 1;
                long cl = file.Length;
                int code = 200;
                if (range.Length > 0)
                {
                    string ran = range.Split('=')[1];
                    string[] rparts = ran.Split('-');
                    if (rparts[1].Length == 0)
                    {
                        fileOffset = Int32.Parse(rparts[0]);
                        fileEndOffset = file.Length;
                        cl = len - fileOffset - 1;
                        rheader = "Content-Range: bytes " + fileOffset + "-" + (len - 2) + "/" + (len - 1) + "\r\n";
                        code = (fileOffset == 0) ? 200 : 206;
                    }
                    else
                    {
                        fileOffset = Int32.Parse(rparts[0]);
                        fileEndOffset = Int32.Parse(rparts[1]);
                        cl = fileEndOffset - fileOffset + 1;
                        rheader = "Content-Range: bytes " + fileOffset + "-" + (fileEndOffset) + "/" + (len - 1) + "\r\n";
                        code = 206;
                    }
                }
                writeHeader(handler, code, "OK", cl, "text/html; chartset=utf-8", rheader);
                if (method.Equals("HEAD"))
                {
                    return;
                }
                FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                BinaryReader reader = new BinaryReader(stream);
                reader.BaseStream.Position = fileOffset;
                long readLen = 0;
                while (readLen <= cl)
                {
                    int a = this.readChunkSize;
                    if (cl - readLen < this.readChunkSize)
                    {
                        a = (int)(cl - readLen);
                    }
                    if (a == 0) break;
                    readLen += a;
                    byte[] res = reader.ReadBytes(a);
                    try
                    {
                        handler.Send(res);
                    }
                    catch (Exception e)
                    {
                        reader.Close();
                        stream.Close();
                        return;
                    }
                }
                reader.Close();
                stream.Close();
                return;
            }
            byte[] msg = Encoding.UTF8.GetBytes("404 - File Not Found");
            writeHeader(handler, 404, "NOT FOUND", (long)msg.Length, "", "");
            handler.Send(msg);
        }
        catch (Exception e)
        {
            error(handler);
            Console.WriteLine("Error: {0}", e.ToString());
        }
    }
}
