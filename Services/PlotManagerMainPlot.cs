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

    public class PlotSegmentData
    {
        public double[] SegmentAngles { get; set; } = Array.Empty<double>(); // Angles for this segment
        public double[] SegmentValues { get; set; } = Array.Empty<double>(); // Values for this segment
        public ScottPlot.Coordinates[] CoordinatesArray { get; set; } // Fixed-length array for coordinates
        public Scatter? ScatterPlot { get; set; }
        public Scatter? MarkerScatterPlot { get; set; }  // Для маркеров
        public double[] MarkerXs { get; set; } = Array.Empty<double>();  // Новое: фиксированный массив X для маркеров
        public double[] MarkerYs { get; set; } = Array.Empty<double>();  // Новое: фиксированный массив Y для маркеров
        public int ValidPointCount { get; set; } // How many points are actually used in the array

        public PlotSegmentData(int maxCapacity = 3600, int maxMarkers = 100)  // Добавьте параметр для маркеров
        {
            CoordinatesArray = new ScottPlot.Coordinates[maxCapacity];
            MarkerXs = new double[maxMarkers];  // Фиксированная длина, e.g. 100
            MarkerYs = new double[maxMarkers];
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

        public bool UseAutoMinLimit { get; set; } = true;
        public double AutoMinLimit { get; set; } = -60; // Для логарифмического

        // Минимальный диапазон для случая min=max
        public double MinRangeLog { get; set; } = 0.5;
        public bool IsLogScale { get; set; } = true;
        public bool IsDark { get; set; } = true;

        public int LineWidth { get; set; } = 2;
        public int MarkerSize { get; set; } = 7;

    }

    public class PlotManagerMain
    {
        private readonly object _plotMainLock = new();
        private DispatcherTimer? _avaPlotRefreshTimer;
        private AvaPlot? _avaPlotMain;
        private double? _globalMin = null;
        private double? _globalMax = null;
        private bool _avaPlotMainNeedsRefresh = false;
        private bool _avaPlotMainNeedsAutoscale = false;
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

        private const int MarkerStep = 50; // every 50 points
        private const int DefaultMaxMarkers = 100;
        private int _currentMarkerSize = 0;

        private static readonly ScottPlot.MarkerShape[] _perTabMarkerShapes = new[]
{
            ScottPlot.MarkerShape.FilledCircle,
            ScottPlot.MarkerShape.FilledSquare,
            ScottPlot.MarkerShape.FilledTriangleUp,
            ScottPlot.MarkerShape.FilledTriangleDown,
            ScottPlot.MarkerShape.FilledDiamond,
            ScottPlot.MarkerShape.OpenCircle,
            ScottPlot.MarkerShape.OpenSquare,
            ScottPlot.MarkerShape.OpenTriangleUp,
            ScottPlot.MarkerShape.Eks,
            ScottPlot.MarkerShape.Cross,
            ScottPlot.MarkerShape.Asterisk,
            ScottPlot.MarkerShape.HashTag
        };
        private int _nextPerTabMarkerShapeIndex = 0;
        private readonly Dictionary<PlotData, ScottPlot.MarkerShape> _assignedPerTabMarkerShapes = new();

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

                if (_currentScaleMode == ScaleMode.Manual)
                {
                    var (min, max) = GetCurrentRange();
                    actualMin = min;
                    actualMax = max;
                    globalRangeChanged = (_globalMin != actualMin || _globalMax != actualMax);
                    _globalMin = _scaleSettings.IsLogScale ? actualMin : 0;
                    _globalMax = _scaleSettings.IsLogScale ? actualMax : 1;

                }
                else
                {
                    globalRangeChanged = UpdateGlobalMinMax(values);
                    actualMin = _scaleSettings.IsLogScale ? _globalMin!.Value : 0;
                    actualMax = _scaleSettings.IsLogScale ? _globalMax!.Value : 1;

                }

                if (globalRangeChanged)
                {
                    Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, _scaleSettings.IsLogScale, actualMin, actualMax, _scaleSettings.IsDark);
                    UpdateAllPlotCoordinates(tabs, actualMin, actualMax);
                }

                UpdateCurrentTabPlot(plotData, actualMin, actualMax, label);
                _avaPlotMainNeedsRefresh = true;
            }
        }

        /// <summary>
        /// Обновляет видимость графика без полной перерисовки
        /// </summary>
        public void UpdatePlotVisibility(TabViewModel tab, bool isVisible)
        {
            if (tab?.Plot == null)
                return;

            lock (_plotMainLock)
            {
                tab.Plot.IsVisible = isVisible;

                if (tab.Plot.PlotSegments != null)
                {
                    foreach (var segmentData in tab.Plot.PlotSegments)
                    {
                        if (segmentData.ScatterPlot != null)
                        {
                            segmentData.ScatterPlot.IsVisible = isVisible;
                        }
                        if (segmentData.MarkerScatterPlot != null)
                        {
                            segmentData.MarkerScatterPlot.IsVisible = isVisible;
                        }
                    }
                }

                _avaPlotMainNeedsRefresh = true;
            }
        }

        /// <summary>
        /// Скрывает график и пересчитывает диапазон
        /// </summary>
        public void HidePlotAndRecalculateRange(TabViewModel tab, IEnumerable<TabViewModel> allTabs)
        {
            if (tab?.Plot == null)
                return;

            bool hasData = tab.Plot.Angles.Length > 0 && tab.Plot.IsVisible;

            if (!hasData)
                return;

            lock (_plotMainLock)
            {
                // Скрываем график
                ClearCurrentTabPlot(tab);
                tab.Plot.IsVisible = false;

                // Пересчитываем диапазон только для видимых графиков
                var visibleTabs = allTabs.Where(t => t.Plot?.IsVisible == true);

                if (visibleTabs.Any() && hasData)
                {
                    RecalculateGlobalRange(visibleTabs);

                    // Обновляем координаты оставшихся видимых графиков
                    var (min, max) = GetCurrentRange();
                    double actualMin = _scaleSettings.IsLogScale ? min : 0;
                    double actualMax = _scaleSettings.IsLogScale ? max : 1;

                    if (_avaPlotMain != null && _polarAxisMain != null)
                    {
                        Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, _scaleSettings.IsLogScale, actualMin, actualMax, _scaleSettings.IsDark);
                    }

                    UpdateAllPlotCoordinates(visibleTabs, actualMin, actualMax);
                }
                else
                {
                    // Если нет видимых графиков, сбрасываем диапазон
                    ResetGlobalRange();
                }

                _avaPlotMainNeedsRefresh = true;
            }
        }

        private void UpdateCurrentTabPlot(PlotData plotData, double min, double max, string? label)
        {
            plotData.PlotSegments ??= new List<PlotSegmentData>();

            double[] values = _scaleSettings.IsLogScale ? plotData.PowerNormValues : plotData.VoltageNormValues;
            var segments = CreateSegmentsOptimized(plotData.Angles, values);
            var color = ScottPlot.Color.FromHex(plotData.ColorHex);
            bool first = true;

            // assign a per-tab marker shape once (do not overwrite if already assigned)
            if (!_assignedPerTabMarkerShapes.ContainsKey(plotData))
            {
                var shape = _perTabMarkerShapes[_nextPerTabMarkerShapeIndex % _perTabMarkerShapes.Length];
                _assignedPerTabMarkerShapes[plotData] = shape;
                _nextPerTabMarkerShapeIndex++;
                plotData.MarkerShape = shape;
            }

            for (int segIndex = 0; segIndex < segments.Count; segIndex++)
            {
                var (segAngles, segValues) = segments[segIndex];
                if (segAngles.Count <= 1) continue;

                PlotSegmentData segmentData = GetOrCreateSegmentData(plotData, segIndex);

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

                    segAngles.CopyTo(segmentData.SegmentAngles, 0);
                    segValues.CopyTo(segmentData.SegmentValues, 0);
                }


                UpdateSegmentCoordinates(segmentData, min, max);

                CreateOrUpdateScatterPlot(segmentData, color, first, segIndex == 0 ? label : null);

                // Now update markers for this segment (creates or updates marker Scatter)
                UpdateSegmentMarkers(segmentData, plotData, color);

                first = false;
            }

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
                var segmentData = new PlotSegmentData(3600, DefaultMaxMarkers);
                plotData.PlotSegments.Add(segmentData);
                return segmentData;
            }
        }


        private void CreateOrUpdateScatterPlot(PlotSegmentData segmentData, ScottPlot.Color color, bool isFirst, string? label)
        {
            if (segmentData.ScatterPlot == null)
            {
                segmentData.ScatterPlot = _avaPlotMain!.Plot.Add.Scatter(segmentData.CoordinatesArray, color: color);
                segmentData.ScatterPlot.LineWidth = _scaleSettings.LineWidth;
                segmentData.ScatterPlot.MarkerSize = 0;
            }
            else
            {
                if (segmentData.ScatterPlot.Color.ToHex() != color.ToHex())
                {
                    segmentData.ScatterPlot.Color = color;
                }
            }
            segmentData.ScatterPlot.LegendText = isFirst && !string.IsNullOrEmpty(label) ? label : "";
        }

        // Marker-related helpers

        private void UpdateSegmentMarkers(PlotSegmentData segmentData, PlotData plotData, ScottPlot.Color color)
        {
            // requires _avaPlotMain and polar axis to be initialized where needed
            if (_avaPlotMain == null || _polarAxisMain == null)
                return;

            // Use effective marker size: per-plot if set, otherwise manager's current value
            int effectiveMarkerSize = plotData.MarkerSize > 0 ? plotData.MarkerSize : _currentMarkerSize;

            // If marker size disabled or not enough points, remove existing marker scatter
            if (effectiveMarkerSize <= 0 || segmentData.ValidPointCount <= 1)
            {
                RemoveMarkerScatterPlot(segmentData);
                return;
            }

            // Ensure backing arrays are large enough (reserve enough entries)
            int expectedByStep = (segmentData.ValidPointCount + MarkerStep - 1) / MarkerStep;
            int ensureLength = Math.Max(DefaultMaxMarkers, expectedByStep);
            if (segmentData.MarkerXs.Length < ensureLength || segmentData.MarkerYs.Length < ensureLength)
            {
                segmentData.MarkerXs = new double[ensureLength];
                segmentData.MarkerYs = new double[ensureLength];
            }

            // Clear arrays
            Array.Clear(segmentData.MarkerXs, 0, segmentData.MarkerXs.Length);
            Array.Clear(segmentData.MarkerYs, 0, segmentData.MarkerYs.Length);

            // Choose target spacing in pixels and convert to world units using plotted radius -> pixel scale
            const double targetSpacingPx = 100.0;

            // Compute approximate pixels-per-world-unit using the plot control size and default polar radius
            double plotRadiusPix = 0.6 * Math.Min(_avaPlotMain.Bounds.Width, _avaPlotMain.Bounds.Height) / 2.0;
            double pixelsPerWorld = plotRadiusPix > 0 ? (plotRadiusPix / Constants.DefaultPlotRadius) : 1.0;
            double targetSpacingWorld = targetSpacingPx / pixelsPerWorld;

            int markerIndex = 0;
            double acc = 0.0;
            int lastChosenIndex = -1;

            // Use polar arc-length sampling: s ≈ r * Δθ (Δθ in radians)
            for (int i = 0; i < segmentData.ValidPointCount; i++)
            {
                var coord = segmentData.CoordinatesArray[i];
                double r = Math.Sqrt(coord.X * coord.X + coord.Y * coord.Y);

                // skip near-center points to avoid crowding
                if (r <= 1e-6)
                    continue;

                if (lastChosenIndex < 0)
                {
                    // always choose the first non-center point
                    segmentData.MarkerXs[markerIndex] = coord.X;
                    segmentData.MarkerYs[markerIndex] = coord.Y;
                    markerIndex++;
                    lastChosenIndex = i;
                    acc = 0.0;
                    if (markerIndex >= segmentData.MarkerXs.Length) break;
                    continue;
                }

                // angle values come from the original data (degrees)
                // compute smallest angular difference
                double degA = segmentData.SegmentAngles[lastChosenIndex];
                double degB = segmentData.SegmentAngles[i];
                double deltaDeg = Math.Abs(degB - degA);
                if (deltaDeg > 180.0) deltaDeg = 360.0 - deltaDeg;
                double deltaRad = deltaDeg * Math.PI / 180.0;

                // approximate arc length at current radius
                double s = r * deltaRad;
                acc += s;

                if (acc >= targetSpacingWorld)
                {
                    // commit marker (skip if projected extremely close to center in pixels)
                    double px = coord.X * pixelsPerWorld;
                    double py = coord.Y * pixelsPerWorld;
                    double distToCenterPx = Math.Sqrt(px * px + py * py);
                    if (distToCenterPx > 2.0) // 2 px tolerance
                    {
                        segmentData.MarkerXs[markerIndex] = coord.X;
                        segmentData.MarkerYs[markerIndex] = coord.Y;
                        markerIndex++;
                        lastChosenIndex = i;
                        acc = 0.0;
                        if (markerIndex >= segmentData.MarkerXs.Length) break;
                    }
                    else
                    {
                        // don't reset lastChosenIndex so we keep accumulating
                        acc = 0.0;
                    }
                }
            }

            // Fallback: if none selected, use coarse index-based sampling
            if (markerIndex == 0)
            {
                for (int i = 0; i < segmentData.ValidPointCount; i += MarkerStep)
                {
                    if (markerIndex >= segmentData.MarkerXs.Length) break;
                    var coord = segmentData.CoordinatesArray[i];
                    segmentData.MarkerXs[markerIndex] = coord.X;
                    segmentData.MarkerYs[markerIndex] = coord.Y;
                    markerIndex++;
                }
            }

            CreateOrUpdateMarkerScatterPlot(segmentData, color, markerIndex, plotData, effectiveMarkerSize);
        }

        private void CreateOrUpdateMarkerScatterPlot(PlotSegmentData segmentData, ScottPlot.Color color, int markerCount, PlotData plotData, int effectiveMarkerSize)
        {
            if (_avaPlotMain == null)
                return;

            if (markerCount == 0)
            {
                RemoveMarkerScatterPlot(segmentData);
                return;
            }

            if (segmentData.MarkerScatterPlot == null)
            {
                // create scatter with Xs and Ys arrays
                segmentData.MarkerScatterPlot = _avaPlotMain.Plot.Add.Scatter(segmentData.MarkerXs, segmentData.MarkerYs, color: color);
                segmentData.MarkerScatterPlot.LineWidth = 0;
                segmentData.MarkerScatterPlot.MarkerSize = effectiveMarkerSize;

                segmentData.MarkerScatterPlot.MarkerShape = plotData.MarkerShape;

                segmentData.MarkerScatterPlot.LegendText = ""; // do not add to legend



                // Important: set MaxRenderIndex so only the first markerCount points render

                segmentData.MarkerScatterPlot.MaxRenderIndex = markerCount - 1;



                // Respect plot visibility

                segmentData.MarkerScatterPlot.IsVisible = plotData.IsVisible;
            }
            else
            {

                // update arrays by reference (Scatter keeps reference to arrays)

                // set MaxRenderIndex so only first markerCount points render

                segmentData.MarkerScatterPlot.MaxRenderIndex = markerCount - 1;
                if (segmentData.MarkerScatterPlot.Color.ToHex() != color.ToHex())
                    segmentData.MarkerScatterPlot.Color = color;
                if (segmentData.MarkerScatterPlot.MarkerSize != effectiveMarkerSize)
                    segmentData.MarkerScatterPlot.MarkerSize = effectiveMarkerSize;
                if (segmentData.MarkerScatterPlot.MarkerShape != plotData.MarkerShape)
                    segmentData.MarkerScatterPlot.MarkerShape = plotData.MarkerShape;
                // ensure MarkerScatterPlot visibility tracks plotData.IsVisible

                segmentData.MarkerScatterPlot.IsVisible = plotData.IsVisible;
            }
        }

        private void RemoveMarkerScatterPlot(PlotSegmentData segmentData)
        {
            if (_avaPlotMain == null)
            {
                segmentData.MarkerScatterPlot = null;
                return;
            }

            if (segmentData.MarkerScatterPlot != null)
            {
                try
                {
                    _avaPlotMain.Plot.Remove(segmentData.MarkerScatterPlot);
                }
                catch
                {
                    // ignore removal exceptions
                }
                segmentData.MarkerScatterPlot = null;
            }
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
                if (lastSegment.MarkerScatterPlot != null)  // Новое: удаляем маркеры
                {
                    _avaPlotMain!.Plot.Remove(lastSegment.MarkerScatterPlot);
                    lastSegment.MarkerScatterPlot = null;
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
                double mirroredAngle = (360 - segmentData.SegmentAngles[i]) % 360;
                double r;

                if (_scaleSettings.IsLogScale)
                {
                    if (max > min)
                    {
                        r = (max - min) > 0 ? r_max * (segmentData.SegmentValues[i] - min) / (max - min) : r_max;
                    }
                    else
                    {
                        r = (max - min) > 0 ? r_max * (segmentData.SegmentValues[i] - min) / (max - min) : 0;
                    }
                }
                else
                {
                    if (max > 0)
                    {
                        r = r_max * (segmentData.SegmentValues[i] / max);
                    }
                    else
                    {
                        r = 0;
                    }
                }
                r = Math.Max(0, Math.Min(r, r_max));
                segmentData.CoordinatesArray[i] = _polarAxisMain!.GetCoordinates(r, mirroredAngle);
            }

            var lastCoord = pointCount > 0 ? segmentData.CoordinatesArray[pointCount - 1] : new ScottPlot.Coordinates(0, 0);
            for (int i = pointCount; i < segmentData.CoordinatesArray.Length; i++)
            {
                segmentData.CoordinatesArray[i] = lastCoord;
            }

            segmentData.ValidPointCount = pointCount;
        }

        private void UpdateAllPlotCoordinates(IEnumerable<TabViewModel> tabs, double min, double max)
        {
            _activePlotTabs.Clear();
            foreach (var tab in tabs)
            {
                if (tab.Plot?.IsVisible == true && tab.Plot.PlotSegments != null)
                {
                    _activePlotTabs.Add(tab);
                }
            }
            foreach (var tab in _activePlotTabs)
            {
                var color = ScottPlot.Color.FromHex(tab.Plot!.ColorHex);
                foreach (var segmentData in tab.Plot!.PlotSegments!)
                {
                    if (segmentData.SegmentAngles?.Length > 0 && segmentData.SegmentValues?.Length > 0)
                    {
                        UpdateSegmentCoordinates(segmentData, min, max);
                        // update markers so they follow updated coordinates
                        UpdateSegmentMarkers(segmentData, tab.Plot, color);
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
                }
            }
        }

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

        /// Устанавливает ручной диапазон масштабирования
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
                }
            }
            return true;
        }

        /// Получает текущий эффективный диапазон (авто или ручной)
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

        /// Применяет ручной диапазон ко всем графикам
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
                    Plots.AutoUpdatePolarAxisCircles(_avaPlotMain, _polarAxisMain, _scaleSettings.IsLogScale, min, max, _scaleSettings.IsDark);
                }

                // Обновляем координаты всех существующих графиков
                UpdateAllExistingPlotCoordinates(min, max);

                _avaPlotMainNeedsRefresh = true;
            }
        }

        /// Обновляет координаты всех существующих графиков (кроме текущего в процессе отрисовки)
        private void UpdateAllExistingPlotCoordinates(double min, double max)
        {
            foreach (var tab in _activePlotTabs)
            {
                if (tab.Plot?.PlotSegments != null)
                {
                    var color = ScottPlot.Color.FromHex(tab.Plot.ColorHex);
                    foreach (var segmentData in tab.Plot.PlotSegments)
                    {
                        if (segmentData.SegmentAngles?.Length > 0 && segmentData.SegmentValues?.Length > 0)
                        {
                            UpdateSegmentCoordinates(segmentData, min, max);
                            UpdateSegmentMarkers(segmentData, tab.Plot, color);
                        }
                    }
                }
            }
        }

        public void SetMarkerSize(IEnumerable<TabViewModel> tabs, int markerSize)
        {
            if (_avaPlotMain == null || _polarAxisMain == null)
                return;

            lock (_plotMainLock)
            {
                // Remember global marker size so new tabs inherit the setting
                _currentMarkerSize = markerSize;

                foreach (var tab in tabs)
                {
                    if (tab?.Plot == null)
                        continue;

                    tab.Plot.MarkerSize = markerSize;

                    if (tab.Plot.PlotSegments != null)
                    {
                        var color = ScottPlot.Color.FromHex(tab.Plot.ColorHex);
                        foreach (var segmentData in tab.Plot.PlotSegments)
                        {
                            // If markers are being disabled, remove existing marker scatter immediately
                            if (markerSize <= 0)
                            {
                                if (segmentData.MarkerScatterPlot != null)
                                {
                                    try { _avaPlotMain.Plot.Remove(segmentData.MarkerScatterPlot); } catch { }
                                    segmentData.MarkerScatterPlot = null;
                                }
                                continue;
                            }

                            if (segmentData.MarkerScatterPlot != null)
                            {
                                // Update existing marker scatter properties
                                segmentData.MarkerScatterPlot.MarkerSize = markerSize;
                                segmentData.MarkerScatterPlot.IsVisible = tab.Plot.IsVisible;
                                segmentData.MarkerScatterPlot.MarkerShape = tab.Plot.MarkerShape;
                                if (segmentData.MarkerScatterPlot.Color.ToHex() != color.ToHex())
                                    segmentData.MarkerScatterPlot.Color = color;

                                int expectedCount = (segmentData.ValidPointCount + MarkerStep - 1) / MarkerStep;
                                segmentData.MarkerScatterPlot.MaxRenderIndex = Math.Max(0, expectedCount - 1);
                            }
                            else
                            {
                                // Create markers on-demand for segments with enough points
                                if (markerSize > 0 && segmentData.ValidPointCount > MarkerStep)
                                {
                                    UpdateSegmentMarkers(segmentData, tab.Plot, color);
                                }
                            }
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

        public void SetAutoscaleMinValue(double limit)
        {
            lock (_plotMainLock)
            {
                _scaleSettings.MinRangeLog = limit;
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

                return changed;
            }
        }

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
                            if (segmentData.MarkerScatterPlot != null)  // Новое: удаляем маркеры
                                _avaPlotMain.Plot.Remove(segmentData.MarkerScatterPlot);
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

                        // Ensure new plot inherits current markers setting if its Plot.MarkerSize is zero

                        if (tab.Plot.MarkerSize <= 0 && _currentMarkerSize > 0)
                            tab.Plot.MarkerSize = _currentMarkerSize;

                        UpdateCurrentTabPlot(tab.Plot, actualMin, actualMax, tab.Header);
                    }

                }
                _avaPlotMainNeedsRefresh = true;
            }
        }

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
                        foreach (var segmentData in tab.Plot.PlotSegments)
                        {
                            if (segmentData.MarkerScatterPlot != null)
                                plot.Plot.Remove(segmentData.MarkerScatterPlot);
                        }
                        tab.Plot.PlotSegments.Clear();
                    }

                    _avaPlotMainNeedsRefresh = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClearCurrentTabPlot] Exception: {ex}");
                }
            }
        }


        public void ResetGlobalRange()
        {
            lock (_plotMainLock)
            {
                _globalMin = null;
                _globalMax = null;
            }
        }

        public void SetLineWidth(int lineWidth)
        {
            lock (_plotMainLock)
            {
                _scaleSettings.LineWidth = lineWidth;
            }
        }

        public void IncrementLineWidth(int increment)
        {
            lock (_plotMainLock)
            {
                _scaleSettings.LineWidth = _scaleSettings.LineWidth + increment;
                if(_scaleSettings.LineWidth < 1)
                    _scaleSettings.LineWidth = 1;
            }
        }

        public void ResetPlotAxes()
        {
            lock (_plotMainLock)
            {
                _avaPlotMainNeedsAutoscale = true;
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
                    Plots.SetScottPlotTheme(isDark, true, avaPlotMain);
                }

            }
            _avaPlotMainNeedsRefresh = true;
        }

        public void InitializePlotMain(AvaPlot plot, bool isDark)
        {
            _avaPlotMain = plot;
            _polarAxisMain = Plots.Initialize(plot, isDark) ?? throw new InvalidOperationException("Failed to initialize main polar axis");
            ApplyThemeToMainPlot(isDark, plot);
            UpdatePolarAxisCircles(plot, true, -50.0, 0, isDark);
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
                if (_avaPlotMainNeedsAutoscale && _avaPlotMain != null && _avaPlotMain?.Plot != null)
                {
                    _avaPlotMain.Plot.Axes.AutoScale();
                    _avaPlotMainNeedsAutoscale = false;
                    _avaPlotMainNeedsRefresh = true;

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

        public void SetLegendFontSize(int fontSize)
        {
            lock (_plotMainLock)
            {
                if (_avaPlotMain != null)
                {
                    _avaPlotMain.Plot.Legend.FontSize = fontSize;
                }
            }
        }

        public void SetLegendVisibility(bool isVisible)
        {
            lock (_plotMainLock)
            {
                if (_avaPlotMain != null)
                {
                    if (isVisible)
                    {
                        _avaPlotMain.Plot.Legend.FontSize = 12;
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
            if (_avaPlotMain == null || _polarAxisMain == null)
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


                    for (int i = 0; i < _polarAxisMain.Spokes.Count; i++)
                    {
                        _polarAxisMain.Spokes[i].LabelStyle.FontSize = 24;
                    }

                    for (int i = 0; i < _polarAxisMain.Circles.Count; i++)
                    {
                        _polarAxisMain.Circles[i].LabelStyle.FontSize = 24;
                    }

                    SetLegendFontSize(28);

                    // Сохраняем PNG
                    _avaPlotMain.Plot.SavePng(filePath, 2000, 2000);


                    // Возвращаем всё обратно
                    if (_angleArrow != null) _angleArrow.IsVisible = arrowVisible;
                    if (_sectorPolygon != null) _sectorPolygon.IsVisible = sectorVisible;
                    ApplyThemeToMainPlot(isDark, _avaPlotMain);
                    for (int i = 0; i < _polarAxisMain.Spokes.Count; i++)
                    {
                        _polarAxisMain.Spokes[i].LabelStyle.FontSize = 12;
                    }
                    for (int i = 0; i < _polarAxisMain.Circles.Count; i++)
                    {
                        _polarAxisMain.Circles[i].LabelStyle.FontSize = 12;
                    }
                    SetLegendFontSize(12);
                    _avaPlotMain.Refresh();
                    ResetPlotAxes();
                }
            });
        }
    }
}


// Add per-plot marker shape support as a small partial extension.
// PlotData is partial in ViewModels; placing this partial in the same namespace ensures it merges.
namespace AntennaAV.ViewModels
{
    public partial class PlotData
    {
        // Default marker shape per-plot. Can be changed per PlotData instance.
        public ScottPlot.MarkerShape MarkerShape { get; set; } = ScottPlot.MarkerShape.FilledCircle;
    }
}