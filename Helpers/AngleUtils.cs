using Microsoft.VisualBasic;
using System;

namespace AntennaAV.Helpers
{
    // Утилиты для работы с углами в системе антенного позиционирования
    // Обрабатывает нормализацию углов, вычисление разностей и проверку диапазонов
    public static class AngleUtils
    {
        // Вычисляет минимальную угловую разность между двумя углами с учетом цикличности
        public static double AngleDiff(double a, double b)
        {
            double diff = Math.Abs(a - b) % 360.0;
            return diff > 180.0 ? 360.0 - diff : diff;
        }

        // Проверяет, находится ли угол в заданном диапазоне с учетом перехода через 0°
        public static bool IsAngleInRange(double angle, double from, double to)
        {
            angle = NormalizeAngle(angle);
            from = NormalizeAngle(from);
            to = NormalizeAngle(to);
            if (AngleDiff(from, to) < 0.1) return true;
            double diffFromTo = NormalizeAngle(from - to);
            double diffFromAngle = NormalizeAngle(from - angle);
            return diffFromAngle <= diffFromTo;
        }

        // Определяет параметры движения антенны для сбора диаграммы
        // Возвращает стартовый угол, конечный угол, направление и признак полного круга
        public static (double startAngleOvershoot, double stopAngleOvershoot, string direction, bool isFullCircle) DetermineStartEndDir(double currentAngle, double from, double to, double currentCounter)
        {
            double startAngle, stopAngle, startAngleOvershoot, stopAngleOvershoot;
            string direction;
            bool isFullCircle = false;
            
            // Особый случай полного круга (360°)
            if (AngleUtils.AngleDiff(from, to) < 0.1)
            {
                startAngleOvershoot = currentAngle;
                isFullCircle = true;
                int fullCircleMovement = 360; 
                if (currentCounter + fullCircleMovement <= Constants.MaxAntennaCounter)
                {
                    direction = "+";
                    stopAngleOvershoot = AngleUtils.NormalizeAngle(currentAngle + Constants.Overshoot);
                }
                else
                {
                    direction = "-";
                    stopAngleOvershoot = AngleUtils.NormalizeAngle(currentAngle - Constants.Overshoot);
                }
            }
            else
            {
                // Определяем ближайшую стартовую точку
                if (AngleUtils.AngleDiff(currentAngle, from) <= AngleUtils.AngleDiff(currentAngle, to))
                {
                    startAngle = from;
                    stopAngle = to;
                }
                else
                {
                    startAngle = to;
                    stopAngle = from;
                }
                
                // Определяем направление движения и углы с перебегом
                if (AngleUtils.IsAngleInRange(startAngle + 1, from, to))
                {
                    startAngleOvershoot = AngleUtils.NormalizeAngle(startAngle - Constants.Overshoot);
                    stopAngleOvershoot = AngleUtils.NormalizeAngle(stopAngle + Constants.Overshoot);
                    direction = "+";
                }
                else
                {
                    startAngleOvershoot = AngleUtils.NormalizeAngle(startAngle + Constants.Overshoot);
                    stopAngleOvershoot = AngleUtils.NormalizeAngle(stopAngle - Constants.Overshoot);
                    direction = "-";
                }
            }
            return (startAngleOvershoot, stopAngleOvershoot, direction, isFullCircle);
        }

        // Нормализует угол к диапазону [0, 360) с округлением до 0.1°
        public static double NormalizeAngle(double angle)
        {
            return Math.Round((angle + 360) % 360, 1);
        }

        // Сдвигает угол на указанное значение (в градусах) с учетом перехода через 0 и округлением до 0.1°

        public static double ShiftAngle(double angle, double shift)
        {
            return NormalizeAngle(angle + shift);
        }

        // Сдвигает угол, представленный в десятых градуса (deg10), возвращает deg10 в диапазоне [0, 3599]

        public static int ShiftAngleDeg10(int deg10, double shift)
        {
            // deg10 -> deg
            double deg = deg10 / 10.0;
            double shifted = ShiftAngle(deg, shift);
            int shiftedDeg10 = (int)Math.Round(shifted * 10.0);
            // Normalize to [0,3599]
            shiftedDeg10 = ((shiftedDeg10 % 3600) + 3600) % 3600;
            return shiftedDeg10;
        }

        // Вычисляет диапазон углов сектора по его размеру и центру
        public static (double from, double to) CalculateSectorRange(double size, double center)
        {
            // Нормализуем центр к диапазону [0, 360)
            center = NormalizeAngle(center);
            // Вычисляем половину размера сектора
            double halfSize = size / 2.0;
            // Вычисляем начальный и конечный углы
            double from = NormalizeAngle(center + halfSize);
            double to = NormalizeAngle(center - halfSize);
            return (from, to);
        }

        // Проверяет валидность строкового представления угла
        public static string ValidateAngle(string value, out double parsedValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                parsedValue = 0;
                return "Поле не может быть пустым";
            }
            if (!double.TryParse(value, out parsedValue) || parsedValue < 0 || parsedValue > Constants.MaxAngleInput)
            {
                return Constants.AngleErrorStr;
            }
            return string.Empty;
        }
    }
}

