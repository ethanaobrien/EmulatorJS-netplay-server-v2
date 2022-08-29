using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

public class WebSocketParser {
    public byte[] mask = new byte[4];
    public ulong length = 0;
    private ulong consumed = 0;
    private ulong speed = 1024;
    public Socket handler = null;
    public bool inIsString = false;
    public bool sendingData = false;
    public void setSendingData(bool tr) {
        this.sendingData = tr;
    }
    public WebSocketParser(Socket handler) {
        this.handler = handler;
    }
    public void streamBytes(Room room) {
        var dest = new List<WebSocketParser>();
        for (int i=0; i<room.Users.Count; i++) {
            dest.Add(room.Users[i].Connection);
        }
        streamBytes(dest);
    }
    public void streamBytes(List<WebSocketParser> dest) {
        if (dest.Count == 0) return;
        foreach(var parser in dest) {
            //Wait for all WebSockets to be ready to send data
            while (parser.sendingData) {
                Thread.Sleep(10);
            }
            parser.setSendingData(true);
            parser.writeHeader(this.length-this.consumed, this.inIsString);
        }
        while (this.length != this.consumed) {
            ulong len = this.speed;
            if (this.length-this.consumed < len) {
                len = this.length-this.consumed;
            }
            byte[] data = this.readBytes(len);
            foreach(var parser in dest) {
                parser.writeBytes(data, this.inIsString, false);
            }
        }
        foreach(var parser in dest) {
            parser.setSendingData(false);
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
        sendingData = true;
        this.writeBytes(Encoding.UTF8.GetBytes(data), true, true);
        sendingData = false;
    }
    public void writeBytes(byte[] data) {
        sendingData = true;
        this.writeBytes(data, false, true);
        sendingData = false;
    }
    public void writeBytes(byte[] data, bool isString) {
        sendingData = true;
        this.writeBytes(data, false, true);
        sendingData = false;
    }
    public void writeBytes(byte[] data, bool isString, bool header) {
        if (header) {
            writeHeader((ulong)data.Length, isString);
        }
        handler.Send(data);
    }
    public void writeHeader(ulong len, bool isString) {
        //Credit to https://github.com/MazyModz/CSharp-WebSocket-Server/blob/master/Library/Helpers.cs
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
    public bool clientConnected() {
        return handler.Connected;
    }
    public void closeSocket() {
        try {
            handler.Shutdown(SocketShutdown.Both);
            handler.Close();
        } catch(Exception e) {}
    }
    public bool tryRead() {
        if (handler.Available == 0 && handler.Connected) return false;
        if (!handler.Connected) return true;
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
        this.consumed = 0;
        
        if (mask && msglen != 0) {
            byte[] masks = new byte[4];
            handler.Receive(masks, 0, 4, SocketFlags.None);
            this.mask[0] = masks[0];
            this.mask[1] = masks[1];
            this.mask[2] = masks[2];
            this.mask[3] = masks[3];
        } else {
            this.closeSocket();
        }
        return true;
    }
    public override int GetHashCode() {
        return this.handler.GetHashCode();
    }
    public override bool Equals(object otherObj) {
        WebSocketParser other = otherObj as WebSocketParser;
        if(other == null) {
            return false;
        }
        return other.handler.Equals(handler);
    }

    public static bool operator ==(WebSocketParser p1, WebSocketParser p2) {
        if(p1 is null) return false;
        return p1.Equals(p2);
    }

     public static bool operator !=(WebSocketParser p1, WebSocketParser p2) {
        if(p1 is null) return false;
        return !p1.Equals(p2);
    }
}
