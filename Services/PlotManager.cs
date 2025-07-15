using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.Linq;
using AntennaAV.Views;
using Avalonia.Threading;
namespace AntennaAV.Services
{
    public class PlotManager
    {
        private readonly object _plot1Lock = new();
        private readonly object _plot2Lock = new();
        private DispatcherTimer? _avaPlot1RefreshTimer;
        private DispatcherTimer? _avaPlot2RefreshTimer;
        private AvaPlot? _avaPlot1;
        private AvaPlot? _avaPlot2;
        private double? _lastMin = null;
        private double? _lastMax = null;
        private bool _avaPlot1NeedsRefresh = false;
        private bool _avaPlot2NeedsRefresh = false;

        private const double DefaultPlotRadius = 100.0;
        private const double Plot2RadiusFactor = 0.6;
        private const double PointerThreshold = 20.0;
        private const double PointerSnapStep = 10.0;
        private const double AngleGapThresholdEqual = 30.0;
        private const double AngleGapThresholdNotEqual = 1.0;
        private const int TransmitterMarkerSize = 10;
        private const int HoverMarkerSize = 8;
        private const int ArrowWidth = 4;
        private const int ArrowheadWidth = 10;

        private ScottPlot.Plottables.Polygon? _sectorPolygon;
        private ScottPlot.Plottables.PolarAxis? _polarAxis1;
        private ScottPlot.Plottables.PolarAxis? _polarAxis2;
        private ScottPlot.Plottables.Arrow? _angleArrow;
        private ScottPlot.Plottables.Marker? _hoverMarker;
        private ScottPlot.Plottables.Marker? _transmitterMarker;

        private double? _pendingSectorStart = null;
        private double? _pendingSectorEnd = null;
        private bool? _pendingSectorVisible = null;
        private bool _sectorUpdatePending = false;

        /// <summary>
        /// Общий метод для построения и отрисовки полярного графика
        /// </summary>
        public void DrawPolarPlot(
            double[] angles,
            double[] values,
            AvaPlot? plot,
            List<Scatter> dataScatters,
            string colorHex,
            bool isLogScale,
            bool isDark,
            double? min = null,
            double? max = null)
        {
            if (_polarAxis1 == null || plot == null)
                return;
            if (angles == null || values == null || angles.Length == 0 || values.Length == 0 || angles.Length != values.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] Нет данных для построения: angles={angles?.Length ?? -1}, values={values?.Length ?? -1}");
                return;
            }
            
