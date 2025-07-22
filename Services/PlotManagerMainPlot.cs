using AntennaAV.Helpers;
using AntennaAV.ViewModels;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.Linq;
namespace AntennaAV.Services
{

    // Helper class to store plot segment data for coordinate updates
    public class PlotSegmentData
    {
        public double[] SegmentAngles { get; set; } = Array.Empty<double>(); // Angles for this segment
        public double[] SegmentValues { get; set; } = Array.Empty<double>(); // Values for this segment
        public ScottPlot.Coordinates[] CoordinatesArray { get; set; } // Fixed-length array for coordinates
        public Scatter? ScatterPlot { get; set; }
        public int ValidPointCount { get; set; } // How many points are actually used in the array

        public PlotSegmentData(int maxCapacity = 3600)
        {
            CoordinatesArray = new ScottPlot.Coordinates[maxCapacity];
        }
    }

    public enum ScaleMode
    {
        Auto,           // Текущее поведение - динамический расчет
        Manual          // Пользователь задает диапазон
    }

    public class ScaleSettings
    {
        public double ManualMin { get; set; } = -50;
        public double ManualMax { get; set; } = 0;

        public bool UseAutoMinLimit { get; set; } = false;
        public double AutoMinLimit { get; set; } = -60; // Для логарифмического
        public double AutoMinLimitLinear { get; set; } = 0; // Для линейного

        // Минимальный диапазон для случая min=max
        public double MinRangeLog { get; set; } = 0.5; 
        public bool IsLogScale { get; set; } = true;
        public bool IsDark{ get; set; } = true;

    }

    


    public class PlotManagerMain
    {
        private readonly object _plotMainLock = new();
        private DispatcherTimer? _avaPlotRefreshTimer;
        private AvaPlot? _avaPlotMain;
        private double? _globalMin = null;
        private double? _globalMax = null;
        private bool _avaPlotMainNeedsRefresh = false;

        private ScottPlot.Plottables.Polygon? _sectorPolygon;
        private ScottPlot.Plottables.PolarAxis? _polarAxisMain;
        private ScottPlot.Plottables.Arrow? _angleArrow;
        private double? _pendingSectorStart = null;
        private double? _pendingSectorEnd = null;
        private bool? _pendingSectorVisible = null;
        private bool _sectorUpdatePending = false;

        private ScaleMode _currentScaleMode = ScaleMode.Auto;
        private ScaleSettings _scaleSettings = new();


        private readonly List<TabViewModel> _activePlotTabs = new();



        /// <summary>
        /// МОДИФИЦИРОВАННЫЙ метод DrawPolarPlot
        /// </summary>
        public void DrawPolarPlot(IEnumerable<TabViewModel> tabs,
            TabViewModel currentTab,
            string? label = null)
        {
            // Существующие проверки...
            if (currentTab.Plot == null || currentTab.Plot.Angles == null ||
                currentTab.Plot.Angles.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] currentTab.Plot data is invalid");
                return;
            }

            if (_polarAxisMain == null || _avaPlotMain == null || _avaPlotMain.Plot == null)
                return;

            var plotData = currentTab.Plot;
            double[] values = _scaleSettings.IsLogScale ? plotData.PowerNormValues : plotData.VoltageNormValues;

