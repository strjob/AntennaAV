using System;
namespace AntennaAV.Helpers
{
    public static class AngleUtils
    {
        public static double AngleDiff(double a, double b)
        {
            double diff = Math.Abs(a - b) % 360.0;
            return diff > 180.0 ? 360.0 - diff : diff;
        }

        public static bool IsAngleInRange(double angle, double from, double to)
        {
            angle = (angle + 360) % 360;
            from = (from + 360) % 360;
            to = (to + 360) % 360;
            if (AngleDiff(from, to) < 0.1) return true;
            double diffFromTo = (from - to + 360) % 360;
            double diffFromAngle = (from - angle + 360) % 360;
            return diffFromAngle <= diffFromTo;
        }

        public static (double from, double to) CalculateSectorRange(double size, double center)
        {
            // Нормализуем центр к диапазону [0, 360)
            center = center % 360.0;
            if (center < 0) center += 360.0;
            // Вычисляем половину размера сектора
            double halfSize = size / 2.0;
            // Вычисляем начальный и конечный углы
            double from = center + halfSize;
            double to = center - halfSize;
            // Нормализуем углы к диапазону [0, 360)
            from = from % 360.0;
            if (from < 0) from += 360.0;
            to = to % 360.0;
            if (to < 0) to += 360.0;
            return (from, to);
        }
    }
} 