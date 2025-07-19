using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntennaAV
{
    public static class Constants
    {
        public const int MinSectorSize = 10;
        public const int MaxTabCount = 10;
        public const double MaxAngleInput = 359.9;
        public const double Overshoot = 2.0;
        public const string AngleErrorStr = "Введите число от 0 до 359.9";
        public const int MaxAntennaCounter = 5400;

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

        public const int UiTimerUpdateIntervalMs = 100;
        public const int TableTimerUpdateIntervalMs = 200;
        public const int PlotTimerUpdateIntervalMs = 100;

    }
}

