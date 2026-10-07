using System;
using System.Net;
using System.Net.Sockets;

internal static class NetworkProbe
{
    private static int Main(string[] args)
    {
        try
        {
            IPAddress ip = IPAddress.Parse(args[1]);
            int port = int.Parse(args[2]);
            bool udp = args[0] == "udp";
            using (var socket = new Socket(ip.AddressFamily, udp ? SocketType.Dgram : SocketType.Stream, udp ? ProtocolType.Udp : ProtocolType.Tcp))
            {
                if (udp)
                {
                    socket.Connect(new IPEndPoint(ip, port));
                    // A small standard DNS query for example.com (no credentials or user data).
                    socket.Send(new byte[] { 0x12, 0x34, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0, 0, 1, 0, 1 });
                    // Send() can succeed even when WFP silently drops a datagram.
                    // A matching DNS response proves delivery; a send alone does not.
                    socket.ReceiveTimeout = 2500;
                    byte[] response = new byte[2048];
                    int count = socket.Receive(response);
                    if (count < 12 || response[0] != 0x12 || response[1] != 0x34 || (response[2] & 0x80) == 0)
                    { Console.WriteLine("unreachable: invalid DNS response"); return 2; }
                }
                else
                {
                    var pending = socket.BeginConnect(new IPEndPoint(ip, port), null, null);
                    using (var wait = pending.AsyncWaitHandle)
                        if (!wait.WaitOne(5000)) { Console.WriteLine("unreachable: timeout"); return 2; }
                    socket.EndConnect(pending);
                }
                Console.WriteLine("success"); return 0;
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine(ex.SocketErrorCode == SocketError.AccessDenied ? "blocked" : "unreachable: " + ex.SocketErrorCode);
            return ex.SocketErrorCode == SocketError.AccessDenied ? 1 : 2;
        }
        catch (Exception ex) { Console.WriteLine("error: " + ex.Message); return 3; }
    }
}
