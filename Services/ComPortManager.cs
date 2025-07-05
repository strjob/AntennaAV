﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RJCP.IO.Ports;
using AntennaAV.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AntennaAV.Services
{
    public class ComPortManager : IComPortService
    {
        private SerialPortStream? _port;
        private Thread? _readThread;
        private volatile bool _reading = false;
        public List<AntennaData> AllRecordedData { get; } = new();

        public readonly ConcurrentQueue<AntennaData> DataQueue = new();
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

        public bool SetAntennaAngle(double angle, string antenna, string direction)
        {
            if (angle < 0.0 || angle > 359.9)
                throw new ArgumentOutOfRangeException(nameof(angle), "Угол должен быть от 0.0 до 359.9");

            if (antenna != "R" && antenna != "T")
                throw new ArgumentOutOfRangeException(nameof(antenna), "Неверное имя антенны");

            if (direction != "+" && direction != "-" && direction != "S" && direction != "G")
                throw new ArgumentOutOfRangeException(nameof(direction), "Неверное направление");

            int value = (int)Math.Round(angle * 10);
            return WriteCommand($"{antenna}={direction}{value}", "ANT");
        }

        public bool ReadCalibration() => WriteCommand("R", "AA");
        public bool ClearCalibration() => WriteCommand("C", "AA");
        public bool SaveCalibration() => WriteCommand("S", "AA");

        public bool SetCalibrationPoint(double value)
        {
            if (_port != null && _port.IsOpen)
            { 
                WriteCommand($"W/{value.ToString("0.###", CultureInfo.InvariantCulture)}", "AA");
                return true;
            }
            return false;
        }

        public bool WriteCommand(string body, string prefix)
        {
            if (_port != null && _port.IsOpen)
            {
                string fullCommand = $"#{prefix}/xx/W/{body}$";
                _port.Write(fullCommand);
                return true;
            }

            return false;
        }


        private void ReadLoop()
        {
            var buffer = new StringBuilder();
            while (_reading && _port != null && _port.IsOpen)
            {
                try
                {
                    if (_port.BytesToRead > 0)
                    {
                        string data = _port.ReadExisting();
                        buffer.Append(data);

                        // Разбираем все сообщения из буфера
                        while (true)
                        {
                            int start = buffer.ToString().IndexOf('#');
                            int end = buffer.ToString().IndexOf('$', start + 1);
                            if (start >= 0 && end > start)
                            {
                                string message = buffer.ToString().Substring(start, end - start + 1);
                                buffer.Remove(0, end + 1);

                                var parsed = ParseDataString(message);
                                if (parsed != null)
                                    DataQueue.Enqueue(parsed);
                            }
                            else
                            {
                                // Нет полного сообщения
                                break;
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ReadLoop error: {ex}");
                }
            }
        }

        private static string? TryReadMessage(SerialPortStream port, int timeoutMs = 500)
        {
            var buffer = new StringBuilder();
            bool insideMessage = false;

            int originalTimeout = port.ReadTimeout;
            try
            {
                port.ReadTimeout = timeoutMs;

                while (true)
                {
                    int b;
                    try
                    {
                        b = port.ReadByte(); // Блокирующее чтение до таймаута
                    }
                    catch (TimeoutException)
                    {
                        break; // Время вышло, выходим из метода
                    }

                    char c = (char)b;

                    if (c == '#')
                    {
                        buffer.Clear();
                        buffer.Append(c);
                        insideMessage = true;
                    }
                    else if (insideMessage)
                    {
                        buffer.Append(c);
                        if (c == '$')
                            return buffer.ToString();
                    }
                }
            }
            finally
            {
                port.ReadTimeout = originalTimeout; // Восстанавливаем изначальное значение
            }

            return null;
        }

        private DateTime? deviceStartTime = null;
        private int? firstSystick = null;
        private int? prevSystick = null;
        private AntennaData? ParseDataString(string input)
        {
            if (!input.StartsWith("#xx/ANT") || !input.EndsWith("$"))
                return null;

            var trimmed = input.Trim('#', '$');
            var parts = trimmed.Split('/');

            if (parts.Length < 11) return null;

            try
            {
                int systick = int.Parse(parts[3]);
                if (deviceStartTime == null || (prevSystick != null && systick < prevSystick))
                {
                    deviceStartTime = DateTime.Now;
                    firstSystick = systick;
                }
                prevSystick = systick;
                if (firstSystick == null)
                    firstSystick = systick;
                int deltaMs = systick - firstSystick.Value;
                return new AntennaData
                {
                    ReceiverAngleDeg10 = int.Parse(parts[4]),
                    TransmitterAngleDeg10 = int.Parse(parts[5]),
                    ReceiverAngleDeg = int.Parse(parts[4]) / 10.0,
                    TransmitterAngleDeg = int.Parse(parts[5]) / 10.0,
                    RxAntennaCounter = int.Parse(parts[6]),
                    TxAntennaCounter = int.Parse(parts[7]),
                    PowerDbm = double.Parse(parts[8], CultureInfo.InvariantCulture),
                    AntennaType = int.Parse(parts[9]),
                    ModeAutoHand = int.Parse(parts[10]),
                    Timestamp = deviceStartTime.Value.AddMilliseconds(deltaMs)
                };
            }
            catch
            {
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
