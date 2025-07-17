using AntennaAV.ViewModels;
using AntennaAV.Views;
using Avalonia.Threading;
using HarfBuzzSharp;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Colormaps;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
namespace AntennaAV.Services
{
    public class PlotManager
    {
        private readonly object _plotMainLock = new();
        private readonly object _plotTxLock = new();
        private readonly object _plotRxLock = new();
        private DispatcherTimer? _avaPlotRefreshTimer;
        private AvaPlot? _avaPlotMain;
        private AvaPlot? _avaPlotTx;
        private AvaPlot? _avaPlotRx;
        private double? _globalMin = null;
        private double? _globalMax = null;
        private bool _avaPlotMainNeedsRefresh = false;
        private bool _avaPlotTxNeedsRefresh = false;
        private bool _avaPlotRxNeedsRefresh = false;


        private ScottPlot.Plottables.Polygon? _sectorPolygon;
        private ScottPlot.Plottables.PolarAxis? _polarAxisMain;
        private ScottPlot.Plottables.PolarAxis? _polarAxisTx;
        private ScottPlot.Plottables.PolarAxis? _polarAxisRx;

        private ScottPlot.Plottables.Arrow? _angleArrow;
        private ScottPlot.Plottables.Marker? _hoverMarkerTx;
        private ScottPlot.Plottables.Marker? _hoverMarkerRx;
        private ScottPlot.Plottables.Marker? _transmitterMarker;
        private ScottPlot.Plottables.Marker? _receiverMarker;


        private double? _pendingSectorStart = null;
        private double? _pendingSectorEnd = null;
        private bool? _pendingSectorVisible = null;
        private bool _sectorUpdatePending = false;


        public void DrawPolarPlotFromValues(
            double[] angles,
            double[] values,
            AvaPlot? plot,
            List<Scatter> dataScatters,
            string colorHex,
            bool isLogScale,
            bool isDark,
            double min,
            double max,
            string? label = null)
        {
            if (angles == null || values == null || angles.Length == 0 || values.Length == 0 || angles.Length != values.Length || _polarAxisMain == null || plot == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlotFromValues] exception");
                return;
            }

            lock (_plotMainLock)
            {
                List<List<ScottPlot.Coordinates>> segments = new();
                List<ScottPlot.Coordinates> current = new();
                double r_max = Constants.DefaultPlotRadius;
                for (int i = 0; i < angles.Length; i++)
                {
                    double mirroredAngle = (360 - angles[i]) % 360;
                    double r;
                    if (isLogScale)
                    {
                        r = (max - min) > 0 ? r_max * (values[i] - min) / (max - min) : r_max;

                    }
                    else
                    {
                        r = max > 0 ? Constants.DefaultPlotRadius * (values[i] / max) : 0;
                    }
                    var pt = _polarAxisMain.GetCoordinates(r, mirroredAngle);
                    if (i > 0 && Math.Abs(angles[i] - angles[i - 1]) > 10)
                    {
                        if (current.Count > 0)
                            segments.Add(current);
                        current = new List<ScottPlot.Coordinates>();
                    }
                    current.Add(pt);
                }
                if (current.Count > 0)
                    segments.Add(current);

                // Рисуем каждый сегмент отдельно
                var color = ScottPlot.Color.FromHex(colorHex);
                bool first = true;
                foreach (var seg in segments)
                {
                    if (seg.Count > 1)
                    {
                        var scatter = plot.Plot.Add.Scatter(seg, color: color);
                        scatter.LineWidth = 2;
                        scatter.MarkerSize = 0;
                        if (first && !string.IsNullOrEmpty(label))
                        {
                            scatter.LegendText = label;
                            first = false;
                        }
                        dataScatters.Add(scatter);
                    }
                }
                _avaPlotMainNeedsRefresh = true;
            }
        }


