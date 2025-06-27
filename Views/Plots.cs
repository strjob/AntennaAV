using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using System.Collections.Generic;

namespace AntennaAV.Views
{
    public static class Plots
    {
        public static ScottPlot.Plottables.PolarAxis Initialize(AvaPlot avaPlot)
        {
            var polarAxis = avaPlot.Plot.Add.PolarAxis(radius: 100);
            polarAxis.Rotation = Angle.FromDegrees(90);

            for (int i = 0; i < polarAxis.Spokes.Count; i++)
            {
                polarAxis.Spokes[i].LineWidth = 1;
                polarAxis.Spokes[i].LabelPaddingFraction = 0.07;
                polarAxis.Spokes[i].Length = 100;
                polarAxis.Spokes[i].LinePattern = LinePattern.Dotted;
            }

            for (int i = 0; i < polarAxis.Circles.Count; i++)
            {
                polarAxis.Circles[i].LabelText = $"{polarAxis.Circles[i].Radius}";
                polarAxis.Circles[i].LinePattern = LinePattern.Dotted;
            }

            polarAxis.Circles[^1].LabelText = "";
            polarAxis.Circles[^1].LineWidth = 3;
            polarAxis.Circles[^1].LinePattern = LinePattern.Solid;


            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
            avaPlot.Plot.Axes.Margins(0.05, 0.05);

            double[] radii = { 50, 90, 80, 60, 40 };
            double[] angles = { 0, 10, 20, 30, 40 };

            var points = new Coordinates[radii.Length];
            for (int i = 0; i < radii.Length; i++)
                points[i] = polarAxis.GetCoordinates(radii[i], angles[i]);

            var line = avaPlot.Plot.Add.Scatter(points);
            line.LineWidth = 2;
            line.MarkerSize = 0;

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
    }
}
