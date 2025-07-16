using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;

namespace AntennaAV.Views
{
    public static class Plots
    {
        private static List<IPlottable> _customSpokeLines = new();

        private static List<IPlottable> _customSpokeLinesSmall = new();


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

            var labelColor = ThemeColors.GetLabelColor(isDark);

            
            for (int i = 0; i < polarAxis.Spokes.Count; i++)
            {
                polarAxis.Spokes[i].LineWidth = 0;
                polarAxis.Spokes[i].LabelPaddingFraction = 0.05;
                polarAxis.Spokes[i].Length = 100;
                polarAxis.Spokes[i].LabelStyle.ForeColor = labelColor;
            }

            avaPlot.Plot.Axes.Margins(0.05, 0.05);

            avaPlot.UserInputProcessor.IsEnabled = false;
            return polarAxis;
        }

        public static class ThemeColors
        {
            public static ScottPlot.Color GetLineColor(bool isDark) =>
                isDark ? ScottPlot.Color.FromHex("#777777") : ScottPlot.Color.FromHex("#BBBBBB");

            public static ScottPlot.Color GetCircleColor(bool isDark) =>
                isDark ? ScottPlot.Color.FromHex("#bbbbbb") : ScottPlot.Color.FromHex("#666666");

            public static ScottPlot.Color GetLabelColor(bool isDark) =>
                isDark ? ScottPlot.Color.FromHex("#eeeeee") : ScottPlot.Color.FromHex("#111111");
        }



        public static ScottPlot.Plottables.PolarAxis InitializeSmall(AvaPlot avaPlot, bool isDark)
        {
            var polarAxis = avaPlot.Plot.Add.PolarAxis(radius: 100);
            polarAxis.Rotation = Angle.FromDegrees(270);
            string[] labels = { "0", "90", "", "270"};
            polarAxis.SetSpokes(labels, 100, true);

            // Цвета для темной и светлой темы
            var labelColor = ThemeColors.GetLabelColor(isDark);


            for (int i = 0; i < polarAxis.Spokes.Count; i++)
            {
                polarAxis.Spokes[i].LineWidth = 0;
                polarAxis.Spokes[i].LabelPaddingFraction = 0.35;
                polarAxis.Spokes[i].Length = 100;
                polarAxis.Spokes[i].LabelStyle.ForeColor = labelColor;
            }

            polarAxis.SetCircles(100, 1);
            polarAxis.Circles[0].LineWidth = 0;


            // Не создаём круги вручную — они будут обновляться в AutoUpdatePolarAxisCircles

            avaPlot.Plot.Add.Palette = isDark
                ? new ScottPlot.Palettes.Penumbra()
                : new ScottPlot.Palettes.Category10();
            //avaPlot.Plot.Axes.Margins(0.05, 0.05);

            avaPlot.UserInputProcessor.IsEnabled = false;
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

            double rStart = 97;
            double rEnd = 103;
            var lineColor = ThemeColors.GetCircleColor(isDark);

            foreach (var line in _customSpokeLinesSmall)
                avaPlot.Plot.Remove(line);
            _customSpokeLinesSmall.Clear();

            for (double angle = 10; angle <= 360; angle += 10)
            {
                if (angle % 30 == 0)
                {
                    rStart = 94;
                    rEnd = 106;
                }
                else
                {
                    rStart = 97;
                    rEnd = 103;
                }
                double angleRad = Math.PI * angle / 180.0;
                double x1 = rStart * Math.Cos(angleRad);
                double y1 = rStart * Math.Sin(angleRad);
                double x2 = rEnd * Math.Cos(angleRad);
                double y2 = rEnd * Math.Sin(angleRad);
                var line = avaPlot.Plot.Add.Line(x1, y1, x2, y2);
                line.Color = lineColor;
                line.LineWidth = 1;
                line.LinePattern = ScottPlot.LinePattern.Solid;
                _customSpokeLinesSmall.Add(line);
            }
        }

