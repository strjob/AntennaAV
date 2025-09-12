using AntennaAV.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static AntennaAV.Services.ComPortManager;

namespace AntennaAV.Services;

// Интерфейс для сервиса работы с COM портом антенной установки
// Определяет методы для подключения, управления антеннами, калибровки и получения данных
public interface IComPortService
{
    // Статус подключения к COM порту
    bool IsOpen { get; }
    
    // Имя подключенного COM порта
    string? ConnectedPortName { get; }

    // Подключается к указанному COM порту
    ConnectResult ConnectToPort(string portName);
    
    // Автоматически обнаруживает и подключается к доступному порту
    ConnectResult AutoDetectAndConnect();

    // Запускает чтение данных с устройства
    void StartReading();
    
    // Останавливает чтение данных
    void StopReading();

    // Останавливает движение указанной антенны (T - передающая, R - приемная)
    bool StopAntenna(string ant);
    
    // Запускает обмен сообщениями с устройством
    bool StartMessaging();
    
    // Останавливает обмен сообщениями
    bool StopMessaging();

    // Устанавливает угол антенны с указанием направления движения
    bool SetAntennaAngle(double angle, string antenna, string direction);
    
    // Устанавливает калибровочную точку
    bool SetCalibrationPoint(double value);
    
    // Сохраняет калибровку в устройство
    bool SaveCalibration();
    
    // Очищает калибровку
    bool ClearCalibration();
    
    // Читает калибровку из устройства
    bool ReadCalibration();
    
    // Выполняет калибровку нулевой точки СВЧ генератора
    bool CalibrateZeroSVCH();
    
    // Управляет состоянием генератора (включен/выключен)
    bool SetGenState(bool isOff);

    // Устанавливает коэффициент усиления АЦП
    bool SetAdcGain(int gain);
    
    // Устанавливает стандартное усиление RF тракта
    bool SetDefaultRFGain(int gain);
    
    // Возвращает все записанные данные измерений
    IEnumerable<AntennaData> GetAllRecordedData();
    
    // Получает список доступных COM портов
    IEnumerable<string> GetAvailablePortNames();
    
    // Извлекает данные из очереди (неблокирующий метод)
    bool TryDequeue(out AntennaData? data);
}


