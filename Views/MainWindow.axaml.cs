using AntennaAV.Services;
using AntennaAV.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Colormaps;
using ScottPlot.Plottables;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Markup;

namespace AntennaAV.Views
{
    public partial class MainWindow : Window
    {
        private ScottPlot.Plottables.PolarAxis? _polarAxis;
        private ScottPlot.Plottables.Polygon? _sectorPolygon;
        private ScottPlot.Plottables.Arrow? _angleArrow;
        private List<Scatter> _dataScatters = new();

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
            _polarAxis = Plots.Initialize(AvaPlot1);
            NumericUpDownSectorSize.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            NumericUpDownSectorCenter.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm)
                {
                    vm.OnBuildRadarPlot += (angles, values) =>
                    {
                        // Сохраняем лимиты
                        var limits = AvaPlot1.Plot.Axes.GetLimits();
                        // Разбиваем на сегменты по разрывам углов
                        List<List<Coordinates>> segments = new();
                        List<Coordinates> current = new();
                        for (int i = 0; i < angles.Length; i++)
                        {
                            double mirroredAngle = (180 - angles[i]) % 360; // если нужно отзеркалить
                            var pt = _polarAxis.GetCoordinates(values[i], mirroredAngle);
                            if (i > 0 && Math.Abs(angles[i] - angles[i - 1]) > 1.0)
                            {
                                if (current.Count > 0)
                                    segments.Add(current);
                                current = new List<Coordinates>();
                            }
                            current.Add(pt);
                        }
                        if (current.Count > 0)
                            segments.Add(current);

                        // Удаляем все старые графики
                        foreach (var scatter in _dataScatters)
                            AvaPlot1.Plot.Remove(scatter);
                        _dataScatters.Clear();

                        // Рисуем каждый сегмент отдельно
                        foreach (var seg in segments)
                        {
                            if (seg.Count > 1)
                            {
                                var scatter = AvaPlot1.Plot.Add.Scatter(seg, color: ScottPlot.Colors.Blue);
                                _dataScatters.Add(scatter);
                            }
                        }
                        // Восстанавливаем лимиты
                        AvaPlot1.Plot.Axes.SetLimits(limits);
                        AvaPlot1.Refresh();
                    };

                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(vm.ReceiverAngleDeg))
                        {
                            DrawReceiverAngleArrow(vm.ReceiverAngleDeg);
                        }
                        else if (e.PropertyName == nameof(vm.DataFlowStatus))
                        {
                            if (vm.DataFlowStatus.Contains("Данные идут"))
                                DrawReceiverAngleArrow(vm.ReceiverAngleDeg);
                        }
                    };

                    // Нарисовать стрелку при запуске, если данные уже идут
                    if (vm.DataFlowStatus.Contains("Данные идут"))
                        DrawReceiverAngleArrow(vm.ReceiverAngleDeg);
                }
            };

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm)
                {
                    vm.OnBuildRadar += (from, to) =>
                    {
                        var limits = AvaPlot1.Plot.Axes.GetLimits();
                        double[] angles = Plots.GetCircularRange(from, to); 
                        double[] radii = angles.Select(a => 100.0).ToArray(); //  100 

                        double[] anglesRad = angles.Select(a => (a+90) * Math.PI / 180.0).ToArray();

                        var points = anglesRad
                            .Select((theta, i) => new ScottPlot.Coordinates(
                                -radii[i] * Math.Cos(theta),
                                radii[i] * Math.Sin(theta)))
                            .ToList();

                        points.Insert(0, new ScottPlot.Coordinates(0, 0)); // 

                        
                        if (_sectorPolygon != null)
                        {
                            AvaPlot1.Plot.Remove(_sectorPolygon);
                            _sectorPolygon = null;
                        }

                           
                        _sectorPolygon = AvaPlot1.Plot.Add.Polygon(points.ToArray());
                        _sectorPolygon.FillColor = Colors.DarkGray.WithAlpha(.7);
                        _sectorPolygon.LineWidth = 0;

                        
                        AvaPlot1.Plot.Axes.SetLimits(limits);
                        AvaPlot1.Refresh();


                    };
                    
                    vm.ShowAntennaChanged += (show) =>
                    {
                        if (_angleArrow != null)
                        {
                            if (show)
                            {
                                // Показываем стрелку антенны
                                _angleArrow.ArrowLineWidth = 3;
                                _angleArrow.ArrowFillColor = Colors.Black;
                            }
                            else
                            {
                                // Скрываем стрелку антенны
                                _angleArrow.ArrowLineWidth = 0;
                                _angleArrow.ArrowFillColor = Colors.Transparent;
                            }
                            AvaPlot1.Refresh();
                        }
                    };
                    
                    vm.ShowSectorChanged += (show) =>
                    {
                        if (_sectorPolygon != null)
                        {
                            if (show)
                            {
                                // Показываем сектор
                                _sectorPolygon.FillColor = Colors.DarkGray.WithAlpha(.7);
                                _sectorPolygon.LineWidth = 0;
                            }
                            else
                            {
                                // Скрываем сектор
                                _sectorPolygon.FillColor = Colors.Transparent;
                                _sectorPolygon.LineWidth = 0;
                            }
                            AvaPlot1.Refresh();
                        }
                    };
                    
                    vm.BuildRadar();
                }
            };

            // �������� �� ��������� ������� ����
            this.GetObservable(Window.ClientSizeProperty).Subscribe(_ => ResetPlotAxes());

            ExportButton.Click += async (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    await vm.ExportSelectedTabAsync(this);
            };

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm)
                {
                    vm.PropertyChanged += (s2, e2) =>
                    {
                        if (e2.PropertyName == nameof(vm.SelectedTabIndex) || e2.PropertyName == nameof(vm.IsDiagramAcquisitionRunning))
                        {
                            if (!vm.IsDiagramAcquisitionRunning && vm.SelectedTab != null)
                                DrawTabPlot(vm.SelectedTab);
                            DrawAllVisiblePlots();
                        }
                    };
                }
            };
        }

        private void DrawReceiverAngleArrow(double angleDeg)
        {
            double radius = 100;
            double theta = (angleDeg + 90) * Math.PI / 180.0;
            double x = radius * Math.Cos(theta);
            double y = radius * Math.Sin(theta);

            if (_angleArrow == null)
            {
                _angleArrow = AvaPlot1.Plot.Add.Arrow(0, 0, -x, y);

            }
            else
            {
                _angleArrow.Base = new Coordinates(0, 0);
                _angleArrow.Tip = new Coordinates(-x, y);
            }

            _angleArrow.ArrowLineWidth = 1;
            _angleArrow.ArrowFillColor = Colors.CornflowerBlue;
            _angleArrow.ArrowLineColor = Colors.CornflowerBlue;
            _angleArrow.ArrowWidth = 3;
            _angleArrow.ArrowheadWidth = 6;

            // Учитываем состояние чекбокса ShowAntenna
            if (DataContext is MainWindowViewModel vm)
            {
                if (!vm.ShowAntenna)
                {
                    _angleArrow.ArrowLineWidth = 0;
                    _angleArrow.ArrowFillColor = Colors.Transparent;
                }
            }
            
            AvaPlot1.Refresh();
        }

        private void Header_DoubleTapped(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.DataContext is TabViewModel vm)
                vm.IsEditingHeader = true;
        }

        private void HeaderEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is TabViewModel vm)
                vm.IsEditingHeader = false;
        }

        private void HeaderEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is TabViewModel vm)
                vm.IsEditingHeader = false;
        }



        private void NumericUpDown_KeyDown(object? sender, KeyEventArgs e)
        {
            //   , Backspace, Delete,, Tab, Enter
            if (!(e.Key >= Key.D0 && e.Key <= Key.D9) &&
                !(e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) &&
                e.Key != Key.Back && e.Key != Key.Delete &&
                e.Key != Key.Left && e.Key != Key.Right &&
                e.Key != Key.Tab && e.Key != Key.Enter)
            {
                e.Handled = true;
            }
        }


        private void NumericUpDown_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is NumericUpDown numericUpDown && DataContext is MainWindowViewModel vm)
            {
                // Проверяем, что значение не пустое и корректное
                if (numericUpDown.Value == null || numericUpDown.Value < numericUpDown.Minimum || numericUpDown.Value > numericUpDown.Maximum)
                {
                    // Устанавливаем значение по умолчанию в зависимости от поля
                    if (numericUpDown.Name == "NumericUpDownSectorSize")
                    {
                        numericUpDown.Value = 10;
                        vm.SectorSize = "10";
                    }
                    else if (numericUpDown.Name == "NumericUpDownSectorCenter")
                    {
                        numericUpDown.Value = 0;
                        vm.SectorCenter = "0";
                    }
                }
            }
        }

        private void ResetPlotAxes()
        {
            if (AvaPlot1 != null)
            {
                AvaPlot1.Plot.Axes.AutoScale();
                AvaPlot1.Refresh();
            }
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm)
            {
                vm.StopMessaging();
            }
        }

        private void DrawTabPlot(TabViewModel tab)
        {
            if (tab == null || tab.Plot == null)
                return;
            // Очистить старые графики
            foreach (var scatter in _dataScatters)
                AvaPlot1.Plot.Remove(scatter);
            _dataScatters.Clear();

            if (this.DataContext is MainWindowViewModel vm && _polarAxis != null)
            {
                double[] angles = tab.Plot.Angles;
                double[] values = vm.IsPowerNormSelected ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                if (angles.Length == 0 || values.Length == 0 || angles.Length != values.Length)
                    return;
                List<ScottPlot.Coordinates> coords = new();
                for (int i = 0; i < angles.Length; i++)
                {
                    double mirroredAngle = (180 - angles[i]) % 360;
                    var pt = _polarAxis.GetCoordinates(values[i], mirroredAngle);
                    coords.Add(pt);
                }
                if (coords.Count > 1)
                {
                    var color = ScottPlot.Color.FromHex(tab.Plot.ColorHex);
                    var scatter = AvaPlot1.Plot.Add.Scatter(coords, color: color);
                    _dataScatters.Add(scatter);
                }
                AvaPlot1.Refresh();
            }
        }

        private void TogglePlotVisibility_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm && vm.SelectedTab != null)
            {
                vm.SelectedTab.Plot.IsVisible = !vm.SelectedTab.Plot.IsVisible;
                DrawAllVisiblePlots();
            }
        }

        private void DrawAllVisiblePlots()
        {
            if (this.DataContext is not MainWindowViewModel vm)
                return;
            // Очистить старые графики
            foreach (var scatter in _dataScatters)
                AvaPlot1.Plot.Remove(scatter);
            _dataScatters.Clear();

            if (_polarAxis == null) return;
            foreach (var tab in vm.Tabs)
            {
                if (tab.Plot != null && tab.Plot.IsVisible)
                {
                    double[] angles = tab.Plot.Angles;
                    double[] values = vm.IsPowerNormSelected ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                    if (angles.Length == 0 || values.Length == 0 || angles.Length != values.Length)
                        continue;
                    List<ScottPlot.Coordinates> coords = new();
                    for (int i = 0; i < angles.Length; i++)
                    {
                        double mirroredAngle = (180 - angles[i]) % 360;
                        var pt = _polarAxis.GetCoordinates(values[i], mirroredAngle);
                        coords.Add(pt);
                    }
                    if (coords.Count > 1)
                    {
                        var color = ScottPlot.Color.FromHex(tab.Plot.ColorHex);
                        var scatter = AvaPlot1.Plot.Add.Scatter(coords, color: color);
                        _dataScatters.Add(scatter);
                    }
                }
            }
            AvaPlot1.Refresh();
        }
    }
}