        public static void AddCustomSpokeLines(AvaPlot avaPlot, ScottPlot.Plottables.PolarAxis polarAxis, bool isDark)
        {
            // Защита от недостаточного количества кругов
            if (polarAxis.Circles.Count < 2)
                return;

            // Удаляем старые кастомные линии
            foreach (var line in _customSpokeLines)
                avaPlot.Plot.Remove(line);
            _customSpokeLines.Clear();

            double rStart = 10;
            double rEnd = 100;
            var lineColor = ThemeColors.GetLineColor(isDark);

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

        public static void UpdatePolarAxisTheme(ScottPlot.Plottables.PolarAxis polarAxis, bool isDark)
        {
            if (polarAxis == null) return;

            var lineColor = ThemeColors.GetLineColor(isDark);
            var circleColor = ThemeColors.GetCircleColor(isDark);
            var labelColor = ThemeColors.GetLabelColor(isDark);


            foreach (var spoke in polarAxis.Spokes)
            {
                spoke.LineColor = lineColor;
                spoke.LabelStyle.ForeColor = labelColor;
            }

            foreach (var circle in polarAxis.Circles)
            {
                circle.LineColor = lineColor;
                circle.LabelStyle.ForeColor = labelColor;
            }

            polarAxis.Circles[^1].LineColor = circleColor;

        }

        public static void UpdatePolarAxisThemeSmall(ScottPlot.Plottables.PolarAxis axis, AvaPlot avaPlot, bool isDark)
        {
            var lineColor = ThemeColors.GetLineColor(isDark);
            var circleColor = ThemeColors.GetCircleColor(isDark);
            var labelColor = ThemeColors.GetLabelColor(isDark);

            // Обновить спицы
            foreach (var spoke in axis.Spokes)
            {
                spoke.LineColor = lineColor;
                spoke.LabelStyle.ForeColor = labelColor;
            }

            // Обновить круги
            foreach (var circle in axis.Circles)
            {
                circle.LineColor = lineColor;
                circle.LabelStyle.ForeColor = labelColor;
            }
            if (axis.Circles.Count > 0)
                axis.Circles[^1].LineColor = circleColor;

            // Обновить кастомные линии
            AddCustomSpokeLinesSmall(avaPlot, axis, isDark);
        }



        public static void AutoUpdatePolarAxisCircles(
            AvaPlot avaPlot,
            ScottPlot.Plottables.PolarAxis polarAxis,
            bool isLogScale,
            double minValue,
            double maxValue,
            bool isDark,
            int minCircles = 3,
            int maxCircles = 7)
        {
            // Оставляем только внешний круг
            while (polarAxis.Circles.Count > 1)
                polarAxis.Circles.RemoveAt(polarAxis.Circles.Count - 2);

            double[] positions;
            string[] labels;

            var lineColor = ThemeColors.GetLineColor(isDark);
            var labelColor = ThemeColors.GetLabelColor(isDark);
            var circleColor = ThemeColors.GetCircleColor(isDark);

            if (isLogScale)
            {
                // Логика для логарифмической шкалы
                double relDiff = Math.Abs(maxValue - minValue) / Math.Max(Math.Max(Math.Abs(maxValue), Math.Abs(minValue)), 1);
                double[] possibleSteps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 30000, 50000, 100000 };
                double range = maxValue - minValue;
                double step = possibleSteps
                    .Select(s => new { s, count = Math.Ceiling(range / s) })
                    .Where(x => x.count >= minCircles && x.count <= maxCircles)
                    .OrderBy(x => Math.Abs(x.count - (minCircles + maxCircles) / 2))
                    .Select(x => x.s)
                    .FirstOrDefault(10);

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
                List<double> circleValues = new();
                for (double v = niceMin; v <= niceMax + step * 0.1; v += step)
                    circleValues.Add(Math.Round(v, 6));
                circleValues = circleValues.OrderBy(v => v).ToList();
                circleValues = circleValues
                    .Where(v => v == niceMax || ((niceMax - niceMin) > 0 ? 100 * (v - niceMin) / (niceMax - niceMin) : 100) >= 10)
                    .ToList();

                if (circleValues.Count > 2)
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
                    double value70 = minValue + (maxValue - minValue) * 0.7;
                    double value40 = minValue + (maxValue - minValue) * 0.4;
                    double value10 = minValue + (maxValue - minValue) * 0.1;
                    positions = new double[] { 10, 40, 70, 100 };
                    labels = new string[] {
                isLogScale ? $"{Math.Round(value10, 1)} дБ" : $"{Math.Round(value10, 1)}",
                isLogScale ? $"{Math.Round(value40, 1)} дБ" : $"{Math.Round(value40, 1)}",
                isLogScale ? $"{Math.Round(value70, 1)} дБ" : $"{Math.Round(value70, 1)}",
                ""
            };
                }
            }
            else
            {
                // Логика для линейной шкалы
                if (maxValue <= 0)
                    return;

                double niceMin = 0; // Центр всегда 0
                double range = maxValue - niceMin;
                double[] possibleSteps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 30000, 50000, 100000 };
                double step = possibleSteps
                    .Select(s => new { s, count = Math.Ceiling(range / s) })
                    .Where(x => x.count >= minCircles && x.count <= maxCircles)
                    .OrderBy(x => Math.Abs(x.count - (minCircles + maxCircles) / 2))
                    .Select(x => x.s)
                    .FirstOrDefault(10);

