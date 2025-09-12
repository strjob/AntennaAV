using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntennaAV
{
    // Константы приложения для настройки интерфейса и алгоритмов работы с антенной установкой
    public static class Constants
    {
        // Минимальный размер сектора измерения в градусах
        public const int MinSectorSize = 10;
        
        // Максимальное количество вкладок с диаграммами
        public const int MaxTabCount = 10;
        
        // Максимально допустимый угол ввода
        public const double MaxAngleInput = 359.9;
        
        // Величина перебега для точной остановки антенны
        public const double Overshoot = 2.0;
        
        // Сообщение об ошибке ввода угла
        public const string AngleErrorStr = "Введите число от 0 до 359.9";
        
        // Максимальное значение счетчика антенны для защиты от перекручивания
        public const double MaxAntennaCounter = 540.0;

        // Параметры отображения полярного графика
        public const double DefaultPlotRadius = 100.0;
        public const double PlotRadiusFactor = 0.6;
        public const double PointerThreshold = 20.0;
        public const double PointerSnapStep = 10.0;
        public const double AngleGapThresholdEqual = 30.0;
        public const double AngleGapThresholdNotEqual = 1.0;
        public const int MarkerSize = 10;
        public const int HoverMarkerSize = 8;
        public const int ArrowWidth = 4;
        public const int ArrowheadWidth = 10;

        // Интервалы обновления таймеров в миллисекундах
        public const int UiTimerUpdateIntervalMs = 100;
        public const int TableTimerUpdateIntervalMs = 500;
        public const int PlotTimerUpdateIntervalMs = 100;
    }
}

