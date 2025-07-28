using AntennaAV.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static AntennaAV.Services.ComPortManager;

namespace AntennaAV.Services;

public interface IComPortService
{
    bool IsOpen { get; }
    string? ConnectedPortName { get; }

    ConnectResult ConnectToPort(string portName);
    ConnectResult AutoDetectAndConnect();


    void StartReading();
    void StopReading();

    bool StopAntenna(string ant);
    bool StartMessaging();
    bool StopMessaging();

    bool SetAntennaAngle(double angle, string antenna, string direction);
    bool SetCalibrationPoint(double value);
    bool SaveCalibration();
    bool ClearCalibration();
    bool ReadCalibration();
    bool CalibrateZeroSVCH();

    bool SetAdcGain(int gain);
    bool SetDefaultRFGain(int gain);
    IEnumerable<AntennaData> GetAllRecordedData();
    IEnumerable<string> GetAvailablePortNames();
    bool TryDequeue(out AntennaData? data);
}


