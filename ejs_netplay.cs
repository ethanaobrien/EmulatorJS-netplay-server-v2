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

public struct Room {
    public List<NetplayUser> Users = new List<NetplayUser>();
    public String name;
    public String id;
    public int MaxUsers = 4;
    public Room(String room, String id) {
        this.name = room;
        this.id = id;
    }
    public Room() {
        this.name = "null";
        this.id = "null";
    }
    public override bool Equals(object other) {
        if(other is null) return false;
        if(!(other is Room)) return false;
        Room otherRoom = (Room) other;
        return otherRoom.id.Equals(this.id);
    }

    public static bool operator ==(Room r1, Room r2) {
        return r1.Equals(r2);
    }

    public static bool operator !=(Room r1, Room r2) {
        return !r1.Equals(r2);
    }
}

public class RoomsManager {
    private List<Room> Rooms = new List<Room>();
    public Room GetRoom(String site, String id, String name) {
        foreach (Room room in Rooms) {
            if (Room.id.Equals(site+"-"+id+"-"+name)) {
                return Room;
            }
        }
        return new Room();
    }
    public List<Room> GetRooms(String site, String id) {
        List<Room> rooms = new List<Room>();
        foreach (Room room in Rooms) {
            if (Room.id.StartsWith(site+"-"+id)) {
                rooms.Add(Room);
            }
        }
        return rooms;
    }
    public bool RoomExists(String room, String site, String id) {
        return !(GetRoom(site, id, room).name.Equals("null"));
    }
    public bool RoomFull(String room, String site, String id) {
        Room room1 = GetRoom(site, id, room);
        if (room1.name.Equals("null")) return true;
        return (room1.Users.Count >= room1.MaxUsers);
    }
    public bool CreateRoom(NetplayUser user, String site, String id, String name) {
        if (RoomExists(name, site, id)) return false;
        Room room = new Room(name, site+"-"+id+"-"+name);
        room.Users.Add(user);
        Rooms.Add(room);
        return true;
    }
    public bool JoinRoom(NetplayUser user, String site, String id, String name) {
        Room room = GetRoom(site, id, name);
        if (room.name.Equals("null")) return false;
        room.Users.Add(user);
        return true;
    }
    public void DeleteRoom(String site, String id, String name) {
        Room room = GetRoom(site, id, name);
        Rooms.Remove(room);
    }
    public void RemoveUser(NetplayUser user, String site, String id, String name, String UserName) {
        Room room = GetRoom(site, id, name);
        if (room.name.Equals("null")) return;
        room.Users.Remove(user);
    }
}

public class NetplayManager {
    public RoomsManager manager = new RoomsManager();
    public NetplayManager() {
        
    }
    public bool OpenRoom(NetplayUser user) {
        if (manager.RoomExists(user.RoomName(), user.Site(), user.SiteID())) return false;
        if (!manager.CreateRoom(user, user.Site(), user.SiteID(), user.RoomName())) return false;
        return true;
    }
    public bool JoinRoom(NetplayUser user) {
        if (!manager.RoomExists(user.RoomName(), user.Site(), user.SiteID())) return false;
        if (manager.RoomFull(user.RoomName(), user.Site(), user.SiteID())) return false;
        return manager.JoinRoom(user, user.Site(), user.SiteID(), user.RoomName());
    }
    public void DeleteRoom(NetplayUser user) {
        manager.DeleteRoom(user.Site(), user.SiteID(), user.RoomName());
    }
    public void DisconnectFromRoom(NetplayUser user) {
        manager.RemoveUser(user, user.Site(), user.SiteID(), user.RoomName(), user.UserName());
    }
    public String ListRooms(String url) {
        if (url.IndexOf("?") == -1) return "[]";
        String site = url.Substring(url.IndexOf("site=")+5).Split('&')[0];
        String id = url.Substring(url.IndexOf("id=")+3).Split('&')[0];
        String rv = "[";
        List<Room> rooms = manager.GetRooms(site, id);
        foreach (Room room in rooms) {
            if (i>0) {
                rv += ",";
            }
            rv += (
                "{"+
                "\"name\": \""+room.name.Replace("\"", "\\\"")+"\","+
                "\"users\": "+room.Users.Count+","+
                "\"max_users\": "+room.MaxUsers+
                "}"
            );
        }
        rv += "]";
        return rv;
    }
}

