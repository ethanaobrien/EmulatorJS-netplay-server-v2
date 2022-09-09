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
    public int MaxUsers;
    public bool HasPassword;
    public String Password;
    public Room(String room, String id, int maxUsers) {
        this.name = room;
        this.id = id;
        this.MaxUsers = maxUsers;
        this.HasPassword = false;
        this.Password = "";
    }
    public Room() {
        this.name = "null";
        this.id = "null";
        this.MaxUsers = 0;
        this.HasPassword = false;
        this.Password = "";
    }
    public void SetPassword(String Password) {
        this.Password = Password;
        this.HasPassword = true;
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
            if (room.id.Equals(site+"-"+id+"-"+name)) {
                return room;
            }
        }
        return new Room();
    }
    public List<Room> GetRooms(String site, String id) {
        List<Room> rooms = new List<Room>();
        foreach (Room room in Rooms) {
            if (room.id.StartsWith(site+"-"+id)) {
                rooms.Add(room);
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
    public String CreateRoom(NetplayUser user, String site, String id, String name, String Password, int MaxUsers) {
        if (RoomExists(name, site, id)) return "Room Already Exists";
        Room room = new Room(name, site+"-"+id+"-"+name, MaxUsers);
        if (Password.Trim().Length > 0) {
            room.SetPassword(Password.Trim());
        }
        room.Users.Add(user);
        Rooms.Add(room);
        return "";
    }
    public String JoinRoom(NetplayUser user, String site, String id, String name, String Password) {
        Room room = GetRoom(site, id, name);
        if (room.name.Equals("null")) return "Room not found";
        if (room.HasPassword) {
            if (!Password.Equals(room.Password)) return "Incorrect Password";
        }
        room.Users.Add(user);
        return "";
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
    public String OpenRoom(NetplayUser user) {
        if (manager.RoomExists(user.RoomName(), user.Site(), user.SiteID())) return "Room Already Exists";
        return manager.CreateRoom(user, user.Site(), user.SiteID(), user.RoomName(), user.Password(), int.Parse(user.RoomLimit()));
    }
    public String JoinRoom(NetplayUser user) {
        if (!manager.RoomExists(user.RoomName(), user.Site(), user.SiteID())) return "Room does not exist!";
        if (manager.RoomFull(user.RoomName(), user.Site(), user.SiteID())) return "Room full";
        return manager.JoinRoom(user, user.Site(), user.SiteID(), user.RoomName(), user.Password());
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
        bool yes = false;
        foreach (Room room in rooms) {
            if (yes) {
                rv += ",";
            }
            rv += (
                "{"+
                "\"name\": \""+room.name.Replace("\"", "\\\"")+"\","+
                "\"users\": "+room.Users.Count+","+
                "\"max_users\": "+room.MaxUsers+","+
                "\"password\": "+(room.HasPassword?"true":"false")+
                "}"
            );
            yes = true;
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
    
    private String RoomLimit1;
    public String RoomLimit() {return this.RoomLimit1;}
    private String Password1;
    public String Password() {return this.Password1;}
    
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
            this.RoomLimit1 = parts[5];
            this.Password1 = parts[6];
            /*
            Console.WriteLine("Open Room");
            Console.WriteLine("Room Name: "+parts[1]);
            Console.WriteLine("User Name: "+parts[2]);
            Console.WriteLine("Site: "+parts[3]);
            Console.WriteLine("Site ID: "+parts[4]);
            */
            String joined = netplay.OpenRoom(user);
            if (joined.Trim().Length > 0) {
                this.Connection.writeString(joined.Trim());
                this.Connection.closeSocket();
                return;
            }
            this.Connection.writeString("Connected");
        } else if (request.Split('\n')[0].Equals("JoinRoom")) {
            this.IsOwner1 = false;
            this.RoomName1 = parts[1];
            this.UserName1 = parts[2];
            this.Site1 = parts[3];
            this.SiteID1 = parts[4];
            this.Password1 = parts[5];
            this.RoomLimit1 = 0;
            String joined = netplay.JoinRoom(user);
            if (joined.Trim().Length > 0) {
                this.Connection.writeString(joined.Trim());
                this.Connection.closeSocket();
                return;
            }
            this.Connection.writeString("Connected");
            Room room = netplay.manager.GetRoom(Site1, SiteID1, RoomName1);
            if (room.name.Equals("null")) {
                this.Connection.writeString("Error Connecting");
                this.Connection.closeSocket();
                return;
            }
            foreach (var client in room.Users) {
                client.Connection.writeString("User Connected: "+this.UserName1);
            }
        } else {
            this.Connection.closeSocket();
            return;
        }
        try {
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
        } catch(Exception e) {}
        
        if (IsOwner1) {
            netplay.DeleteRoom(user);
        } else {
            netplay.DisconnectFromRoom(user);
            Room room = netplay.manager.GetRoom(Site1, SiteID1, RoomName1);
            if (room.name.Equals("null")) return;
            foreach (var client in room.Users) {
                client.Connection.writeString("User Disconnected: "+this.UserName1);
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
            OtherUser.SiteID().Equals(this.SiteID()) &&
            OtherUser.RoomLimit().Equals(this.RoomLimit()) &&
            OtherUser.Password().Equals(this.Password())
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
