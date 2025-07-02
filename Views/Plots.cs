using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;

namespace AntennaAV.Views
{
    public static class Plots
    {
        public static ScottPlot.Plottables.PolarAxis Initialize(AvaPlot avaPlot)
        {
            var polarAxis = avaPlot.Plot.Add.PolarAxis(radius: 100);
            polarAxis.Rotation = Angle.FromDegrees(90);
            string[] labels = { "0", "30", "60", "90", "120", "150", "180", "210", "240", "270", "300", "330" };
            polarAxis.SetSpokes(labels, 95, true);

            for (int i = 0; i < polarAxis.Spokes.Count; i++)
            {
                polarAxis.Spokes[i].LineWidth = 1;
                polarAxis.Spokes[i].LabelPaddingFraction = 0.05;
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

           /* // Пример для ScottPlot 5.x
            double thetaDeg = 45;
            double r1 = 50;
            double r2 = 100.0;
            double thetaRad = Math.PI * thetaDeg / 180.0;

            double x1 = r1 * Math.Cos(thetaRad);
            double y1 = r1 * Math.Sin(thetaRad);
            double x2 = r2 * Math.Cos(thetaRad);
            double y2 = r2 * Math.Sin(thetaRad);

            // Для отрезка используем AddScatter с двумя точками:
            double[] xs = { x1, x2 };
            double[] ys = { y1, y2 };
            avaPlot.Plot.Add.ScatterLine(xs, ys, color: Colors.Red);
            avaPlot.Refresh();

            */
            avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
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
    }
}
