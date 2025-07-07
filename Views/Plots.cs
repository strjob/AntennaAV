using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;

namespace AntennaAV.Views
{
    public static class Plots
    {
        private static List<IPlottable> _customSpokeLines = new();

        private static string[] CreateListofAngles(int step)
        {
            string[] ret = new string[360 / step];

            for (int i = 0; i < ret.Length; i++)
            {
                ret[i] = (i * step).ToString();
            }
            return ret;
        }

        public static ScottPlot.Plottables.PolarAxis Initialize(AvaPlot avaPlot, bool isDark)
        {
            var polarAxis = avaPlot.Plot.Add.PolarAxis(radius: 100);
            polarAxis.Rotation = Angle.FromDegrees(90);
            var labels = CreateListofAngles(30);// { "0", "30", "60", "90", "120", "150", "180", "210", "240", "270", "300", "330" };
            polarAxis.SetSpokes(labels, 95, true);

            // Цвета для темной и светлой темы
            var lineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");
            var circleColor = isDark ? ScottPlot.Color.FromHex("#bbbbbb") : ScottPlot.Color.FromHex("#666666");
            var labelColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");

            
            for (int i = 0; i < polarAxis.Spokes.Count; i++)
            {
                polarAxis.Spokes[i].LineWidth = 0;
                polarAxis.Spokes[i].LabelPaddingFraction = 0.05;
                polarAxis.Spokes[i].Length = 100;
                polarAxis.Spokes[i].LinePattern = LinePattern.Dotted;
                polarAxis.Spokes[i].LineColor = lineColor;
                polarAxis.Spokes[i].LabelStyle.ForeColor = labelColor;
            }
            
            // Не создаём круги вручную — они будут обновляться в AutoUpdatePolarAxisCircles

            avaPlot.Plot.Add.Palette = isDark
                ? new ScottPlot.Palettes.Penumbra()
                : new ScottPlot.Palettes.Category10();
            avaPlot.Plot.Axes.Margins(0.05, 0.05);

            avaPlot.UserInputProcessor.IsEnabled = false;
            avaPlot.Refresh();
            return polarAxis;
        }

        public static double[] GetCircularRange(double from, double to)
        {
            int start = ((int)from % 360 + 360) % 360;
            int end = ((int)to % 360 + 360) % 360;

            var result = new List<double>();


            
            int current = end;
            result.Add(current);

            while (current != start)
            {
                current = (current + 1) % 361; // 0..360 включительно
                if (current > 360) current = 0;
                result.Add(current);
            }
            

            return result.ToArray();
        }

        public static void AddCustomSpokeLines(AvaPlot avaPlot, ScottPlot.Plottables.PolarAxis polarAxis, bool isDark)
        {
            // Удаляем старые кастомные линии
            foreach (var line in _customSpokeLines)
                avaPlot.Plot.Remove(line);
            _customSpokeLines.Clear();

            double rStart = 10;
            double rEnd = 100;
            var lineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");

            rStart = polarAxis.Circles[0].Radius;
            //rEnd = polarAxis.Circles.Last().Radius;

            for (double angle = 0; angle < 360; angle += 10)
            {
                if (angle % 30 == 0) rStart = polarAxis.Circles[0].Radius;
                else rStart = polarAxis.Circles[1].Radius;
                double angleRad = Math.PI * angle / 180.0;
                double x1 = rStart * Math.Cos(angleRad);
                double y1 = rStart * Math.Sin(angleRad);
                double x2 = rEnd * Math.Cos(angleRad);
                double y2 = rEnd * Math.Sin(angleRad);
                var line = avaPlot.Plot.Add.Line(x1, y1, x2, y2);
                line.Color = lineColor;
                line.LineWidth = 1;
                line.LinePattern = ScottPlot.LinePattern.Dotted;
                _customSpokeLines.Add(line);
            }
          
               
        }