            if (values.Length == 0 || plotData.Angles.Length != values.Length)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] arrays length mismatch");
                return;
            }

            lock (_plotMainLock)
            {
                double actualMin, actualMax;
                bool globalRangeChanged = false;

                // НОВАЯ ЛОГИКА: разное поведение для разных режимов масштабирования
                if (_currentScaleMode == ScaleMode.Manual)
                {
                    // Используем ручные значения
                    var (min, max) = GetCurrentRange();
                    actualMin = min;
                    actualMax = max;
                    // В ручном режиме диапазон не меняется, но нужно обновить оси при первом вызове
                    globalRangeChanged = (_globalMin != actualMin || _globalMax != actualMax);
                    _globalMin = _scaleSettings.IsLogScale ? actualMin : 0;
                    _globalMax = _scaleSettings.IsLogScale ? actualMax : 1;
                    
                }
                else
                {
                    // Оригинальная логика для Auto режима
                    globalRangeChanged = UpdateGlobalMinMax(values);
                    actualMin = _scaleSettings.IsLogScale ? _globalMin!.Value : 0;
                    actualMax = _scaleSettings.IsLogScale ? _globalMax!.Value : 1;

                }

                if (globalRangeChanged)
                {
                    // Обновляем оси и координаты всех существующих графиков
                    Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, _scaleSettings.IsLogScale, actualMin, actualMax, _scaleSettings.IsDark);
                    UpdateAllPlotCoordinates(tabs, actualMin, actualMax);
                }

                // Обновляем текущую вкладку
                UpdateCurrentTabPlot(plotData, actualMin, actualMax, label);
                _avaPlotMainNeedsRefresh = true;
            }
        }

        private void UpdateCurrentTabPlot(PlotData plotData, double min, double max, string? label)
        {
            // Initialize segments if not exists
            plotData.PlotSegments ??= new List<PlotSegmentData>();

            // Get values and create segments - optimized for frequent updates
            double[] values = _scaleSettings.IsLogScale ? plotData.PowerNormValues : plotData.VoltageNormValues;
            var segments = CreateSegmentsOptimized(plotData.Angles, values);
            var color = ScottPlot.Color.FromHex(plotData.ColorHex);
            bool first = true;

            // Update existing segments or create new ones
            for (int segIndex = 0; segIndex < segments.Count; segIndex++)
            {
                var (segAngles, segValues) = segments[segIndex];
                if (segAngles.Count <= 1) continue;

                PlotSegmentData segmentData = GetOrCreateSegmentData(plotData, segIndex);

                // Only update arrays if they're different (avoid unnecessary allocations)
                if (NeedsDataUpdate(segmentData, segAngles, segValues))
                {
                    if (segmentData.SegmentAngles?.Length != segAngles.Count)
                    {
                        segmentData.SegmentAngles = new double[segAngles.Count];
                    }
                    if (segmentData.SegmentValues?.Length != segValues.Count)
                    {
                        segmentData.SegmentValues = new double[segValues.Count];
                    }

                    // Копируем данные без создания новых массивов
                    segAngles.CopyTo(segmentData.SegmentAngles, 0);
                    segValues.CopyTo(segmentData.SegmentValues, 0);
                }


                // Update coordinates in the fixed array
                UpdateSegmentCoordinates(segmentData, min, max);

                // Create or update scatter plot
                CreateOrUpdateScatterPlot(segmentData, color, first, segIndex == 0 ? label : null);
                first = false;
            }

            // Remove excess segments
            RemoveExcessSegments(plotData, segments.Count);
        }


        private List<(List<double> segAngles, List<double> segValues)> CreateSegmentsOptimized(double[] angles, double[] values)
        {
            var segments = new List<(List<double>, List<double>)>();
            var currentAngles = new List<double>();
            var currentValues = new List<double>();

            for (int i = 0; i < angles.Length; i++)
            {
                // Check for angle gap (segment break)
                if (i > 0 && Math.Abs(angles[i] - angles[i - 1]) > 10)
                {
                    if (currentAngles.Count > 0)
                    {
                        segments.Add((currentAngles, currentValues));
                        currentAngles = new List<double>();
                        currentValues = new List<double>();
                    }
                }
                currentAngles.Add(angles[i]);
                currentValues.Add(values[i]);
            }

            if (currentAngles.Count > 0)
                segments.Add((currentAngles, currentValues));

            return segments;
        }

        private bool ArraysEqualOptimized(double[] arr1, IReadOnlyList<double> arr2)
        {
            if (arr1.Length != arr2.Count) return false;

            for (int i = 0; i < arr1.Length; i++)
            {
                if (Math.Abs(arr1[i] - arr2[i]) > 1e-10) return false;
            }
            return true;
        }

        private bool NeedsDataUpdate(PlotSegmentData segmentData, List<double> newAngles, List<double> newValues)
        {
            if (segmentData.SegmentAngles?.Length != newAngles.Count ||
                segmentData.SegmentValues?.Length != newValues.Count)
                return true;

            return !ArraysEqualOptimized(segmentData.SegmentAngles, newAngles) ||
                   !ArraysEqualOptimized(segmentData.SegmentValues, newValues);
        }

        private PlotSegmentData GetOrCreateSegmentData(PlotData plotData, int segIndex)
        {
            if (segIndex < plotData.PlotSegments!.Count)
            {
                return plotData.PlotSegments[segIndex];
            }
            else
            {
                var segmentData = new PlotSegmentData();
                plotData.PlotSegments.Add(segmentData);
                return segmentData;
            }
        }

        private void CreateOrUpdateScatterPlot(PlotSegmentData segmentData, ScottPlot.Color color, bool isFirst, string? label)
        {
            if (segmentData.ScatterPlot == null)
            {
                segmentData.ScatterPlot = _avaPlotMain!.Plot.Add.Scatter(segmentData.CoordinatesArray, color: color);
                segmentData.ScatterPlot.LineWidth = 2;
                segmentData.ScatterPlot.MarkerSize = 0;
            }
            else
            {
                if (segmentData.ScatterPlot.Color.ToHex() != color.ToHex())
                {
                    segmentData.ScatterPlot.Color = color;
                }
            }

            // Set legend only for the first segment
            segmentData.ScatterPlot.LegendText = isFirst && !string.IsNullOrEmpty(label) ? label : "";
        }

        private void RemoveExcessSegments(PlotData plotData, int segmentCount)
        {
            while (plotData.PlotSegments!.Count > segmentCount)
            {
                var lastSegment = plotData.PlotSegments[plotData.PlotSegments.Count - 1];
                if (lastSegment.ScatterPlot != null)
                {
                    _avaPlotMain!.Plot.Remove(lastSegment.ScatterPlot);
                    lastSegment.ScatterPlot = null; // Обнуление ссылки
                }

                plotData.PlotSegments.RemoveAt(plotData.PlotSegments.Count - 1);
            }
        }

        private void UpdateSegmentCoordinates(PlotSegmentData segmentData, double min, double max)
        {
            double r_max = Constants.DefaultPlotRadius;
            int pointCount = Math.Min(segmentData.SegmentAngles.Length, segmentData.CoordinatesArray.Length);

            for (int i = 0; i < pointCount; i++)
            {
                // Fix angle orientation - ensure consistent coordinate system
                double mirroredAngle = (360 - segmentData.SegmentAngles[i]) % 360;
                double r;

                if (_scaleSettings.IsLogScale)
                {
                    // For log scale, ensure we have a valid range
                    if (max > min)
                    {
                        // Log scale calculation
                        //double logMin = Math.Log10(min);
                        //double logMax = Math.Log10(max);
                        //double logValue = Math.Log10(Math.Max(segmentData.SegmentValues[i], min)); // Prevent log(0)
                        //r = r_max * (logValue - logMin) / (logMax - logMin);
                        r = (max - min) > 0 ? r_max * (segmentData.SegmentValues[i] - min) / (max - min) : r_max;
                    }
                    else
                    {
                        // Fallback for invalid log range
                        r = (max - min) > 0 ? r_max * (segmentData.SegmentValues[i] - min) / (max - min) : 0;
                    }

                    
                }
                else
                {
                    // Linear scale calculation
                    if (max > 0)
                    {
                        r = r_max * (segmentData.SegmentValues[i] / max);
                    }
                    else
                    {
                        r = 0;
                    }
                }

                // Ensure r is within valid bounds
                r = Math.Max(0, Math.Min(r, r_max));

                segmentData.CoordinatesArray[i] = _polarAxisMain!.GetCoordinates(r, mirroredAngle);
            }

            // Fill remaining array elements with the last valid coordinate
            var lastCoord = pointCount > 0 ? segmentData.CoordinatesArray[pointCount - 1] : new ScottPlot.Coordinates(0, 0);
            for (int i = pointCount; i < segmentData.CoordinatesArray.Length; i++)
            {
                segmentData.CoordinatesArray[i] = lastCoord;
            }

            segmentData.ValidPointCount = pointCount;
        }

        // This is the expensive operation - only called when global min/max changes
        private void UpdateAllPlotCoordinates(IEnumerable<TabViewModel> tabs, double min, double max)
        {
            // Update active tabs cache to avoid repeated enumeration
            _activePlotTabs.Clear();
            foreach (var tab in tabs)
            {
                if (tab.Plot?.IsVisible == true && tab.Plot.PlotSegments != null)
                {
                    _activePlotTabs.Add(tab);
                }
            }

            // Update coordinates for all active plots
            foreach (var tab in _activePlotTabs)
            {
                foreach (var segmentData in tab.Plot!.PlotSegments!)
                {
                    if (segmentData.SegmentAngles?.Length > 0 && segmentData.SegmentValues?.Length > 0)
                    {
                        UpdateSegmentCoordinates(segmentData, min, max);
                    }
                }
            }
        }
        public ScaleMode CurrentScaleMode
        {
            get => _currentScaleMode;
            set
            {
                if (_currentScaleMode != value)
                {
                    _currentScaleMode = value;
                    OnScaleModeChanged?.Invoke(value);
                }
            }
        }

        public ScaleSettings ScaleSettings => _scaleSettings;

        // НОВЫЕ СОБЫТИЯ
        public event Action<ScaleMode>? OnScaleModeChanged;
        public event Action<double, double>? OnScaleRangeChanged;

        // НОВЫЕ ПУБЛИЧНЫЕ МЕТОДЫ

        /// <summary>
        /// Устанавливает режим масштабирования
        /// </summary>
        private void SetScaleMode(ScaleMode mode)
        {
            lock (_plotMainLock)
            {
                if (_currentScaleMode == mode) return;

                // При переходе в Manual режим - сохраняем текущий авто-диапазон как начальные значения
                if (mode == ScaleMode.Manual && _currentScaleMode == ScaleMode.Auto && _scaleSettings.IsLogScale)
                {
                    if (_globalMin.HasValue && _globalMax.HasValue)
                    {
                        _scaleSettings.ManualMin = _globalMin.Value;
                        _scaleSettings.ManualMax = _globalMax.Value;
                    }
                }

                CurrentScaleMode = mode;
            }
        }

        public void UpdateScaleMode(bool isAutoScale)
        {
            ScaleMode mode = isAutoScale ? ScaleMode.Auto : ScaleMode.Manual;
            SetScaleMode(mode);
        }

        /// <summary>
        /// Устанавливает ручной диапазон масштабирования
        /// </summary>
        public bool SetManualRange(int min, int max)
        {
            // Валидация
            if (min >= max) return false;
            //if (isLogScale && (min <= 0 || max <= 0)) return false;

            lock (_plotMainLock)
            {

                _scaleSettings.ManualMin = min;
                _scaleSettings.ManualMax = max;


                // Если режим Manual, сразу применяем изменения
                if (_currentScaleMode == ScaleMode.Manual)
                {
                    ApplyManualRange();
                    OnScaleRangeChanged?.Invoke(min, max);
                }
            }
            return true;
        }

        /// <summary>
        /// Получает текущий эффективный диапазон (авто или ручной)
        /// </summary>
        public (double min, double max) GetCurrentRange()
        {
            lock (_plotMainLock)
            {
                if (_currentScaleMode == ScaleMode.Manual)
                {
                    return (_scaleSettings.ManualMin, _scaleSettings.ManualMax);
                }
                else
                {
                    return (_globalMin ?? 0, _globalMax ?? 0);
                }
            }
        }

        /// <summary>
        /// Применяет ручной диапазон ко всем графикам
        /// </summary>
        public void ApplyManualRange()
        {
            if (_currentScaleMode != ScaleMode.Manual) return;

            lock (_plotMainLock)
            {
                double min, max;

                min = _scaleSettings.IsLogScale ? _scaleSettings.ManualMin : 0;
                max = _scaleSettings.IsLogScale ? _scaleSettings.ManualMax : 1;

                // Принудительно устанавливаем глобальный диапазон
                
                _globalMin = min;
                _globalMax = max;

                // Обновляем оси
                if (_avaPlotMain != null && _polarAxisMain != null)
                {
                    Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, _scaleSettings.IsLogScale, min, max, _scaleSettings.IsDark );
                }

                // Обновляем координаты всех существующих графиков
                UpdateAllExistingPlotCoordinates(min, max);

                _avaPlotMainNeedsRefresh = true;
            }
        }
        /// <summary>
        /// Обновляет координаты всех существующих графиков (кроме текущего в процессе отрисовки)
        /// </summary>
        private void UpdateAllExistingPlotCoordinates(double min, double max)
        {
            foreach (var tab in _activePlotTabs)
            {
                if (tab.Plot?.PlotSegments != null)
                {
                    foreach (var segmentData in tab.Plot.PlotSegments)
                    {
                        if (segmentData.SegmentAngles?.Length > 0 && segmentData.SegmentValues?.Length > 0)
                        {
                            UpdateSegmentCoordinates(segmentData, min, max);
                        }
                    }
                }
            }
        }


        public void SetAutoMinLimit(bool enabled, double limit)
        {
            lock (_plotMainLock)
            {
                _scaleSettings.UseAutoMinLimit = enabled;
                _scaleSettings.AutoMinLimit = limit;
            }
        }

        public void SetScaleMode(bool isLogScale)
        {
            lock (_plotMainLock)
            {
                _scaleSettings.IsLogScale = isLogScale;
            }
        }

        private bool UpdateGlobalMinMax(double[] values)
        {
            lock (_plotMainLock)
            {
                if (_currentScaleMode == ScaleMode.Manual)
                    return false;

                double localMin = values.Min();
                double localMax = values.Max();
                bool changed = false;

                // Применяем ограничение минимума если включено
                if (_scaleSettings.UseAutoMinLimit)
                {
                    // Предполагаем что это для текущего масштаба (нужен параметр isLogScale)
                    // Или можно хранить последний использованный масштаб
                    localMin = Math.Max(localMin, _scaleSettings.AutoMinLimit);
                }

                // Обработка случая min=max
                if (Math.Abs(localMax - localMin) < _scaleSettings.MinRangeLog)
                {
                    localMin -= _scaleSettings.MinRangeLog;

                    // Повторно применяем ограничение минимума после расширения
                    if (_scaleSettings.UseAutoMinLimit)
                    {
                        localMin = Math.Max(localMin, _scaleSettings.AutoMinLimit);
                    }
                }

                if (_globalMax == null || _globalMax < localMax)
                {
                    _globalMax = localMax;
                    changed = true;
                }
                if (_globalMin == null || _globalMin > localMin)
                {
                    _globalMin = localMin;
                    changed = true;
                }

                if (changed)
                {
                    OnScaleRangeChanged?.Invoke(_globalMin.Value, _globalMax.Value);
                }

                return changed;
            }
        }


        // Replacement for DrawAllVisiblePlots - use for scale changes, theme changes, etc.
        public void RefreshAllVisiblePlots(IEnumerable<TabViewModel> tabs)
        {
            if (_avaPlotMain == null || _polarAxisMain == null)
                return;

            lock (_plotMainLock)
            {
                // Clear all existing plots first to avoid coordinate conflicts
                foreach (var tab in tabs)
                {
                    if (tab.Plot?.PlotSegments != null)
                    {
                        foreach (var segmentData in tab.Plot.PlotSegments)
                        {
                            if (segmentData.ScatterPlot != null)
                                _avaPlotMain.Plot.Remove(segmentData.ScatterPlot);
                        }
                        tab.Plot.PlotSegments.Clear();
                    }
                }

                // Reset global range completely
                ResetGlobalRange();

                // Recalculate global min/max with correct scale
                RecalculateGlobalRange(tabs);

                double actualMin = _globalMin ?? 0;
                double actualMax = _globalMax ?? 0;

                if (!_scaleSettings.IsLogScale)
                {
                    actualMin = 0;
                    actualMax = 1;
                }

                // Update axis circles with current range
                Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, _scaleSettings.IsLogScale, actualMin, actualMax, _scaleSettings.IsDark);

                // Recreate all plots with correct coordinates
                foreach (var tab in tabs)
                {
                    if (tab.Plot?.IsVisible == true && tab.Plot.Angles?.Length > 0)
                    {
                        UpdateCurrentTabPlot(tab.Plot, actualMin, actualMax, tab.Header);
                    }
                }
                _avaPlotMainNeedsRefresh = true;
            }
        }


        // Helper method to recalculate global range from all visible plots
        public void RecalculateGlobalRange(IEnumerable<TabViewModel> tabs)
        {
            lock (_plotMainLock)
            {
                ResetGlobalRange();

                foreach (var tab in tabs)
                {
                    if (tab.Plot?.IsVisible == true && tab.Plot.Angles?.Length > 0)
                    {
                        // Use the correct value array based on scale type
                        double[] values = _scaleSettings.IsLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                        if (values.Length > 0)
                        {
                            UpdateGlobalMinMax(values);
                        }
                    }
                }
            }
            _avaPlotMainNeedsRefresh = true;
        }


        public void ClearCurrentTabPlot(TabViewModel tab, AvaPlot? plot = null)
        {
            plot ??= _avaPlotMain;
            if (plot == null || tab?.Plot == null)
                return;

            lock (_plotMainLock)
            {
                try
                {
                    if (tab.Plot.PlotSegments != null)
                    {
                        foreach (var segmentData in tab.Plot.PlotSegments)
                        {
                            if (segmentData.ScatterPlot != null)
                                plot.Plot.Remove(segmentData.ScatterPlot);
                        }
                        tab.Plot.PlotSegments.Clear();
                    }

                    //tab.DataScatters?.Clear();
                    _avaPlotMainNeedsRefresh = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClearCurrentTabPlot] Exception: {ex}");
                }
            }
        }

        // Method to reset global range when starting new acquisition series
        public void ResetGlobalRange()
        {
            lock (_plotMainLock)
            {
                _globalMin = null;
                _globalMax = null;
            }
        }


        public void ResetPlotAxes()
        {
            lock (_plotMainLock)
            {
                if (_avaPlotMain != null)
                    _avaPlotMain.Plot.Axes.AutoScale();
                _avaPlotMainNeedsRefresh = true;
            }
        }

       
        public void CreateOrUpdateSectorPolygon(AvaPlot? plot, double start, double end, bool isVisible)
        {
            if (plot == null)
                return;
            lock (_plotMainLock)
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
            if (plot?.Plot == null) return;
            lock (_plotMainLock)
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


        public void UpdatePolarAxisCircles(AvaPlot plot, bool isLog, double min, double max, bool isDark)
        {
            if (plot == null || plot?.Plot == null || _polarAxisMain == null)
                return;
            lock (_plotMainLock)
            {
                Plots.AutoUpdatePolarAxisCircles(plot, _polarAxisMain, isLog, min, max, isDark);
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
                    _scaleSettings.IsDark = isDark;
                    Plots.UpdatePolarAxisTheme(_polarAxisMain, isDark);
                    Plots.AddCustomSpokeLines(avaPlotMain, _polarAxisMain, isDark);
                    Plots.SetScottPlotTheme(isDark, false, avaPlotMain);
                }

            }
            _avaPlotMainNeedsRefresh = true;
        }

        public void InitializePlotMain(AvaPlot plot, bool isDark)
        {
            _avaPlotMain = plot;
            _polarAxisMain = Plots.Initialize(plot, isDark) ?? throw new InvalidOperationException("Failed to initialize main polar axis");
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

                if (_avaPlotMainNeedsRefresh && _avaPlotMain != null && _avaPlotMain?.Plot != null)
                {
                    lock (_plotMainLock)
                    {
                        _avaPlotMain.Refresh();
                        _avaPlotMainNeedsRefresh = false;
                    }
                }
            };
            _avaPlotRefreshTimer.Start();
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
                    plot.Plot.MoveToFront(_angleArrow);
                }
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

        public void SetLegendVisibility(bool isVisible)
        {
            lock (_plotMainLock)
            {
                if (_avaPlotMain != null)
                {
                    if (isVisible)
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

