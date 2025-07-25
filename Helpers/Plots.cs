using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;

namespace AntennaAV.Helpers
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

        public static PolarAxis Initialize(AvaPlot avaPlot, bool isDark)
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
            public static Color GetLineColor(bool isDark) =>
                isDark ? Color.FromHex("#777777") : Color.FromHex("#BBBBBB");

            public static Color GetCircleColor(bool isDark) =>
                isDark ? Color.FromHex("#bbbbbb") : Color.FromHex("#666666");

            public static Color GetLabelColor(bool isDark) =>
                isDark ? Color.FromHex("#eeeeee") : Color.FromHex("#111111");
        }



        public static PolarAxis InitializeSmall(AvaPlot avaPlot, bool isDark, double rotation = 270)
        {
            var polarAxis = avaPlot.Plot.Add.PolarAxis(radius: 100);
            polarAxis.Rotation = Angle.FromDegrees(rotation);
            string[] labels = { "0", "90", "180", "270"};
            if (rotation == 90)
                labels[0] = "";
            else
                labels[2] = "";
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

        public static void AddCustomSpokeLinesSmall(AvaPlot avaPlot, PolarAxis polarAxis, bool isDark)
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
                line.LinePattern = LinePattern.Solid;
                _customSpokeLinesSmall.Add(line);
            }
        }


        public static void AddCustomSpokeLines(AvaPlot avaPlot, PolarAxis polarAxis, bool isDark)
        {
            // Защита от недостаточного количества кругов
            if (polarAxis.Circles.Count < 2)
                return;

            // Удаляем старые кастомные линии
            foreach (var line in _customSpokeLines)
                avaPlot.Plot.Remove(line);
            _customSpokeLines.Clear();

            double rEnd = 100;
            var lineColor = ThemeColors.GetLineColor(isDark);

            // Определяем пороговое значение для радиуса первого круга
            const double radiusThreshold = 20; // можно настроить по необходимости

            double firstCircleRadius = polarAxis.Circles[0].Radius;
            double secondCircleRadius = polarAxis.Circles[1].Radius;

            for (double angle = 0; angle < 360; angle += 10)
            {
                double rStart;

                if (angle % 30 == 0)
                {
                    // Если шаг кратный 30, то всегда рисуем от нулевого круга (первого)
                    rStart = firstCircleRadius;
                }
                else
                {
                    // Если шаг не кратный 30, то оцениваем радиус первого круга
                    if (firstCircleRadius > radiusThreshold)
                    {
                        // Если радиус первого круга больше порогового значения, рисуем от первого круга
                        rStart = firstCircleRadius;
                    }
                    else
                    {
                        // Иначе рисуем от второго круга
                        rStart = secondCircleRadius;
                    }
                }

                double angleRad = Math.PI * angle / 180.0;
                double x1 = rStart * Math.Cos(angleRad);
                double y1 = rStart * Math.Sin(angleRad);
                double x2 = rEnd * Math.Cos(angleRad);
                double y2 = rEnd * Math.Sin(angleRad);

                var line = avaPlot.Plot.Add.Line(x1, y1, x2, y2);
                line.Color = lineColor;
                line.LineWidth = 1;
                line.LinePattern = LinePattern.Dotted;
                _customSpokeLines.Add(line);
            }
        }

        public static void UpdatePolarAxisTheme(PolarAxis polarAxis, bool isDark)
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

        public static void UpdatePolarAxisThemeSmall(PolarAxis axis, AvaPlot avaPlot, bool isDark)
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
            PolarAxis polarAxis,
            bool isLogScale,
            double minValue,
            double maxValue,
            bool isDark,
            int minCircles = 3,
            int maxCircles = 8)
        {
            // Оставляем только внешний круг
            while (polarAxis.Circles.Count > 1)
                polarAxis.Circles.RemoveAt(polarAxis.Circles.Count - 2);

            double[] positions;
            string[] labels;

            var lineColor = ThemeColors.GetLineColor(isDark);
            var labelColor = ThemeColors.GetLabelColor(isDark);
            var circleColor = ThemeColors.GetCircleColor(isDark);
            const double minRadius = 7;

            if (isLogScale)

            {
                // Логика для логарифмической шкалы
                double range = maxValue - minValue;
                double[] possibleSteps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 30000, 50000, 100000 };

                double step = possibleSteps
                    .Select(s => new { s, count = Math.Ceiling(range / s) })
                    .Where(x => x.count >= minCircles && x.count <= maxCircles)
                    .OrderBy(x => x.s) // Приоритет меньшим шагам
                    .ThenBy(x => Math.Abs(x.count - (minCircles + maxCircles) / 2)) // Затем по близости к идеалу
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

                // Фильтруем значения, оставляя только те, что находятся в пределах реальных данных
                // и обеспечиваем минимальный радиус 10%
                circleValues = circleValues
                    .Where(v => v >= minValue - step * 0.1 && v <= maxValue + step * 0.1)
                    .Where(v => {
                        double dataRange = maxValue - minValue;
                        double r = dataRange > 0 ? 100 * (v - minValue) / dataRange : 100;
                        return v == maxValue || r >= minRadius;
                    })
                    .ToList();

                if (circleValues.Count > 2)
                {
                    int n = circleValues.Count;
                    positions = new double[n];
                    labels = new string[n];

                    // КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: вычисляем радиусы относительно реальных границ данных
                    double dataRange = maxValue - minValue;

                    for (int i = 0; i < n; i++)
                    {
                        double value = circleValues[i];

                        // Радиус вычисляется как процент от реального диапазона данных
                        double r = dataRange > 0 ? 100 * (value - minValue) / dataRange : 100;

                        r = Math.Max(minRadius, Math.Min(100, r));

                        positions[i] = r;

                        // Не показываем подпись для внешнего круга (r = 100) или если круг за пределами
                        labels[i] = (r >= 99.9 || value > maxValue) ? "" :
                                   (isLogScale ? $"{Math.Round(value, 1)} дБ" : $"{Math.Round(value, 1)}");
                    }
                }
                else
                {
                    // Если не удалось создать достаточно кругов, используем простое деление
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
                    polarAxis.Circles[i].LinePattern = LinePattern.Solid;
                    polarAxis.Circles[i].LineColor = circleColor;
                }
                else
                {
                    polarAxis.Circles[i].LineWidth = 1;
                    polarAxis.Circles[i].LinePattern = LinePattern.Dotted;
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
                            avaPlot.Plot.FigureBackground.Color = Color.FromHex("#181818");
                            avaPlot.Plot.DataBackground.Color = Color.FromHex("#1f1f1f");
                            avaPlot.Plot.Axes.Color(Color.FromHex("#d7d7d7"));
                            avaPlot.Plot.Grid.MajorLineColor = Color.FromHex("#404040");
                            avaPlot.Plot.Legend.BackgroundColor = Color.FromHex("#404040");
                            avaPlot.Plot.Legend.FontColor = Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Legend.OutlineColor = Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
                        }
                        else
                        {

                            avaPlot.Plot.FigureBackground.Color = Color.FromHex("#000000");
                            avaPlot.Plot.DataBackground.Color = Color.FromHex("#000000");
                            avaPlot.Plot.Axes.Color(Color.FromHex("#d7d7d7"));
                            avaPlot.Plot.Grid.MajorLineColor = Color.FromHex("#404040");
                            avaPlot.Plot.Legend.BackgroundColor = Color.FromHex("#404040");
                            avaPlot.Plot.Legend.FontColor = Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Legend.OutlineColor = Color.FromHex("#d7d7d7");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
                        }
                    }
                    else
                    {
                        if (main)
                        {

                            avaPlot.Plot.FigureBackground.Color = Color.FromHex("#ececec");
                            avaPlot.Plot.DataBackground.Color = Color.FromHex("#ececec");
                            avaPlot.Plot.Axes.Color(Color.FromHex("#222222"));
                            avaPlot.Plot.Grid.MajorLineColor = Color.FromHex("#e5e5e5");
                            avaPlot.Plot.Legend.BackgroundColor = Color.FromHex("#f0f0f0");
                            avaPlot.Plot.Legend.FontColor = Color.FromHex("#222222");
                            avaPlot.Plot.Legend.OutlineColor = Color.FromHex("#222222");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
                        }
                        else
                        {
                            avaPlot.Plot.FigureBackground.Color = Color.FromHex("#ffffff");
                            avaPlot.Plot.DataBackground.Color = Color.FromHex("#ffffff");
                            avaPlot.Plot.Axes.Color(Color.FromHex("#222222"));
                            avaPlot.Plot.Grid.MajorLineColor = Color.FromHex("#e5e5e5");
                            avaPlot.Plot.Legend.BackgroundColor = Color.FromHex("#f0f0f0");
                            avaPlot.Plot.Legend.FontColor = Color.FromHex("#222222");
                            avaPlot.Plot.Legend.OutlineColor = Color.FromHex("#222222");
                            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
                        }
                    }
                }
            }
        }


    }
}
