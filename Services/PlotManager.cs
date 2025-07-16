using AntennaAV.ViewModels;
using AntennaAV.Views;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
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
        private double? _lastMin = null;
        private double? _lastMax = null;
        private double? _globalMin = null;
        private double? _globalMax = null;
        private TabViewModel? _minSourceTab = null;
        private TabViewModel? _maxSourceTab = null;
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
        private bool _showLegend = true;
        private bool _lastLegendState = true;
        public void SetShowLegend(bool value)
        {
            _showLegend = value;
        }

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
            double? max = null,
            string? label = null)
        {
            System.Diagnostics.Debug.WriteLine($"DrawPolarPlot: label={label}, angles={angles?.Length}, values={values?.Length}");
            if (_polarAxisMain == null || plot == null)
                return;
            if (angles == null || values == null || angles.Length == 0 || values.Length == 0 || angles.Length != values.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] Нет данных для построения: angles={angles?.Length ?? -1}, values={values?.Length ?? -1}");
                return;
            }
            lock (_plotMainLock)
            {
                double actualMin = min ?? values.Min();
                double actualMax = max ?? values.Max();
                double r_max = Constants.DefaultPlotRadius;
                bool allRadiiEqual = Math.Abs(actualMax - actualMin) < 1e-8;
                double angleGapThreshold = allRadiiEqual ? Constants.AngleGapThresholdEqual : Constants.AngleGapThresholdNotEqual;

                // Обновлять круги только если min/max изменились
                if (_lastMin != actualMin || _lastMax != actualMax)
                {
                    if (_avaPlotMain != null)
                        Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, isLogScale, actualMin, actualMax, isDark);
                    _lastMin = actualMin;
                    _lastMax = actualMax;
                }

                List<List<ScottPlot.Coordinates>> segments = new();

                // Если все значения одинаковые и углов больше одного, строим линию по всем точкам
                if (allRadiiEqual && angles.Length > 1)
                {
                    List<ScottPlot.Coordinates> circle = new();
                    for (int i = 0; i < angles.Length; i++)
                    {
                        double mirroredAngle = (360 - angles[i]) % 360;
                        double r = r_max;
                        var pt = _polarAxisMain.GetCoordinates(r, mirroredAngle);
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
                        var pt = _polarAxisMain.GetCoordinates(r, mirroredAngle);
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
                bool first = true;
                foreach (var seg in segments)
                {
                    if (seg.Count > 1)
                    {
                        var scatter = plot.Plot.Add.Scatter(seg, color: color);
                        scatter.MarkerSize = 0;
                        scatter.LineWidth = 2;
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

        public void UpdateGlobalMinMaxOnActiveChange(
            TabViewModel activeTab,
            IEnumerable<TabViewModel> allTabs,
            bool isLogScale)
        {
            var values = isLogScale ? activeTab.Plot.PowerNormValues : activeTab.Plot.VoltageNormValues;
            bool activeIsEmpty = (values == null || values.Length == 0);

            bool needFullRecalc = activeIsEmpty;

            if (!activeIsEmpty && values != null)
            {
                double localMin = values.Min();
                double localMax = values.Max();

                // Проверяем, был ли глобальный min в этой вкладке и исчез ли он
                if (_minSourceTab == activeTab && (_globalMin == null || !values.Contains(_globalMin.Value)))
                    needFullRecalc = true;

                // Проверяем, был ли глобальный max в этой вкладке и исчез ли он
                if (_maxSourceTab == activeTab && (_globalMax == null || !values.Contains(_globalMax.Value)))
                    needFullRecalc = true;
            }

            if (needFullRecalc)
            {
                // Пересчитываем по всем вкладкам, игнорируя пустые!
                double? min = null, max = null;
                TabViewModel? minTab = null, maxTab = null;
                foreach (var tab in allTabs)
                {
                    if (tab.Plot != null && tab.Plot.IsVisible)
                    {
                        var vals = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                        if (vals == null || vals.Length == 0) continue;
                        double tmin = vals.Min();
                        double tmax = vals.Max();
                        if (!min.HasValue || tmin < min.Value)
                        {
                            min = tmin;
                            minTab = tab;
                        }
                        if (!max.HasValue || tmax > max.Value)
                        {
                            max = tmax;
                            maxTab = tab;
                        }
                    }
                }
                _globalMin = min;
                _globalMax = max;
                _minSourceTab = minTab;
                _maxSourceTab = maxTab;
            }
            else
            {
                // Просто обновляем глобальные значения, если нужно
                double localMin = values.Min();
                double localMax = values.Max();
                if (_globalMin == null || localMin < _globalMin)
                {
                    _globalMin = localMin;
                    _minSourceTab = activeTab;
                }
                if (_globalMax == null || localMax > _globalMax)
                {
                    _globalMax = localMax;
                    _maxSourceTab = activeTab;
                }
            }
        }

        //public void DrawAllVisiblePlots(
        //    AntennaAV.ViewModels.MainWindowViewModel vm,
        //    AvaPlot? plot,
        //    bool isLogScale,
        //    bool isDark)
        //{
        //    if (_polarAxisMain == null || plot == null)
        //        return;
        //    lock (_plotMainLock)
        //    {
        //        try
        //        {
        //            // Очистить старые графики
        //            foreach (var tab in vm.Tabs)
        //            {
        //                foreach (var scatter in tab.DataScatters)
        //                    plot.Plot.Remove(scatter);
        //                tab.DataScatters.Clear();
        //            }

        //            double? globalMin = null, globalMax = null;
        //            // Сначала ищем общий min/max по всем видимым графикам
        //            foreach (var tab in vm.Tabs)
        //            {
        //                if (tab.Plot != null && tab.Plot.IsVisible)
        //                {
        //                    double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
        //                    if (values == null || values.Length <= 1)
        //                        continue;
        //                    double min = values.Min();
        //                    double max = values.Max();
        //                    globalMin = globalMin.HasValue ? Math.Min(globalMin.Value, min) : min;
        //                    globalMax = globalMax.HasValue ? Math.Max(globalMax.Value, max) : max;
        //                }
        //            }
        //            // Если нет данных — не обновлять круги и не строить графики
        //            if (!globalMin.HasValue || !globalMax.HasValue || globalMax.Value <= globalMin.Value)
        //            {
        //                return;
        //            }
        //            // Сначала обновить круги полярной оси
        //            if (_avaPlotMain != null)
        //                Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, isLogScale, globalMin.Value, globalMax.Value, isDark);
        //            // Теперь строим все графики с общей нормализацией
        //            foreach (var tab in vm.Tabs)
        //            {
        //                if (tab.Plot != null && tab.Plot.IsVisible)
        //                {
        //                    double[] angles = tab.Plot.Angles;
        //                    double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
        //                    // Передаём label
        //                    DrawPolarPlot(angles, values, plot, tab.DataScatters, tab.Plot.ColorHex, isLogScale, isDark, globalMin, globalMax, tab.Header);
        //                }
        //            }
        //            _avaPlotMainNeedsRefresh = true;
        //        }
        //        catch (Exception ex)
        //        {
        //            System.Diagnostics.Debug.WriteLine($"[DrawAllVisiblePlots] Exception: {ex}");
        //        }
        //    }
        //}

        public void DrawAllVisiblePlots(
            MainWindowViewModel vm,
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
                    // Обновляем глобальные min/max по активной вкладке
                    if (vm.SelectedTab != null)
                        UpdateGlobalMinMaxOnActiveChange(vm.SelectedTab, vm.Tabs, isLogScale);

                    // Очистить старые графики
                    foreach (var tab in vm.Tabs)
                    {
                        foreach (var scatter in tab.DataScatters)
                            plot.Plot.Remove(scatter);
                        tab.DataScatters.Clear();
                    }

                    // Используем только кэшированные значения!
                    double? globalMin = _globalMin;
                    double? globalMax = _globalMax;
                    if (!globalMin.HasValue || !globalMax.HasValue || globalMax.Value <= globalMin.Value)
                        return;

                    // Обновить круги полярной оси
                    if (_avaPlotMain != null)
                        Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, isLogScale, globalMin.Value, globalMax.Value, isDark);

                    // Строим все графики с общей нормализацией
                    foreach (var tab in vm.Tabs)
                    {
                        if (tab.Plot != null && tab.Plot.IsVisible)
                        {
                            double[] angles = tab.Plot.Angles;
                            double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                            System.Diagnostics.Debug.WriteLine($"Вкладка: {tab.Header}, angles: {angles.Length}, values: {values.Length}");
                            if (angles.Length == 0 || values.Length == 0)
                                continue; // Не строим пустые графики!
                            DrawPolarPlot(angles, values, plot, tab.DataScatters, tab.Plot.ColorHex, isLogScale, isDark, globalMin, globalMax, tab.Header);
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

        public void ClearCurrentTabPlot(
            AntennaAV.ViewModels.MainWindowViewModel vm,
            AvaPlot? plot)
        {
            if (plot == null)
                return;
            if (vm.SelectedTab == null || vm.SelectedTab.Plot == null)
                return;
            lock (_plotMainLock)
            {
                try
                {
                    foreach (var scatter in vm.SelectedTab.DataScatters)
                        plot.Plot.Remove(scatter);
                    vm.SelectedTab.DataScatters.Clear();
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
                ref _avaPlotTxNeedsRefresh
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
                ref _avaPlotRxNeedsRefresh
            );
        }

        public void DrawAnglePoint(
            AvaPlot? plot,
            double angleDeg,
            object plotLock,
            ref ScottPlot.Plottables.Marker? marker,
            ref bool needsRefreshFlag)
        {
            if (plot == null || plot.Plot == null)
                return;
            lock (plotLock)
            {
                double radius = Constants.DefaultPlotRadius;
                double angleRad = (-angleDeg + 270) * Math.PI / 180.0;
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

        // Новый приватный метод для реального обновления полигона
        private void InternalUpdateSectorPolygon(AvaPlot? plot, double start, double end, bool isVisible)
        {
            lock(_plotMainLock)
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
                        _avaPlotMainNeedsRefresh = true;
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
                out localSnappedAngle
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
            out double snappedAngle)
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
                double mirroredAngle = (360 - angleDeg + 180) % 360;
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

        public void SetTransmitterMarkerVisibility(bool isVisible)
        {
            lock (_plotTxLock)
            {
                if (_transmitterMarker != null)
                    _transmitterMarker.IsVisible = isVisible;
            }
            _avaPlotTxNeedsRefresh = true;
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
            _avaPlotRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _avaPlotRefreshTimer.Tick += (s, e) =>
            {
                if (_sectorUpdatePending && _pendingSectorStart.HasValue && _pendingSectorEnd.HasValue && _pendingSectorVisible.HasValue)
                {
                    InternalUpdateSectorPolygon(_avaPlotMain, _pendingSectorStart.Value, _pendingSectorEnd.Value, _pendingSectorVisible.Value);
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
                // Проверяем изменение showLegend
                if (_avaPlotMain != null && _showLegend != _lastLegendState)
                {
                    _avaPlotMain.Plot.Legend.IsVisible = _showLegend;
                    _avaPlotMain.Refresh();
                    _lastLegendState = _showLegend;
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
            _polarAxisRx = Plots.InitializeSmall(plot, isDark);
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


        public void CreateOrUpdateAngleArrow(AvaPlot? plot, double angleDeg, bool isVisible)
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
                }
                else
                {
                    _angleArrow.Base = new ScottPlot.Coordinates(0, 0);
                    _angleArrow.Tip = new ScottPlot.Coordinates(x, y);
                }
                _angleArrow.IsVisible = isVisible;
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

        public bool IsHoverMarkerVisible()
        {
            lock (_plotTxLock)
            {
                return _hoverMarkerTx?.IsVisible ?? false;
            }
        }

        public void SetLegendVisibility(bool isVisible)
        {
            lock (_plotMainLock)
            {
                if (_avaPlotMain != null)
                {
                    if(isVisible)
                        _avaPlotMain.Plot.ShowLegend(Alignment.LowerRight);
                    else
                        _avaPlotMain.Plot.HideLegend();
                        _avaPlotMainNeedsRefresh = true;
                }
            }
        }
    }
}

