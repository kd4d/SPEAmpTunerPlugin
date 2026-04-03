using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace SPEAmpTunerEmulator
{
    /// <summary>
    /// Minimal SPE serial emulator: finds 6-byte host frames (0x55…0x90 status poll) and replies with CSV lines (same style as kd4d/SPEExpert SpeEmulator).
    /// </summary>
    internal static class Program
    {
        private static bool _operate = true;
        private static readonly byte[] StatusPoll = { 0x55, 0x55, 0x55, 0x01, 0x90, 0x90 };

        private static void Main(string[] args)
        {
            string port = args.Length > 0 ? args[0] : "COM2";
            int baud = args.Length > 1 && int.TryParse(args[1], out int b) ? b : 9600;

            Console.WriteLine($"SPEAmpTunerEmulator on {port} @ {baud} baud (CSV replies to 0x55/0x90 polls). Ctrl+C to exit.");
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

                        ProcessBuffer(serial, rx);
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

        private static void ProcessBuffer(SerialPort serial, List<byte> rx)
        {
            while (TryFindPattern(rx, StatusPoll, out int idx))
            {
                if (idx > 0)
                    rx.RemoveRange(0, idx);
                if (rx.Count < 6)
                    return;

                rx.RemoveRange(0, 6);
                var line = BuildCsvLine() + "\r\n";
                var bytes = Encoding.UTF8.GetBytes(line);
                serial.Write(bytes, 0, bytes.Length);
            }

            if (rx.Count > 4096)
                rx.Clear();
        }

        private static bool TryFindPattern(List<byte> buffer, byte[] pattern, out int index)
        {
            index = -1;
            if (buffer.Count < pattern.Length)
                return false;

            for (int i = 0; i <= buffer.Count - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    index = i;
                    return true;
                }
            }
            return false;
        }

        private static string BuildCsvLine()
        {
            var fields = new string[22];
            for (int i = 0; i < 22; i++)
                fields[i] = "";

            fields[2] = _operate ? "O" : "S";
            fields[3] = "R";
            fields[5] = "A";
            fields[6] = "05";
            fields[7] = "1";
            fields[9] = "5";
            fields[10] = "0";
            fields[11] = "1.2";
            fields[12] = "1.2";
            fields[13] = "48";
            fields[14] = "5";
            fields[15] = "40";
            fields[18] = "N";
            fields[19] = "N";

            return string.Join(",", fields);
        }
    }
}
