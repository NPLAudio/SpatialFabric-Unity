// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NPLAudio.SpatialFabric.Osc
{
    public readonly struct ReceivedOsc
    {
        public readonly OscMessage Message;
        public readonly IPEndPoint Peer;
        public ReceivedOsc(OscMessage message, IPEndPoint peer) { Message = message; Peer = peer; }
    }
    /// <summary>One explicit exact-bind listener. Worker owns no Unity objects; dispatch on the main thread.</summary>
    public sealed class OscEndpoint : IDisposable
    {
        readonly object gate = new object();
        readonly Queue<ReceivedOsc> queue = new Queue<ReceivedOsc>();
        Socket receiver, sender;
        Thread worker;
        volatile bool stopped;
        int rejected, dropped;
        public int Rejected => Volatile.Read(ref rejected);
        public int Dropped => Volatile.Read(ref dropped);
        public IPEndPoint Bound { get; private set; }
        public bool IsListening => receiver != null && worker != null && worker.IsAlive && !stopped;
        public string LastReceiverError { get; private set; } = "";
        public static IPEndPoint Address(string ip, int port, bool listen = false)
        {
            if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != ip || port < 1 || port > 65535) throw new ArgumentException("Literal IPv4 and port 1..65535 required");
            byte[] b = address.GetAddressBytes();
            if (b[0] >= 224 || (!listen && address.Equals(IPAddress.Any))) throw new ArgumentException("Unicast destination required");
            return new IPEndPoint(address, port);
        }
        public void Start(string ip, int port)
        {
            if (receiver != null) throw new InvalidOperationException("Stop the existing listener before rebinding");
            var target = Address(ip, port, true);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.ExclusiveAddressUse = true; socket.ReceiveBufferSize = 1024 * 1024; socket.ReceiveTimeout = 100;
                socket.Bind(target);
            }
            catch { socket.Dispose(); throw; }
            receiver = socket; Bound = (IPEndPoint)socket.LocalEndPoint; stopped = false; LastReceiverError = "";
            worker = new Thread(() => Receive(socket)) { IsBackground = true, Name = "SpatialFabric OSC input" }; worker.Start();
        }
        void Receive(Socket socket)
        {
            var buffer = new byte[OscCodec.MaxPacket + 1];
            while (!stopped)
            {
                EndPoint peer = new IPEndPoint(IPAddress.Any, 0); int count;
                try { count = socket.ReceiveFrom(buffer, ref peer); }
                catch (SocketException error) { if (error.SocketErrorCode == SocketError.TimedOut || error.SocketErrorCode == SocketError.WouldBlock) continue; if (!stopped) LastReceiverError = error.Message; break; }
                catch (ObjectDisposedException) { break; }
                try
                {
                    var packet = new byte[count]; Buffer.BlockCopy(buffer, 0, packet, 0, count);
                    var messages = OscCodec.Decode(packet); // Entire bundle validates before queueing anything.
                    lock (gate)
                    {
                        foreach (var message in messages)
                        {
                            if (queue.Count >= 2048) { Interlocked.Increment(ref dropped); continue; }
                            queue.Enqueue(new ReceivedOsc(message, (IPEndPoint)peer));
                        }
                    }
                }
                catch (Exception error) when (error is FormatException || error is ArgumentException) { Interlocked.Increment(ref rejected); }
            }
        }
        public List<ReceivedOsc> Drain(int maximum = 256)
        {
            var result = new List<ReceivedOsc>();
            lock (gate) while (queue.Count > 0 && result.Count < maximum) result.Add(queue.Dequeue());
            return result;
        }
        public void Send(string ip, int port, OscMessage message)
        {
            var destination = Address(ip, port); byte[] packet = OscCodec.Encode(message);
            if (sender == null) sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            if (sender.SendTo(packet, destination) != packet.Length) throw new SocketException();
        }
        public void Stop()
        {
            stopped = true; receiver?.Dispose(); receiver = null;
            worker?.Join(1000); worker = null; Bound = null;
            lock (gate) queue.Clear();
        }
        public void Dispose() { Stop(); sender?.Dispose(); sender = null; }
    }
}
