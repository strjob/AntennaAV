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

        public static ScottPlot.Plottables.PolarAxis InitializeSmall(AvaPlot avaPlot, bool isDark)
        {
            var polarAxis = avaPlot.Plot.Add.PolarAxis(radius: 100);
            polarAxis.Rotation = Angle.FromDegrees(270);
            var labels = CreateListofAngles(90);// { "0", "30", "60", "90", "120", "150", "180", "210", "240", "270", "300", "330" };
            polarAxis.SetSpokes(labels, 100, true);

            // Цвета для темной и светлой темы
            var lineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");
            var circleColor = isDark ? ScottPlot.Color.FromHex("#bbbbbb") : ScottPlot.Color.FromHex("#666666");
            var labelColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");


            for (int i = 0; i < polarAxis.Spokes.Count; i++)
            {
                polarAxis.Spokes[i].LineWidth = 0;
                //polarAxis.Spokes[i].LabelPaddingFraction = 0.05;
                polarAxis.Spokes[i].Length = 100;
                polarAxis.Spokes[i].LinePattern = LinePattern.Dotted;
                polarAxis.Spokes[i].LineColor = lineColor;
                polarAxis.Spokes[i].LabelStyle.ForeColor = labelColor;
            }

            polarAxis.SetCircles(100, 1);

            // Не создаём круги вручную — они будут обновляться в AutoUpdatePolarAxisCircles

            avaPlot.Plot.Add.Palette = isDark
                ? new ScottPlot.Palettes.Penumbra()
                : new ScottPlot.Palettes.Category10();
            avaPlot.Plot.Axes.Margins(0.05, 0.05);

            avaPlot.UserInputProcessor.IsEnabled = false;
            avaPlot.Refresh();
            AddCustomSpokeLinesSmall(avaPlot, polarAxis, isDark);
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

        public static void AddCustomSpokeLinesSmall(AvaPlot avaPlot, ScottPlot.Plottables.PolarAxis polarAxis, bool isDark)
        {

            double rStart = 95;
            double rEnd = 105;
            var lineColor = isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");

            for (double angle = 0; angle < 360; angle += 30)
            {
                double angleRad = Math.PI * angle / 180.0;
                double x1 = rStart * Math.Cos(angleRad);
                double y1 = rStart * Math.Sin(angleRad);
                double x2 = rEnd * Math.Cos(angleRad);
                double y2 = rEnd * Math.Sin(angleRad);
                var line = avaPlot.Plot.Add.Line(x1, y1, x2, y2);
                line.Color = lineColor;
                line.LineWidth = 1;
                line.LinePattern = ScottPlot.LinePattern.Solid;
                _customSpokeLines.Add(line);
            }
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
            int minCircles = 3,
            int maxCircles = 8)
        {
            // Оставить только внешний круг (последний)
            while (polarAxis.Circles.Count > 1)
                polarAxis.Circles.RemoveAt(polarAxis.Circles.Count - 2); // удаляем все, кроме последнего
            // Проверка относительной разницы между minValue и maxValue
            double relDiff = Math.Abs(maxValue - minValue) / Math.Max(Math.Max(Math.Abs(maxValue), Math.Abs(minValue)), 1);
            double[] positions = Array.Empty<double>();
            string[] labels = Array.Empty<string>();

            // 1. Пытаемся построить красивые круги
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
            double NiceFloor(double value, double s)
            {
                if (value >= 0)
                    return Math.Floor(value / s) * s;
                else
                    return -Math.Ceiling(Math.Abs(value) / s) * s;
            }
            double NiceCeil(double value, double s)
            {
                if (value >= 0)
                    return Math.Ceiling(value / s) * s;
                else
                    return -Math.Floor(Math.Abs(value) / s) * s;
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
            // 7. Если получилось >=2 кругов — используем их, иначе строим стандартные 3 круга
            if (circleValues.Count >= 2)
            {
                int n = circleValues.Count;
                double r0 = ((niceMax - niceMin) > 0) ? 100 * (circleValues[0] - niceMin) / (niceMax - niceMin) : 100;
                double rN = 100;
                positions = new double[n];
                labels = new string[n];
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
                    positions[i] = r;
                    labels[i] = (i == n - 1) ? "" : (isLogScale ? $"{Math.Round(value, 1)} дБ" : $"{Math.Round(value, 1)}");
                }
            }
            else
            {
                double value50 = (minValue + maxValue) / 2;
                double value10 = minValue + (maxValue - minValue) * 0.1;
                positions = new double[] { 10, 50, 100 };
                labels = new string[] {
                    isLogScale ? $"{Math.Round(value10, 1)} дБ" : $"{Math.Round(value10, 1)}",
                    isLogScale ? $"{Math.Round(value50, 1)} дБ" : $"{Math.Round(value50, 1)}",
                    "" // внешний круг без подписи
                };
            }
            if (positions.Length == 0 || labels.Length == 0)
                return;
            polarAxis.SetCircles(positions, labels);
            // Стилизация кругов
            for (int i = 0; i < polarAxis.Circles.Count; i++)
            {
                var labelColor = isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");
                polarAxis.Circles[i].LabelStyle.ForeColor = labelColor;
                if (i == polarAxis.Circles.Count - 1)
                {
                    polarAxis.Circles[i].LineWidth = 2;
                    polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Solid;
                    polarAxis.Circles[i].LineColor = isDark ? ScottPlot.Color.FromHex("#bbbbbb") : ScottPlot.Color.FromHex("#666666");
                }
                else
                {
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
