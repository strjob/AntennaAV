using AntennaAV.Models;
using System.Collections.Generic;
using static AntennaAV.Services.ComPortManager;

namespace AntennaAV.Services;

public class MockComPortService : IComPortService
{
    public bool IsOpen => true;
    public string? ConnectedPortName => "COM-MOCK";

    public ConnectResult ConnectToPort(string portName) => ConnectResult.Success;

    public ConnectResult AutoDetectAndConnect() => ConnectResult.Success;

    public void StartReading() { /* ничего не делаем */ }
    public void StopReading() { }

    public bool StopAntenna(string ant) => true;
    public bool StartMessaging() => true;
    public bool StopMessaging() => true;

    public bool SetAntennaAngle(double angle, string antenna, string direction) => true;
    public bool SetCalibrationPoint(double value) => true;
    public bool SaveCalibration() => true;
    public bool ClearCalibration() => true;
    public bool ReadCalibration() => true;

    public IEnumerable<AntennaData> GetAllRecordedData() =>
        new List<AntennaData>
        {
            new()
            {
                ReceiverAngleDeg10 = 1234,
                TransmitterAngleDeg10 = 4321,
                PowerDbm = -28.5,
                AntennaType = 1,
                RxAntennaCounter = 7,
                ReceiverAngleDeg = 123.4,
                TransmitterAngleDeg = 321.0,
                Systick = 10
            }
        };

    public bool TryDequeue(out AntennaData data)
    {
        data = new AntennaData
        {
            ReceiverAngleDeg = 90.0,
            TransmitterAngleDeg = 270.0,
            PowerDbm = -30.1,
            AntennaType = 2,
            RxAntennaCounter = 3,
            Systick = 10
        };
        return true;
    }

    public IEnumerable<string> GetAvailablePortNames()
    {
        return new List<string> { "COM1", "COM2", "COM3" };
    }
}