        public void DrawPolarPlot(IEnumerable<TabViewModel> tabs,
            double[] angles,
            double[] values,
            AvaPlot? plot,
            List<Scatter> dataScatters,
            string colorHex,
            bool isLogScale,
            bool isDark,
            string? label = null)
        {
            if (angles == null || values == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] angles или values == null");
                return;
            }
            if (angles.Length == 0 || values.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] angles или values пусты");
                return;
            }
            if (angles.Length != values.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] angles.Length != values.Length: {angles.Length} != {values.Length}");
                return;
            }
            if (_polarAxisMain == null || plot == null)
                return;
            lock (_plotMainLock)
            {
                bool isUpdated = UpdateGlobalMinMax(values);
                double actualMin = _globalMin ?? values.Min();
                double actualMax = _globalMax ?? values.Max();


                if (isUpdated)
                {
                    Plots.AutoUpdatePolarAxisCircles(plot, _polarAxisMain, isLogScale, actualMin, actualMax, isDark);
                    DrawAllVisiblePlots(tabs, plot, isLogScale, isDark);
                }
                else 
                {
                    // Удаляем все старые графики
                    foreach (var scatter in dataScatters)
                        plot.Plot.Remove(scatter);
                    dataScatters.Clear();
                    DrawPolarPlotFromValues(angles, values, plot, dataScatters, colorHex, isLogScale, isDark, actualMin, actualMax, label);
                }

                    _avaPlotMainNeedsRefresh = true;
            }
        }

        private bool UpdateGlobalMinMax(double[]? values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }
            double localMin = values.Min();
            double localMax = values.Max();
            bool isUpdated = false;

            if (_globalMax == null || _globalMax < localMax)
            {
                _globalMax = localMax;
                isUpdated = true;

            }
            if (_globalMin == null || _globalMin > localMin)
            {
                _globalMin = localMin;
                isUpdated = true;
            }
            return isUpdated;
        }

        public bool UpdateGlobalMinMaxForAllTabs(IEnumerable<TabViewModel> tabs, bool isLogScale)
        {
            bool isUpdated = false;
            foreach (var tab in tabs)
            {
                if (tab == null)
                    continue;
                var values = isLogScale ? tab.Plot?.PowerNormValues : tab.Plot?.VoltageNormValues;
                isUpdated = UpdateGlobalMinMax(values);
            }
            return isUpdated;
        }

        public void DrawAllVisiblePlots(IEnumerable<TabViewModel> tabs,
            AvaPlot? plot,
            bool isLogScale,
            bool isDark)
        {
            if (_polarAxisMain == null || plot == null)
                return;
            lock (_plotMainLock)
            {
                try
                {
                    bool isUpdated = false;
                    _globalMax = null;
                    _globalMin = null;
                    isUpdated = UpdateGlobalMinMaxForAllTabs(tabs, isLogScale);
                    // Очистить старые графики
                    foreach (var tab in tabs)
                    {
                        foreach (var scatter in tab.DataScatters)
                            plot.Plot.Remove(scatter);
                        tab.DataScatters.Clear();
                    }

                    // Используем только кэшированные значения!

                    double globalMin = _globalMin ?? 0;
                    double globalMax = _globalMax ?? -1;
                    if (globalMax <= globalMin)
                        return;

                    // Обновить круги полярной оси
                    if (_avaPlotMain != null)
                        Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, isLogScale, globalMin, globalMax, isDark);

                    // Строим все графики с общей нормализацией
                    foreach (var tab in tabs)
                    {
                        if (tab.Plot != null && tab.Plot.IsVisible)
                        {
                            if (tab.Plot.Angles == null)
                                continue; // Не строим пустые графики!
                            double[] angles = tab.Plot.Angles.ToArray();
                            double[] values = isLogScale ? tab.Plot.PowerNormValues.ToArray() : tab.Plot.VoltageNormValues.ToArray();
                            System.Diagnostics.Debug.WriteLine($"Вкладка: {tab.Header}, angles: {angles.Length}, values: {values.Length}");
                            DrawPolarPlotFromValues(angles, values, plot, tab.DataScatters, tab.Plot.ColorHex, isLogScale, isDark, globalMin, globalMax, tab.Header);
                        }
                    }
                    _avaPlotMainNeedsRefresh = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DrawAllVisiblePlots] Exception: {ex}");
                }
            }
        }

        public void ClearCurrentTabPlot(TabViewModel tab, AvaPlot? plot)
        {
            if (plot == null)
                return;
            if (tab == null || tab.Plot == null)
                return;
            lock (_plotMainLock)
            {
                try
                {
                    foreach (var scatter in tab.DataScatters)
                        plot.Plot.Remove(scatter);
                    tab.DataScatters.Clear();
                    _avaPlotMainNeedsRefresh = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClearCurrentTabPlot] Exception: {ex}");
                }
            }
        }
        

        public void ResetPlotAxes()
        {
            lock (_plotMainLock)
            {
                if(_avaPlotMain != null)
                    _avaPlotMain.Plot.Axes.AutoScale();
                _avaPlotMainNeedsRefresh = true;
            }
            lock (_plotTxLock)
            {
                if (_avaPlotTx != null)
                    _avaPlotTx.Plot.Axes.AutoScale();
                _avaPlotTxNeedsRefresh = true;
            }
            lock (_plotRxLock)
            {
                if (_avaPlotRx != null)
                    _avaPlotRx.Plot.Axes.AutoScale();
                _avaPlotRxNeedsRefresh = true;
            }
        }

        public void DrawTransmitterAnglePoint(AvaPlot? plot, double angleDeg)
        {
            
            if (plot == null || plot.Plot == null)
                return;
            DrawAnglePoint(
                plot,
                angleDeg,
                _plotTxLock,
                ref _transmitterMarker,
                ref _avaPlotTxNeedsRefresh,
                270
            );
        }

        public void DrawReceiverAnglePoint(AvaPlot? plot, double angleDeg)
        {

            if (plot == null || plot.Plot == null)
                return;
            DrawAnglePoint(
                plot,
                angleDeg,
                _plotRxLock,
                ref _receiverMarker,
                ref _avaPlotRxNeedsRefresh,
                90
            );
        }
        public void DrawAnglePoint(
            AvaPlot? plot,
            double angleDeg,
            object plotLock,
            ref ScottPlot.Plottables.Marker? marker,
            ref bool needsRefreshFlag,
            double rotation = 0)
        {
            if (plot == null || plot.Plot == null)
                return;
            lock (plotLock)
            {
                double radius = Constants.DefaultPlotRadius;
                double angleRad = (-angleDeg + rotation) * Math.PI / 180.0;
                double x = radius * Math.Cos(angleRad);
                double y = radius * Math.Sin(angleRad);
                if (marker == null)
                {
                    marker = plot.Plot.Add.Marker(
                        x: x,
                        y: y,
                        color: ScottPlot.Color.FromHex("#0073cf"),
                        size: Constants.MarkerSize
                    );
                }
                else
                {
                    marker.X = x;
                    marker.Y = y;
                }
                needsRefreshFlag = true;
            }
        }
        public void CreateOrUpdateSectorPolygon(AvaPlot? plot, double start, double end, bool isVisible)
        {
            if (plot == null)
                return;
            lock(_plotMainLock)
            {
                _pendingSectorStart = start;
                _pendingSectorEnd = end;
                _pendingSectorVisible = isVisible;
                _sectorUpdatePending = true;
                return;
            }
        }

        private void InternalUpdateSectorPolygon(AvaPlot? plot, double start, double end, bool isVisible)
        {
            lock(_plotMainLock)
            {
                // Вычисляем новые точки для сектора
                var points = new List<ScottPlot.Coordinates>();
                double radius = Constants.DefaultPlotRadius;
                // Проверка на полный круг (размер 0 или 360)
                double sectorSize = (end - start + 360) % 360;
                if (sectorSize == 0)
                {
                    if (_sectorPolygon != null && plot != null)
                    {
                        plot.Plot.Remove(_sectorPolygon);
                        _sectorPolygon = null;
                        _avaPlotMainNeedsRefresh = true;
                        return;
                    }
                    return;
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
                _avaPlotMainNeedsRefresh = true;
            }
        }

        public void UpdateHoverMarkerTx(
            AvaPlot? plot,
            double mouseX,
            double mouseY,
            double plotRadiusPix,
            double threshold,
            out double snappedAngle)
        {
            double localSnappedAngle;
            UpdateHoverMarker(
                plot,
                _polarAxisTx,
                _plotTxLock,
                ref _hoverMarkerTx,
                ref _avaPlotTxNeedsRefresh,
                mouseX,
                mouseY,
                plotRadiusPix,
                threshold,
                out localSnappedAngle
            );
            snappedAngle = localSnappedAngle;
        }

        public void UpdateHoverMarkerRx(
            AvaPlot? plot,
            double mouseX,
            double mouseY,
            double plotRadiusPix,
            double threshold,
            out double snappedAngle)
        {
            double localSnappedAngle;
            UpdateHoverMarker(
                plot,
                _polarAxisRx,
                _plotRxLock,
                ref _hoverMarkerRx,
                ref _avaPlotRxNeedsRefresh,
                mouseX,
                mouseY,
                plotRadiusPix,
                threshold,
                out localSnappedAngle,
                180
            );
            snappedAngle = localSnappedAngle;
        }

        private void UpdateHoverMarker(
            AvaPlot? plot,
            ScottPlot.Plottables.PolarAxis? polarAxis,
            object plotLock,
            ref ScottPlot.Plottables.Marker? hoverMarker,
            ref bool needsRefreshFlag,
            double mouseX,
            double mouseY,
            double plotRadiusPix,
            double threshold,
            out double snappedAngle,
            double rotation = 0)
        {
            snappedAngle = double.NaN;
            if (plot == null || polarAxis == null)
                return;
            lock (plotLock)
            {
                double centerX = plot.Bounds.Width / 2.0;
                double centerY = plot.Bounds.Height / 2.0;
                double dx = mouseX - centerX;
                double dy = mouseY - centerY;
                double rPix = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(rPix - plotRadiusPix) > threshold)
                {
                    if (hoverMarker != null && plot.Plot.GetPlottables().Contains(hoverMarker))
                        hoverMarker.IsVisible = false;
                    return;
                }
                double angleRad = Math.Atan2(dx, -dy);
                double angleDeg = (angleRad * 180.0 / Math.PI + 360) % 360;
                double mirroredAngle = (360 - angleDeg + 180 + rotation) % 360;
                double snapStep = Constants.PointerSnapStep;
                snappedAngle = Math.Round(mirroredAngle / snapStep) * snapStep;
                snappedAngle = snappedAngle % 360;
                double r = Constants.DefaultPlotRadius;
                var coord = polarAxis.GetCoordinates(r, snappedAngle);
                if (hoverMarker == null)
                    hoverMarker = plot.Plot.Add.Marker(coord.X, coord.Y, color: ScottPlot.Color.FromHex("#FF0000"), size: Constants.HoverMarkerSize);
                else
                {
                    hoverMarker.X = coord.X;
                    hoverMarker.Y = coord.Y;
                    hoverMarker.IsVisible = true;
                }
                needsRefreshFlag = true;
            }
        }

        public void SetTxHoverMarkerVisibility(bool isVisible)
        {
            lock (_plotTxLock)
            {
                if (_hoverMarkerTx != null)
                    _hoverMarkerTx.IsVisible = isVisible;
            }
            _avaPlotTxNeedsRefresh = true;
        }
        public void SetRxHoverMarkerVisibility(bool isVisible)
        {
            lock (_plotRxLock)
            {
                if (_hoverMarkerRx != null)
                    _hoverMarkerRx.IsVisible = isVisible;
            }
            _avaPlotRxNeedsRefresh = true;
        }

        public void UpdatePolarAxisCircles(AvaPlot plot, bool isLog, double min, double max, bool isDark)
        {
            lock (_plotMainLock)
            {
                if (_avaPlotMain != null && _polarAxisMain != null)
                    Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, isLog, min, max, isDark);
            }
            _avaPlotMainNeedsRefresh = true;
        }


        public void SetSectorVisibility(bool isVisible)
        {
            lock (_plotMainLock)
            {
                if (_sectorPolygon != null)
                    _sectorPolygon.IsVisible = isVisible;
            }
            _avaPlotMainNeedsRefresh = true;
        }

        public void ApplyThemeToMainPlot(
            bool isDark,
            AvaPlot? avaPlotMain)
        {
            lock (_plotMainLock)
            {
                if (avaPlotMain != null && _polarAxisMain != null)
                {
                    Plots.UpdatePolarAxisTheme(_polarAxisMain, isDark);
                    Plots.AddCustomSpokeLines(avaPlotMain, _polarAxisMain, isDark);
                    Plots.SetScottPlotTheme(isDark, false, avaPlotMain);
                }

            }
            _avaPlotMainNeedsRefresh = true;
        }


        public void ApplyThemeToPlotSmall(
            bool isDark,
            AvaPlot? plot,
            object plotLock,
            ScottPlot.Plottables.PolarAxis? polarAxis,
            ref bool needsRefreshFlag)
        {
            lock (plotLock)
            {
                if (plot != null && polarAxis != null)
                {
                    Plots.UpdatePolarAxisThemeSmall(polarAxis, plot, isDark);
                    Plots.SetScottPlotTheme(isDark, false, plot);
                }
            }
            needsRefreshFlag = true;
        }

        public void InitializePlotMain(AvaPlot plot, bool isDark)
        {
            _avaPlotMain = plot;
            _polarAxisMain = Plots.Initialize(plot, isDark);
            ApplyThemeToMainPlot(isDark, plot);
            UpdatePolarAxisCircles(plot, true, -50, 0, isDark);
            InitializeRefreshTimer();
        }

        private void InitializeRefreshTimer()
        {
            _avaPlotRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.PlotTimerUpdateIntervalMs) };
            _avaPlotRefreshTimer.Tick += (s, e) =>
            {
                if (_sectorUpdatePending && _pendingSectorStart.HasValue && _pendingSectorEnd.HasValue && _pendingSectorVisible.HasValue)
                {
                    InternalUpdateSectorPolygon(_avaPlotMain, _pendingSectorStart.Value, _pendingSectorEnd.Value, _pendingSectorVisible.Value);
                    MoveAngleArrowToFront(_avaPlotMain);
                    _sectorUpdatePending = false;
                }

                if (_avaPlotMainNeedsRefresh && _avaPlotMain != null)
                {
                    lock (_plotMainLock)
                    {
                        _avaPlotMain.Refresh();
                        _avaPlotMainNeedsRefresh = false;
                    }
                }

                if (_avaPlotTxNeedsRefresh && _avaPlotTx != null)
                {
                    lock (_plotTxLock)
                    {
                        _avaPlotTx.Refresh();
                        _avaPlotTxNeedsRefresh = false;
                    }
                }
                if (_avaPlotRxNeedsRefresh && _avaPlotRx != null)
                {
                    lock (_plotRxLock)
                    {
                        _avaPlotRx.Refresh();
                        _avaPlotRxNeedsRefresh = false;
                    }
                }
            };
            _avaPlotRefreshTimer.Start();
        }

        public void InitializeTxPlot(AvaPlot plot, bool isDark)
        {
            _avaPlotTx = plot;
            _polarAxisTx = Plots.InitializeSmall(plot, isDark);
            ApplyThemeToPlotSmall(
                isDark,
                plot,
                _plotTxLock,
                _polarAxisTx,
                ref _avaPlotTxNeedsRefresh
            );
        }

        public void InitializeRxPlot(AvaPlot plot, bool isDark)
        {
            _avaPlotRx = plot;
            _polarAxisRx = Plots.InitializeSmall(plot, isDark, 90);
            ApplyThemeToPlotSmall(
                isDark,
                plot,
                _plotRxLock,
                _polarAxisRx,
                ref _avaPlotRxNeedsRefresh
            );
        }

        public void ApplyThemeToPlotTx(AvaPlot plot, bool isDark)
        {
            if (plot != null && _polarAxisTx != null)
                ApplyThemeToPlotSmall(
                isDark,
                plot,
                _plotTxLock,
                _polarAxisTx,
                ref _avaPlotTxNeedsRefresh
            );
        }

        public void ApplyThemeToPlotRx(AvaPlot plot, bool isDark)
        {
            if (plot != null && _polarAxisTx != null)
                ApplyThemeToPlotSmall(
                isDark,
                plot,
                _plotRxLock,
                _polarAxisRx,
                ref _avaPlotRxNeedsRefresh
            );
        }


        public void CreateOrUpdateAngleArrow(AvaPlot? plot, double angleDeg)
        {
            if (plot == null || plot.Plot == null)
                return;
            lock (_plotMainLock)
            {
                double radius = Constants.DefaultPlotRadius;
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
                    _angleArrow.ArrowWidth = Constants.ArrowWidth;
                    _angleArrow.ArrowheadWidth = Constants.ArrowheadWidth;
                    _angleArrow.ArrowFillColor = ScottPlot.Color.FromHex("#0073cf");
                    plot.Plot.MoveToFront(_angleArrow);                }
                else
                {
                    _angleArrow.Base = new ScottPlot.Coordinates(0, 0);
                    _angleArrow.Tip = new ScottPlot.Coordinates(x, y);
                }
                _avaPlotMainNeedsRefresh = true;
            }
        }

        public void SetAngleArrowVisibility(bool isVisible)
        {
            lock (_plotMainLock)
            {
                if (_angleArrow != null)
                    _angleArrow.IsVisible = isVisible;
            }
            _avaPlotMainNeedsRefresh = true;
        }

        public void MoveAngleArrowToFront(AvaPlot? plot)
        {
            lock (_plotMainLock)
            {
                if (_angleArrow != null && plot != null)
                    plot.Plot.MoveToFront(_angleArrow);
            }
            _avaPlotMainNeedsRefresh = true;
        }

        public bool IsHoverMarkerTxVisible()
        {
            lock (_plotTxLock)
            {
                return _hoverMarkerTx?.IsVisible ?? false;
            }
        }

        public bool IsHoverMarkerRxVisible()
        {
            lock (_plotTxLock)
            {
                return _hoverMarkerRx?.IsVisible ?? false;
            }
        }

        public void SetLegendVisibility(bool isVisible)
        {
            lock (_plotMainLock)
            {
                if (_avaPlotMain != null)
                {
                    if(isVisible)
                    {
                        _avaPlotMain.Plot.ShowLegend(Alignment.LowerRight, Orientation.Vertical);
                    }
                        
                    else
                    {
                        _avaPlotMain.Plot.HideLegend();
                        _avaPlotMainNeedsRefresh = true;
                    }
                }
            }
        }

        public async System.Threading.Tasks.Task SaveMainPlotToPngAsync(string filePath, bool isDark)
        {
            if (_avaPlotMain == null)
                return;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_plotMainLock)
                {
                    // Сохраняем текущие состояния
                    bool arrowVisible = _angleArrow?.IsVisible ?? false;
                    bool sectorVisible = _sectorPolygon?.IsVisible ?? false;

                    // Скрываем стрелку и полигон
                    if (_angleArrow != null) _angleArrow.IsVisible = false;
                    if (_sectorPolygon != null) _sectorPolygon.IsVisible = false;

                    // Применяем светлую тему
                    ApplyThemeToMainPlot(false, _avaPlotMain);

                    _avaPlotMain.Refresh();

                    // Сохраняем PNG
                    _avaPlotMain.Plot.SavePng(filePath, 900, 900);

                    // Возвращаем всё обратно
                    if (_angleArrow != null) _angleArrow.IsVisible = arrowVisible;
                    if (_sectorPolygon != null) _sectorPolygon.IsVisible = sectorVisible;
                    ApplyThemeToMainPlot(isDark, _avaPlotMain);
                    _avaPlotMain.Refresh();
                    ResetPlotAxes();
                }
            });
        }
    }
}