            lock (_plot1Lock)
            {
                double actualMin = min ?? values.Min();
                double actualMax = max ?? values.Max();
                double r_max = DefaultPlotRadius;
                bool allRadiiEqual = Math.Abs(actualMax - actualMin) < 1e-8;
                double angleGapThreshold = allRadiiEqual ? AngleGapThresholdEqual : AngleGapThresholdNotEqual;

                // Обновлять круги только если min/max изменились
                if (_lastMin != actualMin || _lastMax != actualMax)
                {
                    if (_avaPlot1 != null)
                        Plots.AutoUpdatePolarAxisCircles(_avaPlot1, _polarAxis1, isLogScale, actualMin, actualMax, isDark);
                    _lastMin = actualMin;
                    _lastMax = actualMax;
                }

                List<List<ScottPlot.Coordinates>> segments = new();

                // Если все значения одинаковые и углов больше одной — строим линию по всем точкам (замкнутый круг)
                if (allRadiiEqual && angles.Length > 1)
                {
                    List<ScottPlot.Coordinates> circle = new();
                    for (int i = 0; i < angles.Length; i++)
                    {
                        double mirroredAngle = (360 - angles[i]) % 360;
                        double r = r_max;
                        var pt = _polarAxis1.GetCoordinates(r, mirroredAngle);
                        circle.Add(pt);
                    }
                    if (circle.Count > 2)
                        circle.Add(circle[0]);
                    segments.Add(circle);
                }
                else
                {
                    List<ScottPlot.Coordinates> current = new();
                    for (int i = 0; i < angles.Length; i++)
                    {
                        double mirroredAngle = (360 - angles[i]) % 360;
                        double r;
                        if (isLogScale)
                        {
                            r = (actualMax - actualMin) > 0 ? r_max * (values[i] - actualMin) / (actualMax - actualMin) : r_max;
                        }
                        else
                        {
                            r = actualMax > 0 ? r_max * (values[i] / actualMax) : 0;
                        }
                        var pt = _polarAxis1.GetCoordinates(r, mirroredAngle);
                        if (i > 0 && Math.Abs(angles[i] - angles[i - 1]) > angleGapThreshold)
                        {
                            if (current.Count > 0)
                                segments.Add(current);
                            current = new List<ScottPlot.Coordinates>();
                        }
                        current.Add(pt);
                    }
                    if (current.Count > 0)
                        segments.Add(current);
                }

                // Удаляем все старые графики
                foreach (var scatter in dataScatters)
                    plot.Plot.Remove(scatter);
                dataScatters.Clear();

                // Рисуем каждый сегмент отдельно
                var color = ScottPlot.Color.FromHex(colorHex);
                foreach (var seg in segments)
                {
                    if (seg.Count > 1)
                    {
                        var scatter = plot.Plot.Add.Scatter(seg, color: color);
                        scatter.MarkerSize = 0;
                        scatter.LineWidth = 2;
                        dataScatters.Add(scatter);
                    }
                }
                _avaPlot1NeedsRefresh = true;
            }
        }