                if (step == 0)
                    step = 10; // Значение по умолчанию

                // Генерируем значения кругов
                List<double> circleValues = new();
                for (double v = step; v <= maxValue; v += step)
                    circleValues.Add(v);

                // Убеждаемся, что внешний круг соответствует maxValue
                if (circleValues.Count == 0 || circleValues.Last() < maxValue)
                    circleValues.Add(maxValue);

                // Вычисляем радиусы пропорционально maxValue
                positions = circleValues.Select(v => 100 * v / maxValue).ToArray();
                labels = circleValues.Select(v => v == maxValue ? "" : Math.Round(v, 1).ToString()).ToArray();

                // Фильтруем круги с радиусом < 10, кроме внешнего
                var filtered = positions.Zip(labels, (p, l) => new { p, l })
                    .Where(x => x.p >= 10 || x.l == "")
                    .ToArray();
                positions = filtered.Select(x => x.p).ToArray();
                labels = filtered.Select(x => x.l).ToArray();

                // Если кругов меньше 2, используем стандартные позиции
                if (positions.Length < 2)
                {
                    double value10 = 0.1 * maxValue;
                    double value40 = 0.4 * maxValue;
                    double value70 = 0.7 * maxValue;
                    positions = new double[] { 10, 40, 70, 100 };
                    labels = new string[] {
                Math.Round(value10, 1).ToString(),
                Math.Round(value40, 1).ToString(),
                Math.Round(value70, 1).ToString(),
                ""
            };
                }
            }

            if (positions.Length == 0 || labels.Length == 0)
                return;

            polarAxis.SetCircles(positions, labels);

            // Стилизация кругов
            for (int i = 0; i < polarAxis.Circles.Count; i++)
            {
                polarAxis.Circles[i].LabelStyle.ForeColor = labelColor;
                if (i == polarAxis.Circles.Count - 1)
                {
                    polarAxis.Circles[i].Radius = 100;
                    polarAxis.Circles[i].LineWidth = 2;
                    polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Solid;
                    polarAxis.Circles[i].LineColor = circleColor;
                }
                else
                {
                    polarAxis.Circles[i].LineWidth = 1;
                    polarAxis.Circles[i].LinePattern = ScottPlot.LinePattern.Dotted;
                    polarAxis.Circles[i].LineColor = lineColor;
                }
            }

            AddCustomSpokeLines(avaPlot, polarAxis, isDark);
        }

        public static void SetScottPlotTheme(bool isDark, bool main, params AvaPlot[] plots)
        {

            foreach (var avaPlot in plots)
            {
                if (avaPlot?.Plot != null)
                {
                    if (isDark)
                    {
                        if(main)
                        {
                            avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
                            avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
                            avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
                            avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
                            avaPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
                            avaPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
                        }
                        else
                        {

                            avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#000000");
                            avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#000000");
                            avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
                            avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
                            avaPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
                            avaPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
                        }
                    }
                    else
                    {
                        if (main)
                        {

                            avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                            avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                            avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#222222"));
                            avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#e5e5e5");
                            avaPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#f0f0f0");
                            avaPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#222222");
                            avaPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#222222");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
                        }
                        else
                        {
                            avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                            avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                            avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#222222"));
                            avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#e5e5e5");
                            avaPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#f0f0f0");
                            avaPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#222222");
                            avaPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#222222");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
                        }
                    }
                }
            }
        }


    }
}
