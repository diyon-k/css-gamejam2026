using System;
using System.Diagnostics;
using System.IO;
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
        [Tooltip("Start blink_server/blink_server.py on enable. Untick if you run the sidecar by hand.")]
        [SerializeField] bool launchSidecar = true;

        readonly object _lock = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        EyeState _latest = EyeState.Open;
        double _lastPacketAt = double.NegativeInfinity;
        long _packetCount;

        UdpClient _client;
        Thread _thread;
        volatile bool _running;
        bool _wasActive;
        Process _sidecar;

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

            if (launchSidecar) StartSidecar();
        }

        void OnDisable()
        {
            StopSidecar();
            _running = false;
            _client?.Close(); // unblocks Receive()
            _thread?.Join(200);
            _client = null;
            _thread = null;
        }

        void OnApplicationQuit() => StopSidecar();

        // The venv's python and the one-time setup script differ per OS (see blink_server/setup.*).
#if UNITY_EDITOR_WIN
        const string SidecarPython = "blink_server/.venv/Scripts/python.exe";
        const string SidecarSetup = "powershell -ExecutionPolicy Bypass -File blink_server/setup.ps1";
#else
        const string SidecarPython = "blink_server/.venv/bin/python";
        const string SidecarSetup = "sh blink_server/setup.sh";
#endif

        void StartSidecar()
        {
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX // ponytail: Editor only; standalone builds will launch a PyInstaller exe instead
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string python = Path.Combine(root, SidecarPython);
            if (!File.Exists(python))
            {
                Debug.LogWarning($"[Blink] No webcam sidecar: {python} not found. Run `{SidecarSetup}` once from the repo folder. Without a webcam, blink with blink_server/send_test_blinks.py.");
                return;
            }
            // Don't redirect stdout/stderr: nobody reads them, and a full pipe would freeze the sidecar.
            // --parent-pid: the sidecar exits on its own if the Editor crashes, so it doesn't keep the webcam.
            string args = $"\"{Path.Combine(root, "blink_server/blink_server.py")}\" --parent-pid {Process.GetCurrentProcess().Id}";
            _sidecar = Process.Start(new ProcessStartInfo(python, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
#endif
        }

        void StopSidecar()
        {
            try
            {
                if (_sidecar is { HasExited: false }) _sidecar.Kill(); // the venv launcher takes the real python with it
            }
            catch (Exception) { /* already gone or exiting; never block the socket teardown in OnDisable */ }
            _sidecar?.Dispose();
            _sidecar = null;
        }

        void Update()
        {
            bool active = IsActive;
            if (logConnectionChanges && active != _wasActive)
                Debug.Log(active ? $"[Blink] Webcam sidecar connected on UDP {port}" : "[Blink] Webcam sidecar went quiet");
            _wasActive = active;

            if (_sidecar is { HasExited: true })
            {
                Debug.LogWarning($"[Blink] Webcam sidecar exited (code {_sidecar.ExitCode}). Is the camera in use by another app" +
#if UNITY_EDITOR_OSX
                                 ", or is Unity missing camera access (System Settings > Privacy & Security > Camera)" +
#endif
                                 $"? Run {SidecarPython} blink_server/blink_server.py --preview to see the error. Without a webcam, blink with blink_server/send_test_blinks.py.");
                _sidecar.Dispose();
                _sidecar = null;
            }
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
