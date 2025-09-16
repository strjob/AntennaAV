using AntennaAV.Models;
using HarfBuzzSharp;
using RJCP.IO.Ports;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AntennaAV.Services
{
    public class ComPortManager : IComPortService
    {
        private SerialPortStream? _port;
        private Thread? _readThread;
        private volatile bool _reading = false;
        public List<AntennaData> AllRecordedData { get; } = new();

        public readonly ConcurrentQueue<AntennaData> DataQueue = new();
        public readonly ConcurrentQueue<string> CalibrationQueue = new();

        public string? ConnectedPortName { get; private set; }

        private readonly int _baudRate;
        public bool IsOpen => _port?.IsOpen == true;
        public IEnumerable<AntennaData> GetAllRecordedData() => AllRecordedData;

        public bool TryDequeue(out AntennaData data)
        {
            data = default!;

            if (DataQueue.TryDequeue(out var result) && result != null)
            {
                data = result;
                return true;
            }

            return false;
        }

        public ComPortManager(int baudRate = 115200)
        {
            _baudRate = baudRate;
        }

        public enum ConnectResult
        {
            Success,
            DeviceNotResponding,
            InvalidResponse,
            PortBusy,
            PortNotFound,
            ExceptionOccurred
        }

        public ConnectResult AutoDetectAndConnect()
        {
            using (var port = new SerialPortStream())
            {
                var portNames = port.GetPortNames();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    portNames = portNames.Where(p => !p.Equals("COM1", StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    portNames = portNames.Where(p => p.StartsWith("/dev/ttyUSB") || p.StartsWith("/dev/ttyACM")).ToArray();
                }
                foreach (string portName in portNames)
                {
                    var result = ConnectToPort(portName);
                    if (result == ConnectResult.Success)
                        return ConnectResult.Success;
                }
            }
            return ConnectResult.DeviceNotResponding;
        }

        public IEnumerable<string> GetAvailablePortNames()
        {
            using (var port = new SerialPortStream())
            {
                return port.GetPortNames();
            }
        }

        private readonly object _portLock = new object();

        public ConnectResult ConnectToPort(string portName)
        {
            lock (_portLock)
            {
                const int maxAttempts = 3;
                SerialPortStream? temp = null;
                try
                {
                    using (var port = new SerialPortStream())
                    {
                        var portNames = port.GetPortNames();
                        if (!portNames.Contains(portName))
                        {
                            return ConnectResult.PortNotFound;
                        }
                    }

                    if (_port != null)
                    {
                        if (_port.IsOpen)
                            _port.Close();
                        _port.Dispose();
                        _port = null;
                    }

                    temp = new SerialPortStream(portName, _baudRate, 8, Parity.None, StopBits.One)
                    {
                        ReadTimeout = 100,
                        WriteTimeout = 50
                    };
                    if (!TryOpenPortWithTimeout(temp, 500)) // 0.5 секунд
                    {
                        return ConnectResult.DeviceNotResponding;
                    }
                    temp.DiscardInBuffer();

                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        temp.Write("#ANT/xx/W/OFF$");
                        Thread.Sleep(50);
                        temp.DiscardInBuffer();
                        temp.Write("#AA/xx/R$");

                        Thread.Sleep(50);

                        // Читаем всё, что есть в буфере
                        string buffer = temp.ReadExisting();

                        // Ищем полное сообщение
                        int start = buffer.IndexOf('#');
                        int end = buffer.IndexOf('$', start + 1);
                        string? response = null;
                        if (start >= 0 && end > start)
                        {
                            response = buffer.Substring(start, end - start + 1);
                            buffer = buffer.Substring(end + 1); // удаляем обработанное сообщение
                        }

                        if (response != null && response.StartsWith("#xx/A"))
                        {
                            _port = temp;
                            _port.DiscardInBuffer();
                            ConnectedPortName = portName;
                            StartMessaging();
                            return ConnectResult.Success;
                        }

                        if (attempt == maxAttempts)
                        {
                            if (response == null)
                            {
                                return ConnectResult.DeviceNotResponding;
                            }
                            else
                            {
                                return ConnectResult.InvalidResponse;
                            }
                        }

                        Thread.Sleep(50);
                    }
                    return ConnectResult.ExceptionOccurred;
                }
                catch (UnauthorizedAccessException)
                {
                    return ConnectResult.PortBusy;
                }
                catch (Exception)
                {
                    return ConnectResult.ExceptionOccurred;
                }
                finally
                {
                    // Если не удалось подключиться, гарантируем освобождение порта
                    if (_port == null && temp != null)
                    {
                        if (temp.IsOpen)
                            temp.Close();
                        temp.Dispose();
                    }
                }
            }
        }

        public void StartReading()
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("COM-порт не открыт");

            _reading = true;

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true
            };
            _readThread.Start();
        }

        public void StopReading()
        {
             StopMessaging();
            _reading = false;
            _readThread?.Join();
            _port?.DiscardInBuffer();
            _port?.Close();
        }

        public bool StartMessaging() => WriteCommand("ON", "ANT");
        public bool StopMessaging() => WriteCommand("OFF", "ANT");
        public bool StopAntenna(string ant)
        {
           return WriteCommand(ant + "=STOP", "ANT");
        }

        public bool SetAdcGain(int gain)
        {
            return WriteCommand($"P={gain}", "ANT");
        }

        public bool SetDefaultRFGain(int gain)
        {
            return WriteCommand($"D={gain}", "ANT");
        }

        public bool SetGenState(bool isOff)
        {
            if(isOff) return SendCommand($"P/1", "AA");
            else return SendCommand($"P/0", "AA");
        }

        public bool SetAntennaAngle(double angle, string antenna, string direction)
        {
            if (angle < 0.0 || angle > 359.94)
                throw new ArgumentOutOfRangeException(nameof(angle), "Угол должен быть от 0.0 до 359.9");

            if (antenna != "R" && antenna != "T")
                throw new ArgumentOutOfRangeException(nameof(antenna), "Неверное имя антенны");

            if (direction != "+" && direction != "-" && direction != "S" && direction != "G" && direction != "Z")
                throw new ArgumentOutOfRangeException(nameof(direction), "Неверное направление");

            int value = (int)Math.Round(angle * 10);
            return WriteCommand($"{antenna}={direction}{value}", "ANT");
        }

        public bool ReadCalibration() => SendCommand("R", "AA");
        public bool ClearCalibration() => SendCommand("C", "AA");
        public bool SaveCalibration() => SendCommand("S", "AA");

        public bool SetCalibrationPoint(double value)
        {
            return WriteCommand($"{value.ToString("0.####", CultureInfo.InvariantCulture)}", "AA");
        }

        public bool CalibrateZeroSVCH()
        {
            return SendCommand("K", "AA");
        }

        public bool WriteCommand(string body, string prefix)
        {
            if (_port != null && _port.IsOpen)
            {
                string fullCommand = $"#{prefix}/xx/W/{body}$";
                Debug.WriteLine(fullCommand);
                _port.Write(fullCommand);
                return true;
            }
            return false;
        }

        public bool SendCommand(string body, string prefix)
        {
            if (_port != null && _port.IsOpen)
            {
                string fullCommand = $"#{prefix}/xx/{body}$";
                Debug.WriteLine(fullCommand);
                _port.Write(fullCommand);
                return true;
            }
            return false;
        }

        private void ReadLoop()
        {
            var buffer = new StringBuilder(1024);
            const int maxBufferSize = 10000;
            int errorCount = 0;
            const int maxErrors = 100;
            const int delayMs = 15;

            while (_reading && _port != null && _port.IsOpen)
            {
                try
                {
                    if (_port.BytesToRead > 0)
                    {
                        string data = _port.ReadExisting();
                        buffer.Append(data);

                        if (buffer.Length > maxBufferSize)
                        {
                            Debug.WriteLine($"Buffer overflow (length={buffer.Length}), trimming to last #");
                            int lastHash = buffer.ToString().LastIndexOf('#');
                            if (lastHash >= 0)
                                buffer.Remove(0, lastHash);
                            else
                                buffer.Clear();
                            errorCount++;
                        }

                        string currentBuffer = buffer.ToString();
                        int index = 0;

                        while (index < currentBuffer.Length)
                        {
                            int start = currentBuffer.IndexOf('#', index);
                            if (start == -1) break;
                            int end = currentBuffer.IndexOf('$', start + 1);
                            if (end == -1) break;

                            string message = currentBuffer.Substring(start, end - start + 1);
                            var parsed = ParseDataString(message);
                            if (parsed != null)
                            {
                                DataQueue.Enqueue(parsed);
                                errorCount = 0;
                            }
                            else
                            {
                                Debug.WriteLine($"Invalid message format: {message}");
                                errorCount++;
                            }

                            index = end + 1;
                        }

                        if (index > 0)
                        {
                            buffer.Remove(0, index);
                        }

                        if (errorCount >= maxErrors)
                        {
                            Debug.WriteLine($"Too many errors, clearing buffer and discarding OS buffer (BytesToRead={_port.BytesToRead})");
                            buffer.Clear();
                            Thread.Sleep(1000);
                            _port.DiscardInBuffer();
                            errorCount = 0;
                        }
                    }
                    else
                    {
                        Thread.Sleep(delayMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ReadLoop error: {ex.Message} (Type: {ex.GetType().Name}, BytesToRead={_port.BytesToRead})");
                    errorCount++;
                    if (errorCount >= maxErrors)
                    {
                        Debug.WriteLine($"Too many read errors, clearing buffer and discarding OS buffer (BytesToRead={_port.BytesToRead})");
                        buffer.Clear();
                        _port.DiscardInBuffer();
                        Thread.Sleep(1000);
                        errorCount = 0;
                    }
                }
            }
        }

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private AntennaData? ParseDataString(string input)
        {
            if (input.StartsWith("#xx/ANT", StringComparison.Ordinal) || !input.EndsWith("$"))
            {

                var trimmed = input.Trim('#', '$');
                var parts = trimmed.Split('/');

                if (parts.Length < 12) return null;

                if (!int.TryParse(parts[3], out int systick)) return null;
                if (!int.TryParse(parts[4], out int receiverAngleDeg10)) return null;
                if (!int.TryParse(parts[5], out int transmitterAngleDeg10)) return null;
                if (!int.TryParse(parts[6], out int rxAntennaCounter)) return null;
                if (!int.TryParse(parts[7], out int txAntennaCounter)) return null;
                if (!double.TryParse(parts[8], NumberStyles.Float, Invariant, out double powerDbm)) return null;
                if (!double.TryParse(parts[9], NumberStyles.Float, Invariant, out double voltage)) return null;
                if (!int.TryParse(parts[10], out int antennaType)) return null;
                if (!int.TryParse(parts[11], out int modeAutoHand)) return null;
                if (!int.TryParse(parts[12], out int genOnOff)) return null;

                return new AntennaData
                {
                    Systick = systick,
                    ReceiverAngleDeg10 = receiverAngleDeg10,
                    TransmitterAngleDeg10 = transmitterAngleDeg10,
                    ReceiverAngleDeg = receiverAngleDeg10 / 10.0,
                    TransmitterAngleDeg = transmitterAngleDeg10 / 10.0,
                    RxAntennaCounter = rxAntennaCounter,
                    TxAntennaCounter = txAntennaCounter,
                    PowerDbm = powerDbm,
                    Voltage = voltage,
                    AntennaType = antennaType,
                    ModeAutoHand = modeAutoHand,
                    GenOnOff = genOnOff,
                };
            }
            else
            {
                if(input.StartsWith("#xx/AA", StringComparison.Ordinal) || !input.EndsWith("$"))
                {
                    var trimmed = input.Trim('#', '$');
                    CalibrationQueue.Enqueue(trimmed);
                }
                return null;
            }
        }

        private bool TryOpenPortWithTimeout(SerialPortStream port, int timeoutMs)
        {
            var task = Task.Run(() => {
                try
                {
                    port.Open();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
            return task.Wait(timeoutMs) && task.Result;
        }
    }
}