public class NetplayUser {
    public Socket handler;
    private bool debug = true;
    public WebSocketParser Connection;
    private String RoomName1;
    public String RoomName() {return this.RoomName1;}
    private String UserName1;
    public String UserName() {return this.UserName1;}
    private String Site1;
    public String Site() {return this.Site1;}
    private String SiteID1;
    public String SiteID() {return this.SiteID1;}
    private bool IsOwner1 = false;
    public bool IsOwner() {return this.IsOwner1;}
    public NetplayUser(Socket handler) {
        this.handler = handler;
        this.Connection = new WebSocketParser(handler);
    }
    public void listen(NetplayManager netplay, NetplayUser user) {
        while (!Connection.tryRead()) {
            Thread.Sleep(10);
        };
        if (!Connection.clientConnected()) return;
        //Once we connect the user - we do nothing. Mostly everything is client site
        //(It's cheaper and easier that way).
        String request = Connection.readBytesAsString();
        String[] parts = request.Split('\n');
        if (parts[0].Equals("OpenRoom")) {
            this.IsOwner1 = true;
            this.RoomName1 = parts[1];
            this.UserName1 = parts[2];
            this.Site1 = parts[3];
            this.SiteID1 = parts[4];
            /*
            Console.WriteLine("Open Room");
            Console.WriteLine("Room Name: "+parts[1]);
            Console.WriteLine("User Name: "+parts[2]);
            Console.WriteLine("Site: "+parts[3]);
            Console.WriteLine("Site ID: "+parts[4]);
            */
            bool joined = netplay.OpenRoom(user);
            if (debug) Console.WriteLine("Opened: "+joined);
            if (!joined) {
                this.Connection.writeString("Error Connecting");
                this.Connection.closeSocket();
                return;
            }
        } else if (request.Split('\n')[0].Equals("JoinRoom")) {
            this.IsOwner1 = false;
            this.RoomName1 = parts[1];
            this.UserName1 = parts[2];
            this.Site1 = parts[3];
            this.SiteID1 = parts[4];
            bool joined = netplay.JoinRoom(user);
            if (debug) Console.WriteLine("Joined: "+joined);
            if (!joined) {
                this.Connection.writeString("Error Connecting");
                this.Connection.closeSocket();
                return;
            }
            Room room = netplay.manager.GetRoom(Site1, SiteID1, RoomName1);
            if (room.name.Equals("null")) {
                this.Connection.writeString("Error Connecting");
                this.Connection.closeSocket();
                return;
            }
            foreach (var user in room.Users) {
                user.Connection.writeString("User Connected: "+this.UserName1);
            }
        } else {
            this.Connection.closeSocket();
            return;
        }
        this.Connection.writeString("Connected");
        
        while (true)
        {
            while (!Connection.tryRead()) {
                Thread.Sleep(10);
            };
            if (!Connection.clientConnected()) break;
            
            Room room = netplay.manager.GetRoom(Site1, SiteID1, RoomName1);
            if (room.name.Equals("null")) {
                this.Connection.closeSocket();
                break;
            }
            //We (the server) dont need to monitor the data after the initial connection
            //so we just echo the bytes back to the room.
            Connection.streamBytes(room);
            
        }
        if (IsOwner1) {
            netplay.DeleteRoom(user);
        } else {
            netplay.DisconnectFromRoom(user);
            Room room = netplay.manager.GetRoom(Site1, SiteID1, RoomName1);
            if (room.name.Equals("null")) return;
            foreach (var user in room.Users) {
                user.Connection.writeString("User Disconnected: "+this.UserName1);
            }
        }
    }
    public override bool Equals(object other) {
        NetplayUser OtherUser = other as NetplayUser;
        if(OtherUser is null) {
            return false;
        }
        return (
            OtherUser.RoomName().Equals(this.RoomName()) &&
            OtherUser.UserName().Equals(this.UserName()) &&
            OtherUser.Site().Equals(this.Site()) &&
            OtherUser.SiteID().Equals(this.SiteID())
        );
    }
    public static bool operator ==(NetplayUser u1, NetplayUser u2) {
        if(u1 is null) return false;
        return u1.Equals(u2);
    }
    public static bool operator !=(NetplayUser u1, NetplayUser u2) {
        if(u1 is null) return false;
        return !u1.Equals(u2);
    }
}