        public static void AutoUpdatePolarAxisCircles(
            AvaPlot avaPlot,
            ScottPlot.Plottables.PolarAxis polarAxis,
            bool isLogScale,
            double minValue,
            double maxValue,
            bool isDark,
            int minCircles = 2,
            int maxCircles = 7)
        {
            // Проверка относительной разницы между minValue и maxValue
            double relDiff = Math.Abs(maxValue - minValue) / Math.Max(Math.Max(Math.Abs(maxValue), Math.Abs(minValue)), 1);
            if (relDiff < 0.02)
            {
                double value50 = (minValue + maxValue) / 2;
                double value10 = minValue + (maxValue - minValue) * 0.1;
                for (int i = 0; i < polarAxis.Circles.Count; i++)
                {
                    if (i == 0)
                    {
                        polarAxis.Circles[i].Radius = 10;
                        polarAxis.Circles[i].LabelText = isLogScale
                            ? $"{Math.Round(value10, 1)} дБ"
                            : $"{Math.Round(value10, 1)}";
                        polarAxis.Circles[i].LineWidth = 1;
                        polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Dotted;
                        polarAxis.Circles[i].LineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");
                        polarAxis.Circles[i].LabelStyle.ForeColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");
                    }
                    else if (i == 1)
                    {
                        polarAxis.Circles[i].Radius = 50;
                        polarAxis.Circles[i].LabelText = isLogScale
                            ? $"{Math.Round(value50, 1)} дБ"
                            : $"{Math.Round(value50, 1)}";
                        polarAxis.Circles[i].LineWidth = 1;
                        polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Dotted;
                        polarAxis.Circles[i].LineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");
                        polarAxis.Circles[i].LabelStyle.ForeColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");
                    }
                    else if (i == 2)
                    {
                        polarAxis.Circles[i].Radius = 100;
                        polarAxis.Circles[i].LabelText = "";
                        polarAxis.Circles[i].LineWidth = 2;
                        polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Solid;
                        polarAxis.Circles[i].LineColor = isDark ? ScottPlot.Color.FromHex("#bbbbbb") : ScottPlot.Color.FromHex("#666666");
                        polarAxis.Circles[i].LabelStyle.ForeColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");
                    }
                    else
                    {
                        polarAxis.Circles[i].Radius = 0;
                        polarAxis.Circles[i].LabelText = "";
                    }
                }
                AddCustomSpokeLines(avaPlot, polarAxis, isDark);
                return;
            }
            // 1. Красивые шаги (только стандартные, без дробных)
            double[] possibleSteps = {0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 30000, 50000, 100000 };
            double range = maxValue - minValue;
            // 2. Находим красивый шаг (только целые или стандартные значения)
            double step = possibleSteps
                .Select(s => new { s, count = Math.Ceiling(range / s) })
                .Where(x => x.count >= minCircles && x.count <= maxCircles)
                .OrderBy(x => Math.Abs(x.count - (minCircles + maxCircles) / 2))
                .Select(x => x.s)
                .FirstOrDefault(10);
            // 3. Округляем minValue вниз, maxValue вверх к красивым значениям по модулю
            double NiceFloor(double value, double step)
            {
                if (value >= 0)
                    return Math.Floor(value / step) * step;
                else
                    return -Math.Ceiling(Math.Abs(value) / step) * step;
            }
            double NiceCeil(double value, double step)
            {
                if (value >= 0)
                    return Math.Ceiling(value / step) * step;
                else
                    return -Math.Floor(Math.Abs(value) / step) * step;
            }
            double niceMin = NiceFloor(minValue, step);
            double niceMax = NiceCeil(maxValue, step);
            // 4. Генерируем красивые значения строго по шагу, с защитой от накопления ошибок
            List<double> circleValues = new();
            for (double v = niceMin; v <= niceMax + step * 0.1; v += step)
                circleValues.Add(Math.Round(v, 6));
            // 5. Сортируем значения
            circleValues = circleValues.OrderBy(v => v).ToList();
            // 6. Фильтруем по радиусу >= 10% (кроме niceMax)
            circleValues = circleValues
                .Where(v => v == niceMax || ((niceMax - niceMin) > 0 ? 100 * (v - niceMin) / (niceMax - niceMin) : 100) >= 10)
                .ToList();
            // 7. Радиусы: первый и последний — по формуле, промежуточные — равномерно между ними
            int n = Math.Min(polarAxis.Circles.Count, circleValues.Count);
            if (n == 0) return;
            double r0 = ((niceMax - niceMin) > 0) ? 100 * (circleValues[0] - niceMin) / (niceMax - niceMin) : 100;
            double rN = 100;
            for (int i = 0; i < n; i++)
            {
                double value = circleValues[i];
                double r;
                if (i == 0)
                    r = r0;
                else if (i == n - 1)
                    r = rN;
                else
                    r = r0 + (rN - r0) * i / (n - 1);
                polarAxis.Circles[i].Radius = r;
                var labelColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");
                polarAxis.Circles[i].LabelStyle.ForeColor = labelColor;
                if (i == n - 1)
                {
                    // Самый внешний круг — всегда сплошной и толстый
                    polarAxis.Circles[i].LabelText = "";
                    polarAxis.Circles[i].LineWidth = 2;
                    polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Solid;
                    polarAxis.Circles[i].LineColor = isDark ? ScottPlot.Color.FromHex("#bbbbbb") : ScottPlot.Color.FromHex("#666666");
                }
                else
                {
                    polarAxis.Circles[i].LabelText = isLogScale
                        ? $"{Math.Round(value, 1)} дБ"
                        : $"{Math.Round(value, 1)}";
                    polarAxis.Circles[i].LineWidth = 1;
                    polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Dotted;
                    polarAxis.Circles[i].LineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");
                }
            }
            // Добавляем кастомные спицы-отрезки
            AddCustomSpokeLines(avaPlot, polarAxis, isDark);
        }
    }
}
