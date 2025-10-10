using AntennaAV.Helpers;
using Avalonia.Threading;
using ScottPlot.Avalonia;
using System;
using System.Linq;
namespace AntennaAV.Services
{


    public class PlotManagerSmall
    {

        private readonly object _plotTxLock = new();
        private readonly object _plotRxLock = new();
        private DispatcherTimer? _avaPlotRefreshTimer;
        private AvaPlot? _avaPlotTx;
        private AvaPlot? _avaPlotRx;
        private bool _avaPlotTxNeedsRefresh = false;
        private bool _avaPlotTxNeedsAutoScale = false;
        private bool _avaPlotRxNeedsRefresh = false;
        private bool _avaPlotRxNeedsAutoScale = false;

        private ScottPlot.Plottables.PolarAxis? _polarAxisTx;
        private ScottPlot.Plottables.PolarAxis? _polarAxisRx;

        private ScottPlot.Plottables.Marker? _hoverMarkerTx;
        private ScottPlot.Plottables.Marker? _hoverMarkerRx;
        private ScottPlot.Plottables.Marker? _transmitterMarker;
        private ScottPlot.Plottables.Marker? _receiverMarker;

        public void ResetPlotAxes()
        {
            _avaPlotTxNeedsAutoScale = true;
            _avaPlotRxNeedsAutoScale = true;
            _avaPlotTxNeedsRefresh = true;
            _avaPlotRxNeedsRefresh = true;
        }

        public void DrawTransmitterAnglePoint(AvaPlot? plot, double angleDeg)
        {
            
            if (plot == null || plot?.Plot == null)
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
        private void DrawAnglePoint(
            AvaPlot? plot,
            double angleDeg,
            object plotLock,
            ref ScottPlot.Plottables.Marker? marker,
            ref bool needsRefreshFlag,
            double rotation = 0)
        {
            if (plot == null || plot?.Plot == null)
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
            if (plot == null || polarAxis == null || plot.Plot == null)
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
                    if (hoverMarker != null && plot.Plot != null && plot.Plot.GetPlottables().Contains(hoverMarker))
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



        public void InitializeSmallPlots(AvaPlot txPlot, AvaPlot rxPlot, bool isDark)
        {
            InitializeTxPlot(txPlot, isDark);
            InitializeRxPlot(rxPlot, isDark);
            InitializeRefreshTimer();
        }

        private void InitializeRefreshTimer()
        {
            _avaPlotRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.PlotTimerUpdateIntervalMs) };
            _avaPlotRefreshTimer.Tick += (s, e) =>
            {

                if (_avaPlotTxNeedsRefresh && _avaPlotTx != null && _avaPlotTx?.Plot != null)
                {
                    lock (_plotTxLock)
                    {  
                        if(_avaPlotRxNeedsAutoScale)
                        {
                            _avaPlotTx.Plot.Axes.AutoScale();
                            _avaPlotTxNeedsAutoScale = false;
                        }
                        _avaPlotTx.Refresh();
                        _avaPlotTxNeedsRefresh = false;
                    }
                }
                if (_avaPlotRxNeedsRefresh && _avaPlotRx != null && _avaPlotRx?.Plot != null)
                {
                    lock (_plotRxLock)
                    {
                        if (_avaPlotRxNeedsAutoScale)
                        {
                            _avaPlotRx.Plot.Axes.AutoScale();
                            _avaPlotRxNeedsAutoScale = false;
                        }
                        _avaPlotRx.Refresh();
                        _avaPlotRxNeedsRefresh = false;
                    }
                }
            };
            _avaPlotRefreshTimer.Start();
        }

        private void InitializeTxPlot(AvaPlot plot, bool isDark)
        {
            _avaPlotTx = plot;
            _polarAxisTx = Plots.InitializeSmall(plot, isDark) ?? throw new InvalidOperationException("Failed to initialize Tx polar axis");
            ApplyThemeToPlotSmall(
                isDark,
                plot,
                _plotTxLock,
                _polarAxisTx,
                ref _avaPlotTxNeedsRefresh
            );
        }

        private void InitializeRxPlot(AvaPlot plot, bool isDark)
        {
            _avaPlotRx = plot;
            _polarAxisRx = Plots.InitializeSmall(plot, isDark, 90) ?? throw new InvalidOperationException("Failed to initialize Rx polar axis");
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
            MoveTxMarkerToFront();
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
            MoveRxMarkerToFront();
        }
        private void MoveRxMarkerToFront()
        {
            if(_receiverMarker != null && _avaPlotRx != null)
            {
                lock(_plotRxLock)
                {
                    _avaPlotRx.Plot.MoveToFront(_receiverMarker);
                }
            }
        }
        private void MoveTxMarkerToFront()
        {
            if (_transmitterMarker != null && _avaPlotTx != null)
            {
                lock (_plotTxLock)
                {
                    _avaPlotTx.Plot.MoveToFront(_transmitterMarker);
                }
            }
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
    }
}

