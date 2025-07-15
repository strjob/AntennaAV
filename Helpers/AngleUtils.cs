using Microsoft.VisualBasic;
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
            angle = NormalizeAngle(angle);
            from = NormalizeAngle(from);
            to = NormalizeAngle(to);
            if (AngleDiff(from, to) < 0.1) return true;
            double diffFromTo = NormalizeAngle(from - to);
            double diffFromAngle = NormalizeAngle(from - angle);
            return diffFromAngle <= diffFromTo;
        }

        public static (double startAngleOvershoot, double stopAngleOvershoot, string direction, bool isFullCircle) DetermineStartEndDir(double currentAngle, double from, double to, int currentCounter)
        {
            double startAngle, stopAngle, startAngleOvershoot, stopAngleOvershoot;
            string direction;
            bool isFullCircle = false;
            //Определение стартового угла
            //Особый случай, если полный круг
            if (AngleUtils.AngleDiff(from, to) < 0.1)
            {
                startAngleOvershoot = currentAngle;
                isFullCircle = true;
                int fullCircleMovement = 3600; // 360° в единицах 0.1°
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
                //Определяем какая точка ближе к текущему положению
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
                //Определяем направление и углы
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


        public static double NormalizeAngle(double angle)
        {
            return (angle + 360) % 360;
        }

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

