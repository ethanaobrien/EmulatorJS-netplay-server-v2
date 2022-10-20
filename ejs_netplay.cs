using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace main
{
    public class Room {
        public readonly List<NetplayUser> Users;
        public readonly string Name;
        public readonly string Id;
        public readonly int MaxUsers;
        public bool HasPassword;
        public string Password;
        public Room(string room, string id, int maxUsers) {
            Users = new List<NetplayUser>();
            Name = room;
            Id = id;
            MaxUsers = maxUsers;
            HasPassword = false;
            Password = "";
        }
        public void SetPassword(string password) {
            Password = password;
            HasPassword = true;
        }

        private bool Equals(Room other)
        {
            return Equals(Users, other.Users) && Name == other.Name && Id == other.Id && MaxUsers == other.MaxUsers && HasPassword == other.HasPassword && Password == other.Password;
        }
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((Room) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Users, Name, Id, MaxUsers, HasPassword, Password);
        }

        public static bool operator == (Room r1, Room r2) {
            return r1 is not null && r1.Equals(r2);
        }

        public static bool operator != (Room r1, Room r2) {
            return r1 is not null && !r1.Equals(r2);
        }
    }


    public class RoomsManager {
        private readonly List<Room> _rooms;

        public RoomsManager()
        {
            _rooms = new List<Room>();
        }
    
        public Room GetRoom(string site, string id, string name)
        {
            foreach (var room in _rooms.Where(room => room.Id.Equals(site+"-"+id+"-"+name)))
            {
                return room;
            }

            return null;
        }
        public IEnumerable<Room> GetRooms(string site, string id)
        {
            return _rooms.Where(room => room.Id.StartsWith(site + "-" + id)).ToList();
        }
        public bool RoomExists(string room, string site, string id) {
            return GetRoom(site, id, room) != null;
        }
        public bool RoomFull(string room, string site, string id) {
            var room1 = GetRoom(site, id, room);
            if (room1 == null) return true;
            return (room1.Users.Count >= room1.MaxUsers);
        }
        public string CreateRoom(NetplayUser user, string site, string id, string name, string password, int maxUsers) {
            if (RoomExists(name, site, id)) return "Room Already Exists";
            var room = new Room(name, site+"-"+id+"-"+name, maxUsers);
            if (password.Trim().Length > 0) {
                room.SetPassword(password.Trim());
            }
            room.Users.Add(user);
            _rooms.Add(room);
            return "";
        }
        public string JoinRoom(NetplayUser user, string site, string id, string name, string password) {
            var room = GetRoom(site, id, name);
            if (room == null) return "Room not found";
            if (room.HasPassword) {
                if (!password.Equals(room.Password)) return "Incorrect Password";
            }
            room.Users.Add(user);
            return "";
        }
        public void DeleteRoom(string site, string id, string name) {
            var room = GetRoom(site, id, name);
            _rooms.Remove(room);
        }
        public void RemoveUser(NetplayUser user, string site, string id, string name, string userName) {
            var room = GetRoom(site, id, name);
            if (room == null) return;
            room.Users.Remove(user);
        }
    }

    public class NetplayManager {
        public readonly RoomsManager Manager;
        public NetplayManager()
        {
            Manager = new RoomsManager();
        }
        public string OpenRoom(NetplayUser user)
        {
            return Manager.RoomExists(user.RoomName, user.Site, user.SiteId) ? "Room Already Exists" : Manager.CreateRoom(user, user.Site, user.SiteId, user.RoomName, user.Password, user.RoomLimit);
        }
        public string JoinRoom(NetplayUser user) {
            if (!Manager.RoomExists(user.RoomName, user.Site, user.SiteId)) return "Room does not exist!";
        
            return Manager.RoomFull(user.RoomName, user.Site, user.SiteId) ? "Room full" : Manager.JoinRoom(user, user.Site, user.SiteId, user.RoomName, user.Password);
        }
        public void DeleteRoom(NetplayUser user) {
            Manager.DeleteRoom(user.Site, user.SiteId, user.RoomName);
        }
        public void DisconnectFromRoom(NetplayUser user) {
            Manager.RemoveUser(user, user.Site, user.SiteId, user.RoomName, user.UserName);
        }
        public string ListRooms(string url) {
            if (!url.Contains('?')) return "[]";
            var site = url[(url.IndexOf("site=", StringComparison.Ordinal)+5)..].Split('&')[0];
            var id = url[(url.IndexOf("id=", StringComparison.Ordinal)+3)..].Split('&')[0];
            var rv = "[";
            var rooms = Manager.GetRooms(site, id);
            var yes = false;
            foreach (Room room in rooms) {
                if (yes) {
                    rv += ",";
                }
                rv += (
                    "{"+
                    "\"name\": \""+room.Name.Replace("\"", "\\\"")+"\","+
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
        private readonly SocketManager _handler;
        private const bool Debug = true;
        public readonly WebSocketParser Connection;
        public string RoomName { get; private set; }
        public string UserName { get; private set; }
        public string Site { get; private set; }
        public string SiteId { get; private set; }
        public int RoomLimit { get; private set; }
        public string Password { get; private set; }
        private bool IsOwner { get; set; }
    
        public NetplayUser(SocketManager handler) {
            _handler = handler;
            Connection = new WebSocketParser(handler);
            IsOwner = false;
        }
        public void Listen(NetplayManager netplay, NetplayUser user) {
            while (!Connection.TryRead()) {
                Thread.Sleep(10);
            }

            if (!Connection.ClientConnected()) return;
            //Once we connect the user - we do nothing. Mostly everything is client site
            //(It's cheaper and easier that way).
            var request = Connection.ReadBytesAsString();
            var parts = request.Split('\n');
            if (parts[0].Equals("OpenRoom")) {
                IsOwner = true;
                RoomName = parts[1];
                UserName = parts[2];
                Site = parts[3];
                SiteId = parts[4];
                RoomLimit = int.Parse(parts[5]);
                Password = parts[6];
                /*
            Console.WriteLine("Open Room");
            Console.WriteLine("Room Name: "+parts[1]);
            Console.WriteLine("User Name: "+parts[2]);
            Console.WriteLine("Site: "+parts[3]);
            Console.WriteLine("Site ID: "+parts[4]);
            */
                var joined = netplay.OpenRoom(user);
                if (joined.Trim().Length > 0) {
                    Connection.WriteString(joined.Trim());
                    Connection.CloseSocket();
                    return;
                }
                Connection.WriteString("Connected");
            } else if (request.Split('\n')[0].Equals("JoinRoom")) {
                IsOwner = false;
                RoomName = parts[1];
                UserName = parts[2];
                Site = parts[3];
                SiteId = parts[4];
                Password = parts[5];
                RoomLimit = 0;
                var joined = netplay.JoinRoom(user);
                if (joined.Trim().Length > 0) {
                    Connection.WriteString(joined.Trim());
                    Connection.CloseSocket();
                    return;
                }
                Connection.WriteString("Connected");
                var room = netplay.Manager.GetRoom(Site, SiteId, RoomName);
                if (room == null) {
                    Connection.WriteString("Error Connecting");
                    Connection.CloseSocket();
                    return;
                }
                foreach (var client in room.Users) {
                    client.Connection.WriteString("User Connected: "+UserName);
                }
            } else {
                Connection.CloseSocket();
                return;
            }
            try {
                while (true)
                {
                    while (!Connection.TryRead()) {
                        Thread.Sleep(10);
                    }

                    if (!Connection.ClientConnected()) break;
                
                    var room = netplay.Manager.GetRoom(Site, SiteId, RoomName);
                    if (room == null) {
                        Connection.CloseSocket();
                        break;
                    }
                    //We (the server) dont need to monitor the data after the initial connection
                    //so we just echo the bytes back to the room.
                    Connection.StreamBytes(room);
                
                }
            }
            catch (Exception)
            {
                // ignored
            }

            if (IsOwner) {
                netplay.DeleteRoom(user);
            } else {
                netplay.DisconnectFromRoom(user);
                var room = netplay.Manager.GetRoom(Site, SiteId, RoomName);
                if (room.Name.Equals("null")) return;
                foreach (var client in room.Users) {
                    client.Connection.WriteString("User Disconnected: "+UserName);
                }
            }
        }
        public static bool operator == (NetplayUser u1, NetplayUser u2)
        {
            return u1 is not null && u1.Equals(u2);
        }
        public static bool operator != (NetplayUser u1, NetplayUser u2) {
            if (u1 is null) return false;
            return !u1.Equals(u2);
        }
    
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((NetplayUser) obj);
        }

        private bool Equals(NetplayUser other)
        {
            return Equals(_handler, other._handler) && Equals(Connection, other.Connection) && RoomName == other.RoomName && UserName == other.UserName && Site == other.Site && SiteId == other.SiteId && RoomLimit == other.RoomLimit && Password == other.Password && IsOwner == other.IsOwner;
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(_handler);
            hashCode.Add(Connection);
            hashCode.Add(RoomName);
            hashCode.Add(UserName);
            hashCode.Add(Site);
            hashCode.Add(SiteId);
            hashCode.Add(RoomLimit);
            hashCode.Add(Password);
            hashCode.Add(IsOwner);
            return hashCode.ToHashCode();
        }
    }
}