using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IndustrialCommDemo.Snap7Server
{
    /// <summary>
    /// Minimal .NET Framework host for the native Snap7Server.
    /// The byte arrays remain pinned for the complete lifetime of the server,
    /// so S7 clients can read and write them as normal DB areas.
    /// </summary>
    internal sealed class Snap7ServerHost : IDisposable
    {
        private const int ServerAreaDb = 5;
        private IntPtr _server;
        private GCHandle _db1Handle;
        private bool _db1Pinned;
        private bool _running;

        public Snap7ServerHost(byte[] db1)
        {
            if (db1 == null || db1.Length == 0)
                throw new ArgumentException("DB1 must contain at least one byte.", nameof(db1));

            Db1 = db1;
        }

        public byte[] Db1 { get; }

        public bool IsRunning => _running;

        public void Start(string address, int port)
        {
            if (_running)
                throw new InvalidOperationException("Snap7Server is already running.");
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("Listen address is required.", nameof(address));
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

            _server = Srv_Create();
            if (_server == IntPtr.Zero)
                throw new InvalidOperationException("Snap7Server native instance could not be created.");

            try
            {
                _db1Handle = GCHandle.Alloc(Db1, GCHandleType.Pinned);
                _db1Pinned = true;
                var registerResult = Srv_RegisterArea(
                    _server,
                    ServerAreaDb,
                    1,
                    _db1Handle.AddrOfPinnedObject(),
                    Db1.Length);
                ThrowIfError(registerResult, "register DB1");

                var localPort = (ushort)port;
                var portResult = Srv_SetParam(_server, 1, ref localPort);
                ThrowIfError(portResult, "设置 Snap7Server 端口");

                var startResult = Srv_StartTo(_server, address);
                ThrowIfError(startResult, "start Snap7Server");
                _running = true;
            }
            catch
            {
                Stop();
                throw;
            }
        }

        public void Stop()
        {
            if (_server == IntPtr.Zero)
                return;

            try
            {
                if (_running)
                    Srv_Stop(_server);
            }
            finally
            {
                _running = false;
                if (_db1Pinned)
                {
                    _db1Handle.Free();
                    _db1Pinned = false;
                }

                var server = _server;
                _server = IntPtr.Zero;
                Srv_Destroy(ref server);
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        ~Snap7ServerHost()
        {
            try { Stop(); } catch { }
        }

        private static void ThrowIfError(int code, string operation)
        {
            if (code == 0)
                return;

            var message = new StringBuilder(256);
            try { Srv_ErrorText(code, message, message.Capacity); } catch { }
            var detail = message.Length == 0 ? string.Format("错误码 0x{0:X8}", code) : message.ToString();
            throw new InvalidOperationException(string.Format("{0}失败：{1}", operation, detail));
        }

        [DllImport("snap7.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Srv_Create();

        [DllImport("snap7.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Srv_Destroy(ref IntPtr server);

        [DllImport("snap7.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Srv_RegisterArea(
            IntPtr server, int areaCode, int index, IntPtr userData, int size);

        [DllImport("snap7.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Srv_SetParam(IntPtr server, int parameter, ref ushort value);

        [DllImport("snap7.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Srv_StartTo(
            IntPtr server, [MarshalAs(UnmanagedType.LPStr)] string address);

        [DllImport("snap7.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Srv_Stop(IntPtr server);

        [DllImport("snap7.dll", CallingConvention = CallingConvention.Cdecl,
            CharSet = CharSet.Ansi)]
        private static extern int Srv_ErrorText(
            int error, StringBuilder errorText, int textSize);
    }
}