        public void DrawAllVisiblePlots(
            AntennaAV.ViewModels.MainWindowViewModel vm,
            AvaPlot? plot,
            bool isLogScale,
            bool isDark)
        {
            if (_polarAxis1 == null || plot == null)
                return;
            lock (_plot1Lock)
            {
                try
                {
                    // Очистить старые графики
                    foreach (var tab in vm.Tabs)
                    {
                        foreach (var scatter in tab.DataScatters)
                            plot.Plot.Remove(scatter);
                        tab.DataScatters.Clear();
                    }

                    double? globalMin = null, globalMax = null;
                    // Сначала ищем общий min/max по всем видимым графикам
                    foreach (var tab in vm.Tabs)
                    {
                        if (tab.Plot != null && tab.Plot.IsVisible)
                        {
                            double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                            if (values == null || values.Length <= 1)
                                continue;
                            double min = values.Min();
                            double max = values.Max();
                            globalMin = globalMin.HasValue ? Math.Min(globalMin.Value, min) : min;
                            globalMax = globalMax.HasValue ? Math.Max(globalMax.Value, max) : max;
                        }
                    }
                    // Если нет данных — не обновлять круги и не строить графики
                    if (!globalMin.HasValue || !globalMax.HasValue || globalMax.Value <= globalMin.Value)
                    {
                        return;
                    }
                    // Сначала обновить круги полярной оси
                    if (_avaPlot1 != null)
                        Plots.AutoUpdatePolarAxisCircles(_avaPlot1, _polarAxis1, isLogScale, globalMin.Value, globalMax.Value, isDark);
                    // Теперь строим все графики с общей нормализацией
                    foreach (var tab in vm.Tabs)
                    {
                        if (tab.Plot != null && tab.Plot.IsVisible)
                        {
                            double[] angles = tab.Plot.Angles;
                            double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                            DrawPolarPlot(angles, values, plot, tab.DataScatters, tab.Plot.ColorHex, isLogScale, isDark, globalMin, globalMax);
                        }
                    }
                    _avaPlot1NeedsRefresh = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DrawAllVisiblePlots] Exception: {ex}");
                }
            }
        }

        public void ClearCurrentTabPlot(
            AntennaAV.ViewModels.MainWindowViewModel vm,
            AvaPlot? plot)
        {
            if (plot == null)
                return;
            if (vm.SelectedTab == null || vm.SelectedTab.Plot == null)
                return;
            lock (_plot1Lock)
            {
                try
                {
                    foreach (var scatter in vm.SelectedTab.DataScatters)
                        plot.Plot.Remove(scatter);
                    vm.SelectedTab.DataScatters.Clear();
                    _avaPlot1NeedsRefresh = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClearCurrentTabPlot] Exception: {ex}");
                }
            }
        }

        public void ResetPlotAxes(params AvaPlot?[] plots)
        {
            foreach (var avaPlot in plots)
            {
                if (avaPlot?.Plot != null)
                {
                    avaPlot.Plot.Axes.AutoScale();
                    avaPlot.Refresh();
                }
            }
        }

        public void DrawTransmitterAnglePoint(AvaPlot? plot, double angleDeg)
        {
            if (plot == null || plot.Plot == null)
                return;
            lock (_plot2Lock)
            {
                double radius = DefaultPlotRadius;
                double angleRad = (-angleDeg + 270) * Math.PI / 180.0;
                double x = radius * Math.Cos(angleRad);
                double y = radius * Math.Sin(angleRad);
                if (_transmitterMarker == null)
                {
                    _transmitterMarker = plot.Plot.Add.Marker(
                        x: x,
                        y: y,
                        color: ScottPlot.Color.FromHex("#0073cf"),
                        size: TransmitterMarkerSize
                    );
                }
                else
                {
                    _transmitterMarker.X = x;
                    _transmitterMarker.Y = y;
                }
                _avaPlot2NeedsRefresh = true;
            }
        }

        public void CreateOrUpdateSectorPolygon(AvaPlot? plot, double start, double end, bool isVisible)
        {
            if (plot == null)
                return;
            lock(_plot1Lock)
            {
                _pendingSectorStart = start;
                _pendingSectorEnd = end;
                _pendingSectorVisible = isVisible;
                _sectorUpdatePending = true;
                return;
            }
        }

        // Новый приватный метод для реального обновления полигона
        private void InternalUpdateSectorPolygon(AvaPlot? plot, double start, double end, bool isVisible)
        {
            lock(_plot1Lock)
            {
                // Старая логика из CreateOrUpdateSectorPolygon
                // Вычисляем новые точки для сектора
                var points = new List<ScottPlot.Coordinates>();
                double radius = 100;
                // Проверка на полный круг (размер 0 или 360)
                double sectorSize = (end - start + 360) % 360;
                if (sectorSize == 0)
                {
                    if (_sectorPolygon != null && plot != null)
                    {
                        plot.Plot.Remove(_sectorPolygon);
                        _sectorPolygon = null;
                        _avaPlot1NeedsRefresh = true;
                        return;
                    }



                }
                points.Add(new ScottPlot.Coordinates(0, 0));
                double step = 1; // 1 градус
                if (start < end)
                {
                    for (double angle = start; angle <= end; angle += step)
                    {
                        double theta = (angle + 90) * Math.PI / 180.0;
                        points.Add(new ScottPlot.Coordinates(-radius * Math.Cos(theta), radius * Math.Sin(theta)));
                    }
                }
                else // сектор через 0°
                {
                    for (double angle = start; angle < 360; angle += step)
                    {
                        double theta = (angle + 90) * Math.PI / 180.0;
                        points.Add(new ScottPlot.Coordinates(-radius * Math.Cos(theta), radius * Math.Sin(theta)));
                    }
                    for (double angle = 0; angle <= end; angle += step)
                    {
                        double theta = (angle + 90) * Math.PI / 180.0;
                        points.Add(new ScottPlot.Coordinates(-radius * Math.Cos(theta), radius * Math.Sin(theta)));
                    }
                }
                if (_sectorPolygon == null)
                {
                    if (plot != null)
                    {
                        _sectorPolygon = plot.Plot.Add.Polygon(points.ToArray());
                        _sectorPolygon.FillColor = Colors.DarkGray.WithAlpha(.5);
                        _sectorPolygon.LineWidth = 0;
                        _sectorPolygon.IsVisible = isVisible;
                    }
                }
                else
                {
                    _sectorPolygon.UpdateCoordinates(points.ToArray());
                    _sectorPolygon.IsVisible = isVisible;
                }
                _avaPlot1NeedsRefresh = true;
            }
        }

        public void UpdateHoverMarker(AvaPlot? plot, double mouseX, double mouseY, double plotRadiusPix, double threshold, out double snappedAngle)
        {
            snappedAngle = double.NaN;
            if (plot == null || _polarAxis2 == null)
                return;
            lock (_plot2Lock)
            {
                double centerX = plot.Bounds.Width / 2.0;
                double centerY = plot.Bounds.Height / 2.0;
                double dx = mouseX - centerX;
                double dy = mouseY - centerY;
                double rPix = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(rPix - plotRadiusPix) > threshold)
                {
                    if (_hoverMarker != null && plot.Plot.GetPlottables().Contains(_hoverMarker))
                        _hoverMarker.IsVisible = false;
                    return;
                }
                double angleRad = Math.Atan2(dx, -dy);
                double angleDeg = (angleRad * 180.0 / Math.PI + 360) % 360;
                double mirroredAngle = (360 - angleDeg + 180) % 360;
                double snapStep = PointerSnapStep;
                snappedAngle = Math.Round(mirroredAngle / snapStep) * snapStep;
                snappedAngle = snappedAngle % 360;
                double r = DefaultPlotRadius;
                var coord = _polarAxis2.GetCoordinates(r, snappedAngle);
                if (_hoverMarker == null)
                    _hoverMarker = plot.Plot.Add.Marker(coord.X, coord.Y, color: ScottPlot.Color.FromHex("#FF0000"), size: HoverMarkerSize);
                else
                {
                    _hoverMarker.X = coord.X;
                    _hoverMarker.Y = coord.Y;
                    _hoverMarker.IsVisible = true;
                }
                _avaPlot2NeedsRefresh = true;
            }
        }

        public void SetTransmitterMarkerVisibility(bool isVisible)
        {
            lock (_plot2Lock)
            {
                if (_transmitterMarker != null)
                    _transmitterMarker.IsVisible = isVisible;
            }
            _avaPlot2NeedsRefresh = true;
        }

        public void UpdatePolarAxisCircles(AvaPlot plot, bool isLog, double min, double max, bool isDark)
        {
            lock (_plot1Lock)
            {
                if (_avaPlot1 != null && _polarAxis1 != null)
                    Plots.AutoUpdatePolarAxisCircles(_avaPlot1, _polarAxis1, isLog, min, max, isDark);
            }
            _avaPlot1NeedsRefresh = true;
        }


        public void SetSectorVisibility(bool isVisible)
        {
            lock (_plot1Lock)
            {
                if (_sectorPolygon != null)
                    _sectorPolygon.IsVisible = isVisible;
            }
            _avaPlot1NeedsRefresh = true;
        }

        public void AutoScaleAxes2(AvaPlot plot)
        {
            lock (_plot2Lock)
            {
                plot.Plot.Axes.AutoScale();
            }
            _avaPlot2NeedsRefresh = true;
        }

        public void ApplyThemeToMainPlot(
            bool isDark,
            AvaPlot? avaPlot1)
        {
            lock (_plot1Lock)
            {
                if (avaPlot1 != null && _polarAxis1 != null)
                {
                    Plots.UpdatePolarAxisTheme(_polarAxis1, isDark);
                    Plots.AddCustomSpokeLines(avaPlot1, _polarAxis1, isDark);
                    Plots.SetScottPlotTheme(isDark, false, avaPlot1);
                }

            }
            _avaPlot1NeedsRefresh = true;
        }

        public void ApplyThemeToPlot2(
            bool isDark,
            AvaPlot? avaPlot2)
        {
            lock (_plot2Lock)
            {
                if (avaPlot2 != null && _polarAxis2 != null)
                {
                    Plots.UpdatePolarAxisThemeSmall(_polarAxis2, avaPlot2, isDark);
                    Plots.SetScottPlotTheme(isDark, false, avaPlot2);
                }
            }
            _avaPlot2NeedsRefresh = true;
        }

        public void InitializePlot1(AvaPlot plot, bool isDark)
        {
            _avaPlot1 = plot;
            _polarAxis1 = Plots.Initialize(plot, isDark);
            ApplyThemeToMainPlot(isDark, plot);
            UpdatePolarAxisCircles(plot, true, -50, 0, isDark);
            _avaPlot1RefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _avaPlot1RefreshTimer.Tick += (s, e) =>
            {
                lock (_plot1Lock)
                {
                    if (_sectorUpdatePending && _pendingSectorStart.HasValue && _pendingSectorEnd.HasValue && _pendingSectorVisible.HasValue)
                    {
                        InternalUpdateSectorPolygon(_avaPlot1, _pendingSectorStart.Value, _pendingSectorEnd.Value, _pendingSectorVisible.Value);
                        _sectorUpdatePending = false;
                    }
                    if (_avaPlot1NeedsRefresh && _avaPlot1 != null)
                    {
                        _avaPlot1.Refresh();
                        _avaPlot1NeedsRefresh = false;
                    }
                }
            };
            _avaPlot1RefreshTimer.Start();
        }

        public void InitializePlot2(AvaPlot plot, bool isDark)
        {
            _avaPlot2 = plot;
            _polarAxis2 = Plots.InitializeSmall(plot, isDark);
            ApplyThemeToPlot2(isDark, plot);
            _avaPlot2RefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _avaPlot2RefreshTimer.Tick += (s, e) =>
            {
                lock (_plot2Lock)
                {
                    if (_avaPlot2NeedsRefresh && _avaPlot2 != null)
                    {
                        _avaPlot2.Refresh();
                        _avaPlot2NeedsRefresh = false;
                    }
                }
            };
            _avaPlot2RefreshTimer.Start();
        }

        public void CreateOrUpdateAngleArrow(AvaPlot? plot, double angleDeg, bool isVisible)
        {
            if (plot == null || plot.Plot == null)
                return;
            lock (_plot1Lock)
            {
                double radius = DefaultPlotRadius;
                double angleRad = (-angleDeg + 90) * Math.PI / 180.0;
                double x = radius * Math.Cos(angleRad);
                double y = radius * Math.Sin(angleRad);

                if (_angleArrow == null)
                {
                    _angleArrow = plot.Plot.Add.Arrow(
                        new ScottPlot.Coordinates(0, 0),
                        new ScottPlot.Coordinates(x, y)
                    );
                    _angleArrow.ArrowLineWidth = 0;
                    _angleArrow.ArrowWidth = ArrowWidth;
                    _angleArrow.ArrowheadWidth = ArrowheadWidth;
                    _angleArrow.ArrowFillColor = ScottPlot.Color.FromHex("#0073cf");
                }
                else
                {
                    _angleArrow.Base = new ScottPlot.Coordinates(0, 0);
                    _angleArrow.Tip = new ScottPlot.Coordinates(x, y);
                }
                _angleArrow.IsVisible = isVisible;
                _avaPlot1NeedsRefresh = true;
            }
        }

        public void SetAngleArrowVisibility(bool isVisible)
        {
            lock (_plot1Lock)
            {
                if (_angleArrow != null)
                    _angleArrow.IsVisible = isVisible;
            }
            _avaPlot1NeedsRefresh = true;
        }

        public void MoveAngleArrowToFront(AvaPlot? plot)
        {
            lock (_plot1Lock)
            {
                if (_angleArrow != null && plot != null)
                    plot.Plot.MoveToFront(_angleArrow);
            }
            _avaPlot1NeedsRefresh = true;
        }

        public bool IsHoverMarkerVisible()
        {
            lock (_plot2Lock)
            {
                return _hoverMarker?.IsVisible ?? false;
            }
        }
    }
}

