using System.Net.Sockets;

namespace main
{
    public class SocketManager
    {
        private readonly Socket _socket;

        public bool Connected => _socket.Connected;
        public int Available => _socket.Available;

        public SocketManager(Socket socket)
        {
            _socket = socket;
        }

        public int Send(byte[] data)
        {
            return _socket.Send(data);
        }

        public int Receive(byte[] bytes)
        {
            return _socket.Receive(bytes);
        }

        public int Receive(byte[] bytes, int offset, int size, SocketFlags flags)
        {
            return _socket.Receive(bytes, offset, size, flags);
        }

        public void Shutdown(SocketShutdown args)
        {
            _socket.Shutdown(args);
        }
        
        public void Close()
        {
            _socket.Close();
        }
    }
}
