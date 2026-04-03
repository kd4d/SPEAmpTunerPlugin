using System;
using System.Collections.Generic;
using System.IO.Ports;
using SPEAmpTunerPlugin.MyModel.Internal;

namespace SPEAmpTunerEmulator
{
    /// <summary>
    /// Minimal SPE frame emulator for loopback testing (com0com / null-modem). Uses the same framing
    /// and status layout as <see cref="SpeCommandTranslator"/> / <see cref="SpeFrameCodec"/>.
    /// </summary>
    internal static class Program
    {
        private static int _power = 100;
        private static int _swrAtu10 = 12;
        private static int _swrAnt100 = 115;
        private static int _temp = 42;
        private static bool _ptt;
        private static bool _operate = true;
        private static bool _bypass;
        private static bool _tuning;
        private static int _band = 5;
        private static double _volt = 13.8;
        private static double _amps = 8.5;
        private static int _ant = 1;
        private static int _inp = 1;
        private static int _fault;
        private static int _lvl;

        private static void Main(string[] args)
        {
            string port = args.Length > 0 ? args[0] : "COM2";
            int baud = args.Length > 1 && int.TryParse(args[1], out int b) ? b : 38400;

            Console.WriteLine($"SPEAmpTunerEmulator on {port} @ {baud} baud. Ctrl+C to exit.");
            Console.WriteLine("Pair with the plugin (e.g. plugin on COM1, emulator on COM2 with com0com).");

            using var serial = new SerialPort(port, baud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 50,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            serial.Open();
            var rx = new List<byte>();

            while (true)
            {
                try
                {
                    int n = serial.BytesToRead;
                    if (n > 0)
                    {
                        var buf = new byte[n];
                        int r = serial.Read(buf, 0, n);
                        for (int i = 0; i < r; i++)
                            rx.Add(buf[i]);

                        while (SpeFrameCodec.TryExtractFrame(rx, out byte[]? payload))
                            HandleCommand(serial, payload);
                    }
                    else
                        System.Threading.Thread.Sleep(5);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static void HandleCommand(SerialPort serial, byte[] payload)
        {
            if (payload.Length == 0) return;

            byte cmd = payload[0];
            switch (cmd)
            {
                case 0x01:
                case 0x10:
                    break;
                case 0x02 when payload.Length >= 2:
                    _ant = Math.Clamp((int)payload[1], 1, 4);
                    break;
                case 0x03 when payload.Length >= 2:
                    _inp = Math.Clamp((int)payload[1], 1, 2);
                    break;
                case 0x04 when payload.Length >= 2:
                    _band = Math.Clamp((int)payload[1], 0, 10);
                    break;
                case 0x05:
                    _ptt = true;
                    break;
                case 0x06:
                    _ptt = false;
                    break;
                case 0x07:
                    _operate = false;
                    break;
                case 0x08:
                    _operate = true;
                    break;
                case 0x09:
                    _fault = 0;
                    break;
                case 0x0A:
                    _tuning = true;
                    break;
                case 0x0B:
                    _tuning = false;
                    break;
                case 0x0C:
                    _bypass = true;
                    break;
                case 0x0D:
                    _bypass = false;
                    break;
                case 0x0E:
                    _operate = !_operate;
                    break;
                case 0x0F:
                    _lvl = (_lvl + 1) % 3;
                    break;
                case 0x11:
                    _operate = false;
                    _ptt = false;
                    break;
                default:
                    break;
            }

            byte[] response = SpeCommandTranslator.BuildFullStatusResponse(
                _power,
                _swrAtu10,
                _swrAnt100,
                _temp,
                _ptt,
                _operate,
                _bypass,
                _tuning,
                _band,
                _volt,
                _amps,
                _ant,
                _inp,
                _fault,
                _lvl);

            serial.Write(response, 0, response.Length);
        }
    }
}
