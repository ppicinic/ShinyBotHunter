using System.Net.Sockets;

namespace ShinyBotHunter.Emulators
{
    public abstract class BizHawkClientBase : IAsyncDisposable
    {
        protected TcpClient _tcp = null!;
        protected NetworkStream _stream = null!;
        private readonly string _host;
        private readonly int _port;
        private readonly string _name;

        protected BizHawkClientBase(string name, string host, int port)
        {
            _name = name;
            _host = host;
            _port = port;
        }

        public void Connect()
        {
            _tcp = new TcpClient(_host, _port);
            _stream = _tcp.GetStream();
            Console.WriteLine($"[{_name}] Connected on port {_port}");
        }

        protected abstract void Disconnect();

        public virtual ValueTask DisposeAsync()
        {
            try { Disconnect(); } catch { }
            _stream.Dispose();
            _tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}