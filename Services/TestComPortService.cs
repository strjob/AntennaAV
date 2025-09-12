using AntennaAV.Helpers;
using AntennaAV.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AntennaAV.Services.ComPortManager;

namespace AntennaAV.Services;

public class TestComPortService : IComPortService, IDisposable
{
    public bool IsOpen => true;
    public string? ConnectedPortName => "COM-TEST";

    private readonly ConcurrentQueue<AntennaData> _queue = new();
    private CancellationTokenSource? _cts;
    private Task? _generatorTask;
    private int _counter = 0;
    private int _receiverAngle = 3000;
    private int _antennaType = 0;
    private int _modeAutoHand = 0;
    private int _rareChangeCounter = 0;
    private double _powerPhase = 0.0;

    public ConnectResult ConnectToPort(string portName) => ConnectResult.Success;
    public ConnectResult AutoDetectAndConnect() => ConnectResult.Success;

    public void StartReading()
    {
        if (_generatorTask != null && !_generatorTask.IsCompleted)
            return;
        _cts = new CancellationTokenSource();
        _generatorTask = Task.Run(() => GenerateData(_cts.Token));
    }

    public void StopReading()
    {
        _cts?.Cancel();
        _generatorTask?.Wait();
    }

    private void GenerateData(CancellationToken token)
    {
        var rand = new Random();
        while (!token.IsCancellationRequested)
        {
            // Циклическое изменение ReceiverAngleDeg
            double angle = Math.Round(_receiverAngle/10.0, 1);


            // ReceiverAngleDeg10 всегда = ReceiverAngleDeg*10
            int angle10 = (int)Math.Round(angle * 10);

            // AntennaType и ModeAutoHand меняются редко
            if (_rareChangeCounter++ % 100 == 0)
            {
                _antennaType = rand.Next(0, 4);
            }
            if (_rareChangeCounter % 150 == 0)
            {
                _modeAutoHand = rand.Next(0, 2);
            }

            // TransmitterAngleDeg10 и TransmitterAngleDeg
            int transmitterAngle10 = 2700 + _counter % 100;
            double transmitterAngle = transmitterAngle10 / 10.0;

            // PowerDbm: плавное изменение с двумя знаками после запятой
            _powerPhase = angle * 2 * Math.PI / 360; // скорость изменения
            if (_powerPhase > Math.PI * 2) _powerPhase -= Math.PI * 2;
            double basePower = -65 + 20 * Math.Sin(_powerPhase*5); // от -55 до -15
            double noise = (rand.NextDouble() - 0.5) * 2.0; // небольшой шум
            double powerDbm = Math.Round(basePower, 2);
            //double powerDbm = -89.8;
            //double powerDbm = -angle/10;

            var data = new AntennaData
            {
                ReceiverAngleDeg = angle,
                TransmitterAngleDeg = AngleUtils.NormalizeAngle(angle * 2),
                ReceiverAngleDeg10 = angle10,
                TransmitterAngleDeg10 = angle10,
                PowerDbm = powerDbm,
                AntennaType = _antennaType,
                RxAntennaCounter = _counter,
                TxAntennaCounter = _counter * 2,
                ModeAutoHand = _modeAutoHand,
                Systick = _counter*20 + 4534543,
                GenOnOff = 0
            };
            _queue.Enqueue(data);
            _counter++;

            // Увеличиваем угол с шагом 0.1, циклически

            if (rand.NextDouble() > 0.08)
                _receiverAngle++;
                
            if (_receiverAngle >= 3600)
                _receiverAngle = 0;

            Thread.Sleep(20);
        }
    }

    public bool TryDequeue(out AntennaData? data)
    {
        return _queue.TryDequeue(out data);
    }

    public bool StopAntenna(string ant) => true;
    public bool StartMessaging() => true;
    public bool StopMessaging() => true;
    public bool SetAntennaAngle(double angle, string antenna, string direction) => true;
    public bool SetCalibrationPoint(double value) => true;
    public bool SaveCalibration() => true;
    public bool ClearCalibration() => true;
    public bool ReadCalibration() => true;
    public bool SetAdcGain(int gain) => true;
    public bool SetDefaultRFGain(int gain) => true;
    public bool SetGenState(bool isOff) => true;
    public bool CalibrateZeroSVCH() => true;

    
    public IEnumerable<AntennaData> GetAllRecordedData() => _queue.ToArray();

    public IEnumerable<string> GetAvailablePortNames()
    {
        return new List<string> { "COM4", "COM5" };
    }

    public void Dispose()
    {
        StopReading();
        _cts?.Dispose();
    }
} 