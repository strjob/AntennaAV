using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AntennaAV.Views
{
    public static class Plots
    {
        private static List<IPlottable> _customSpokeLines = new();

        public static ScottPlot.Plottables.PolarAxis Initialize(AvaPlot avaPlot, bool isDark)
        {
            var polarAxis = avaPlot.Plot.Add.PolarAxis(radius: 100);
            polarAxis.Rotation = Angle.FromDegrees(90);
            string[] labels = { "0", "30", "60", "90", "120", "150", "180", "210", "240", "270", "300", "330" };
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

            if (polarAxis.Circles.Count < 2)
                return;
            double rStart = polarAxis.Circles[0].Radius;
            double rEnd = polarAxis.Circles.Last().Radius;
            var lineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");

            for (int angle = 0; angle < 360; angle += 10)
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
            // 1. Выбираем красивый шаг
            double range = maxValue - minValue;
            double[] possibleSteps = {0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 30000, 50000, 100000 };
            double step = possibleSteps
                .Select(s => new { s, count = Math.Ceiling(range / s) })
                .Where(x => x.count >= minCircles && x.count <= maxCircles)
                .OrderBy(x => Math.Abs(x.count - (minCircles + maxCircles) / 2))
                .Select(x => x.s)
                .FirstOrDefault(10);

            // 2. Генерируем значения для подписей
            List<double> circleValues = new();
            double start = Math.Ceiling(minValue / step) * step;
            double end = Math.Floor(maxValue / step) * step;
            for (double v = start; v <= end; v += step)
            {
                double r = (maxValue - minValue) > 0 ? 100 * (v - minValue) / (maxValue - minValue) : 100;
                if (r >= 10)
                    circleValues.Add(v);
            }
            // Гарантируем, что внешний круг всегда есть и его радиус = 100
            if (circleValues.Count == 0 || Math.Abs(circleValues.Last() - maxValue) > 1e-6)
                circleValues.Add(maxValue);
            var labelColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");
            var lineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");
            // 3. Обновляем круги
            int n = Math.Min(polarAxis.Circles.Count, circleValues.Count);
            for (int i = 0; i < n; i++)
            {
                double value = circleValues[i];
                double r = (i == n - 1) ? 100 : (maxValue - minValue) > 0 ? 100 * (value - minValue) / (maxValue - minValue) : 100;
                polarAxis.Circles[i].Radius = r;
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
