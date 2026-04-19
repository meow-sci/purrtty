using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace StarMap.SimpleExampleMod
{
    public class UdpHandler
    {
        private UdpClient? _udpClient;
        private Thread? _udpThread;
        private bool _udpRunning = false;
        private readonly int _port;
        private readonly object _lock = new object();

        // Data storage
        // Indices: 0=X, 1=Y, 2=Z, 3=Yaw, 4=Pitch, 5=Roll
        private double[] _rawValues = new double[6];
        private double[] _centerValues = new double[6];
        private bool _resetCenterNext = false;

        public bool IsRunning => _udpRunning;
        public int Port => _port;

        public UdpHandler(int port = 4242)
        {
            _port = port;
        }

        public void Start()
        {
            if (_udpRunning) return;

            try
            {
                _udpClient = new UdpClient(_port);
                _udpRunning = true;
                _udpThread = new Thread(ReceiveLoop);
                _udpThread.IsBackground = true;
                _udpThread.Start();
                Console.WriteLine($"UdpHandler - Server started on port {_port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UdpHandler - Failed to start server: {ex.Message}");
            }
        }

        public void Stop()
        {
            _udpRunning = false;
            _udpClient?.Close();
            _udpClient = null;
            // We don't join the thread to avoid blocking the UI/Game thread
        }

        public void RequestCenterReset()
        {
            lock (_lock)
            {
                _resetCenterNext = true;
            }
        }

        /// <summary>
        /// Returns the values relative to the center position.
        /// </summary>
        public double[] GetRelativeValues()
        {
            lock (_lock)
            {
                double[] result = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    result[i] = _rawValues[i] - _centerValues[i];
                }
                return result;
            }
        }

        /// <summary>
        /// Returns the raw values (for debug/display).
        /// </summary>
        public double[] GetRawValues()
        {
            lock (_lock)
            {
                return (double[])_rawValues.Clone();
            }
        }

        private void ReceiveLoop()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, _port);
            while (_udpRunning && _udpClient != null)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref endpoint);
                    if (data.Length == 48) // 6 doubles * 8 bytes
                    {
                        double[] received = new double[6];
                        for (int i = 0; i < 6; i++)
                        {
                            received[i] = BitConverter.ToDouble(data, i * 8);
                        }

                        lock (_lock)
                        {
                            if (_resetCenterNext)
                            {
                                Array.Copy(received, _centerValues, 6);
                                _resetCenterNext = false;
                                Console.WriteLine($"UdpHandler - Center Reset: {string.Join(", ", _centerValues)}");
                            }

                            Array.Copy(received, _rawValues, 6);
                        }
                    }
                }
                catch (SocketException)
                {
                    // Ignore socket exceptions (usually closed socket)
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UdpHandler - Receive Error: {ex.Message}");
                }
            }
        }
    }
}








