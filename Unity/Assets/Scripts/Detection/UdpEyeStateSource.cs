using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Blink.Detection
{
    /// <summary>
    /// Receives <see cref="BlinkPacket"/>s from the Python webcam sidecar on a background thread.
    /// Active while packets keep arriving; goes inactive after <see cref="timeoutSeconds"/> of silence.
    /// </summary>
    public sealed class UdpEyeStateSource : MonoBehaviour, IEyeStateSource
    {
        [SerializeField] int port = 5065;
        [Tooltip("Seconds without a packet before this source counts as disconnected.")]
        [SerializeField] float timeoutSeconds = 0.5f;
        [SerializeField] bool logConnectionChanges = true;

        readonly object _lock = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        EyeState _latest = EyeState.Open;
        double _lastPacketAt = double.NegativeInfinity;
        long _packetCount;

        UdpClient _client;
        Thread _thread;
        volatile bool _running;
        bool _wasActive;

        public string SourceName => $"Webcam (UDP :{port})";
        public long PacketCount { get { lock (_lock) return _packetCount; } }

        public bool IsActive
        {
            get { lock (_lock) return _clock.Elapsed.TotalSeconds - _lastPacketAt < timeoutSeconds; }
        }

        public EyeState Current
        {
            get { lock (_lock) return _latest; }
        }

        void OnEnable()
        {
            try
            {
                _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (SocketException e)
            {
                Debug.LogError($"[Blink] Could not listen on UDP port {port}: {e.Message}");
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "BlinkUdpReceiver" };
            _thread.Start();
        }

        void OnDisable()
        {
            _running = false;
            _client?.Close(); // unblocks Receive()
            _thread?.Join(200);
            _client = null;
            _thread = null;
        }

        void Update()
        {
            bool active = IsActive;
            if (logConnectionChanges && active != _wasActive)
                Debug.Log(active ? $"[Blink] Webcam sidecar connected on UDP {port}" : "[Blink] Webcam sidecar went quiet");
            _wasActive = active;
        }

        void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _client.Receive(ref remote);
                    if (!BlinkPacket.TryParse(Encoding.UTF8.GetString(data), out var packet)) continue;
                    lock (_lock)
                    {
                        _latest = packet.ToEyeState();
                        _lastPacketAt = _clock.Elapsed.TotalSeconds;
                        _packetCount++;
                    }
                }
                catch (SocketException) { /* socket closed or transient error */ }
                catch (ObjectDisposedException) { return; }
            }
        }
    }
}
