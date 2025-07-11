using AntennaAV.Services;
using AntennaAV.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Colormaps;
using ScottPlot.Hatches;
using ScottPlot.Plottables;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Markup;
using System.Timers;

namespace AntennaAV.Views
{
    public partial class MainWindow : Window
    {
        private ScottPlot.Plottables.PolarAxis? _polarAxis;
        private ScottPlot.Plottables.PolarAxis? _polarAxisTx;
        private ScottPlot.Plottables.Polygon? _sectorPolygon;
        private ScottPlot.Plottables.Arrow? _angleArrow;

        private bool _needsAvaPlot2Refresh = false;
        private Timer? _avaPlot2RefreshTimer;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
            this.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            AvaPlot2.PointerMoved += AvaPlot2_PointerMoved;
            AvaPlot2.PointerPressed += AvaPlot2_PointerPressed;



            // Удаляем старую ось, если есть (на старте не нужно, но для универсальности)
            if (_polarAxis != null)
            {
                AvaPlot1.Plot.Remove(_polarAxis);
                _polarAxis = null;
            }
            bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
            _polarAxis = Plots.Initialize(AvaPlot1, isDark);
            _polarAxisTx = Plots.InitializeSmall(AvaPlot2, isDark);

            // При инициализации просто передаём true для isLogScale
            Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, true, -50, 0, isDark);
            NumericUpDownSectorSize.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            NumericUpDownSectorCenter.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm)
                {
                    vm.OnBuildRadarPlot += (angles, values) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            // Сохраняем лимиты
                            var limits = AvaPlot1.Plot.Axes.GetLimits();

                            // Проверка на пустой массив и вывод длины
                            //System.Diagnostics.Debug.WriteLine($"OnBuildRadarPlot: values.Length={values?.Length ?? -1}");
                            if (values == null || values.Length == 0)
                            {
                                // Удаляем все старые графики, если данных нет
                                if (vm.SelectedTab != null)
                                {
                                    foreach (var scatter in vm.SelectedTab.DataScatters)
                                        AvaPlot1.Plot.Remove(scatter);
                                    vm.SelectedTab.DataScatters.Clear();
                                }
                                AvaPlot1.Refresh();
                                return;
                            }

                            // Сортировка по углу
                            var zipped = angles.Zip(values, (a, v) => new { Angle = a, Value = v })
                                               .OrderBy(x => x.Angle)
                                               .ToList();
                            var sortedAngles = zipped.Select(x => x.Angle).ToArray();
                            var sortedValues = zipped.Select(x => x.Value).ToArray();

                            // Разбиваем на сегменты по разрывам углов
                            List<List<Coordinates>> segments = new();
                            List<Coordinates> current = new();
                            double min = sortedValues.Min();
                            double max = sortedValues.Max();
                            double r_max = 100;
                            bool allRadiiEqual = Math.Abs(max - min) < 1e-8;
                            double angleGapThreshold = allRadiiEqual ? 30.0 : 1.0; // если мощности одинаковые, разрешаем большие разрывы, но не более 30°
                            for (int i = 0; i < sortedAngles.Length; i++)
                            {
                                double mirroredAngle = (360 - sortedAngles[i]) % 360; // если нужно отзеркалить
                                double r = (max - min) > 0 ? r_max * (sortedValues[i] - min) / (max - min) : r_max;
                                var pt = _polarAxis.GetCoordinates(r, mirroredAngle);
                                if (i > 0 && Math.Abs(sortedAngles[i] - sortedAngles[i - 1]) > angleGapThreshold)
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
                            if (vm.SelectedTab != null)
                            {
                                foreach (var scatter in vm.SelectedTab.DataScatters)
                                    AvaPlot1.Plot.Remove(scatter);
                                vm.SelectedTab.DataScatters.Clear();
                            }

                            // Рисуем каждый сегмент отдельно
                            if (vm.SelectedTab != null)
                            {
                                foreach (var seg in segments)
                                {
                                    if (seg.Count > 1)
                                    {
                                        var color = ScottPlot.Color.FromHex(vm.SelectedTab.Plot?.ColorHex ?? "#0000FF");
                                        var scatter = AvaPlot1.Plot.Add.Scatter(seg, color: color);
                                        scatter.MarkerSize = 0;
                                        scatter.LineWidth = 2;
                                        vm.SelectedTab.DataScatters.Add(scatter);
                                    }
                                }
                            }
                            // Восстанавливаем лимиты
                            AvaPlot1.Plot.Axes.SetLimits(limits);

                            // === Добавлено: обновление кругов полярной оси ===
                            bool isLogScale = vm.IsPowerNormSelected; 
                            Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, min, max, isDark);
                            AvaPlot1.Refresh();
                        });
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
                            {
                                DrawTransmitterAnglePoint(vm.TransmitterAngleDeg);
                                DrawReceiverAngleArrow(vm.ReceiverAngleDeg);
                            }
                        }
                        else if (e.PropertyName == nameof(vm.IsPowerNormSelected))
                        {
                            // Проверяем, есть ли данные для построения графика
                            bool hasData = vm.Tabs.Any(tab => tab.Plot != null && tab.Plot.Angles.Length > 0);
                            if (!hasData && _polarAxis != null)
                            {
                                bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                                if (vm.IsPowerNormSelected)
                                    Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, true, -50, 0, isDark);
                                else
                                    Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, false, 0, 1, isDark);
                                AvaPlot1.Refresh();
                            }
                        }
                        else if (e.PropertyName == nameof(vm.TransmitterAngleDeg))
                        {
                            DrawTransmitterAnglePoint(vm.TransmitterAngleDeg);
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
                        _sectorPolygon.FillColor = Colors.DarkGray.WithAlpha(.5);
                        _sectorPolygon.LineWidth = 0;

                        // Добавлено: скрывать сектор, если чекбокс снят
                        if (this.DataContext is MainWindowViewModel vm)
                        {
                            if (!vm.ShowSector)
                            {
                                _sectorPolygon.FillColor = Colors.Transparent;
                                _sectorPolygon.LineWidth = 0;
                            }
                        }

                        
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
                                _angleArrow.ArrowFillColor = ScottPlot.Color.FromHex("#0073cf");
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
                                _sectorPolygon.FillColor = Colors.DarkGray.WithAlpha(.5);
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
                    if (vm.SelectedTab != null)
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
                            //DrawAllVisiblePlots();
                        }
                    };
                    vm.Tabs.CollectionChanged += (s2, e2) =>
                    {
                        DrawAllVisiblePlots();
                    };
                    vm.RequestPlotRedraw += () =>
                    {
                        if (vm.SelectedTab != null)
                            DrawTabPlot(vm.SelectedTab);
                    };
                }
            };
            Application.Current!.ActualThemeVariantChanged += OnThemeChanged;
            // Теперь применяем тему ко всем графикам только после инициализации
            SetScottPlotTheme(Application.Current!.ActualThemeVariant == ThemeVariant.Dark, AvaPlot1, AvaPlot2);
            _avaPlot2RefreshTimer = new Timer(100); // 10 Гц
            _avaPlot2RefreshTimer.Elapsed += (s, e) =>
            {
                if (_needsAvaPlot2Refresh)
                {
                    _needsAvaPlot2Refresh = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (AvaPlot2 != null && AvaPlot2.Plot != null)
                            AvaPlot2.Refresh();
                    });
                }
            };
            _avaPlot2RefreshTimer.Start();
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
            _angleArrow.ArrowFillColor = ScottPlot.Color.FromHex("#0073cf");
            _angleArrow.ArrowLineColor = ScottPlot.Color.FromHex("#0073cf");
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

        private ScottPlot.Plottables.Marker? _hoverMarker;
        private double _hoverAngleDeg = double.NaN;



        private void AvaPlot2_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (AvaPlot2 == null || AvaPlot2.Plot == null)
                return;
            var point = e.GetPosition(AvaPlot2);

            // Центр контрола
            double centerX = AvaPlot2.Bounds.Width / 2.0;
            double centerY = AvaPlot2.Bounds.Height / 2.0;

            // Вектор от центра к мыши
            double dx = point.X - centerX;
            double dy = point.Y - centerY; // тут не инвертируем, просто расстояние

            // Радиус (расстояние от центра до мыши) в пикселях
            double rPix = Math.Sqrt(dx * dx + dy * dy);

            // Радиус внешнего круга в пикселях (например, 90% от половины минимального размера)
            double plotRadiusPix = 0.6 * Math.Min(AvaPlot2.Bounds.Width, AvaPlot2.Bounds.Height) / 2.0;

            // Порог: мышь должна быть в пределах ±10 пикселей от внешнего круга
            double threshold = 20.0;
            if (Math.Abs(rPix - plotRadiusPix) > threshold)
            {
                // Скрыть маркер, если он есть
                Dispatcher.UIThread.Post(() =>
                {
                    if (_hoverMarker != null && AvaPlot2.Plot.GetPlottables().Contains(_hoverMarker))
                    {
                        AvaPlot2.Plot.Remove(_hoverMarker);
                        _hoverMarker = null;
                        RequestAvaPlot2Refresh();
                    }
                });
                _hoverAngleDeg = double.NaN;
                if (DataContext is MainWindowViewModel vm)
                    DrawTransmitterAnglePoint(vm.TransmitterAngleDeg);
                return;
            }

            // Угол (0° вверх, по часовой стрелке)
            double angleRad = Math.Atan2(dx, -dy); // -dy, чтобы 0° было вверх
            double angleDeg = (angleRad * 180.0 / Math.PI + 360) % 360;

            // ОТЗЕРКАЛИВАЕМ и ПОВОРАЧИВАЕМ на 90° по часовой
            double mirroredAngle = (360 - angleDeg + 180) % 360;
            // Округление к ближайшим 10°
            double snapStep = 10.0;
            double snappedAngle = Math.Round(mirroredAngle / snapStep) * snapStep;
            snappedAngle = snappedAngle % 360;
            _hoverAngleDeg = snappedAngle;

            // Координаты на внешнем круге (радиус 100 в координатах графика)
            double r = 100;
            if (_polarAxisTx == null) return;
            var coord = _polarAxisTx.GetCoordinates(r, snappedAngle);

            Dispatcher.UIThread.Post(() =>
            {
                if (_hoverMarker == null)
                    _hoverMarker = AvaPlot2.Plot.Add.Marker(coord.X, coord.Y, color: ScottPlot.Color.FromHex("#FF0000"), size: 8);
                else
                {
                    _hoverMarker.X = coord.X;
                    _hoverMarker.Y = coord.Y;
                }
                RequestAvaPlot2Refresh();
            });
        }

        private void AvaPlot2_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Клик срабатывает только если маркер есть и угол валиден
            if (_hoverMarker != null && !double.IsNaN(_hoverAngleDeg))
            {
                double selectedAngle = _hoverAngleDeg;
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.OnTransmitterAngleSelected?.Invoke((360 - selectedAngle) % 360);
                }
            }
            // иначе — ничего не делаем
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

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Если клик не по TextBox
            if (e.Source is not TextBox)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    foreach (var tab in vm.Tabs)
                    {
                        if (tab.IsEditingHeader)
                            tab.IsEditingHeader = false;
                    }
                }
            }
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
            if (AvaPlot2 != null)
            {
                AvaPlot2.Plot.Axes.AutoScale();
                AvaPlot2.Refresh();
            }
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm)
            {
                vm.StopMessaging();
            }
        }

        public void DrawTabPlot(TabViewModel tab, double? globalMin = null, double? globalMax = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (tab == null || tab.Plot == null)
                    return;
                // Очистить только свои графики
                foreach (var scatter in tab.DataScatters)
                    AvaPlot1.Plot.Remove(scatter);
                tab.DataScatters.Clear();

                if (this.DataContext is MainWindowViewModel vm && _polarAxis != null)
                {
                    double[] angles = tab.Plot.Angles;
                    double[] values = vm.IsPowerNormSelected ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                    if (angles == null || values == null || angles.Length == 0 || values.Length == 0 || angles.Length != values.Length)
                        return;
                    // Сортировка по углу
                    var zipped = angles.Zip(values, (a, v) => new { Angle = a, Value = v })
                                       .OrderBy(x => x.Angle)
                                       .ToList();
                    var sortedAngles = zipped.Select(x => x.Angle).ToArray();
                    var sortedValues = zipped.Select(x => x.Value).ToArray();
                    double min = globalMin ?? sortedValues.Min();
                    double max = globalMax ?? sortedValues.Max();
                    double r_max = 100;
                    bool allRadiiEqual = Math.Abs(max - min) < 1e-8;
                    double angleGapThreshold = allRadiiEqual ? 30.0 : 1.0;

                    List<List<ScottPlot.Coordinates>> segments = new();

                    // Если все значения мощности одинаковые и углов больше одной — строим линию по всем точкам
                    if (allRadiiEqual && sortedAngles.Length > 1)
                    {
                        List<ScottPlot.Coordinates> circle = new();
                        for (int i = 0; i < sortedAngles.Length; i++)
                        {
                            double mirroredAngle = (360 - sortedAngles[i]) % 360;
                            double r = r_max;
                            var pt = _polarAxis.GetCoordinates(r, mirroredAngle);
                            circle.Add(pt);
                        }
                        // Замыкаем окружность, если точек больше двух
                        if (circle.Count > 2)
                            circle.Add(circle[0]);
                        segments.Add(circle);
                    }
                    else
                    {
                        List<ScottPlot.Coordinates> current = new();
                        for (int i = 0; i < sortedAngles.Length; i++)
                        {
                            double mirroredAngle = (360 - sortedAngles[i]) % 360;
                            double r = (max - min) > 0 ? r_max * (sortedValues[i] - min) / (max - min) : r_max;
                            var pt = _polarAxis.GetCoordinates(r, mirroredAngle);
                            if (i > 0 && Math.Abs(sortedAngles[i] - sortedAngles[i - 1]) > angleGapThreshold)
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

                    // === Сначала обновляем круги ===
                    bool isLogScale = vm.IsPowerNormSelected;
                    bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                    Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, min, max, isDark);

                    // === Потом рисуем графики ===
                    var color = ScottPlot.Color.FromHex(tab.Plot.ColorHex);
                    System.Diagnostics.Debug.WriteLine($"[DrawTabPlot] Segments: {segments.Count}");
                    for (int i = 0; i < segments.Count; i++)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Segment {i + 1}: {segments[i].Count} points");
                    }
                    foreach (var seg in segments)
                    {
                        if (seg.Count > 1)
                        {
                            var scatter = AvaPlot1.Plot.Add.Scatter(seg, color: color);
                            scatter.MarkerSize = 0;
                            scatter.LineWidth = 2;
                            tab.DataScatters.Add(scatter);
                        }
                    }
                    AvaPlot1.Refresh();
                }
            });
        }

        private void TogglePlotVisibility_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm && vm.SelectedTab != null && vm.SelectedTab.Plot != null)
            {
                vm.SelectedTab.Plot.IsVisible = !vm.SelectedTab.Plot.IsVisible;
                DrawAllVisiblePlots();
            }
        }

        private void DrawAllVisiblePlots()
        {
            try
            {
                if (this.DataContext is not MainWindowViewModel vm)
                    return;
                // Очистить старые графики
                foreach (var tab in vm.Tabs)
                {
                    foreach (var scatter in tab.DataScatters)
                        AvaPlot1.Plot.Remove(scatter);
                    tab.DataScatters.Clear();
                }

                if (_polarAxis == null) return;
                bool isLogScale = vm.IsPowerNormSelected;
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
                    AvaPlot1.Refresh();
                    return;
                }
                // Теперь строим все графики с общей нормализацией
                foreach (var tab in vm.Tabs)
                {
                    if (tab.Plot != null && tab.Plot.IsVisible)
                    {
                        DrawTabPlot(tab, globalMin, globalMax);
                    }
                }
                AvaPlot1.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DrawAllVisiblePlots] Exception: {ex}");
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            var isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
             if (_polarAxis != null)
             {
                Plots.UpdatePolarAxisTheme(_polarAxis, isDark);
                Plots.AddCustomSpokeLines(AvaPlot1, _polarAxis, isDark);

                AvaPlot1?.Refresh();
            }
            
            if (_polarAxisTx != null)
            {
                AvaPlot2.Plot.Remove(_polarAxisTx);
                _polarAxisTx = null;
            }
            if (this.DataContext is MainWindowViewModel vm)
            {
                //_polarAxis = Plots.Initialize(AvaPlot1, isDark);
                //bool isLogScale = vm.IsPowerNormSelected;
                //Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, -50, 0, isDark);
                _polarAxisTx = Plots.InitializeSmall(AvaPlot2, isDark);

                _transmitterMarker = null;
                DrawTransmitterAnglePoint(vm.TransmitterAngleDeg);
            }
            // Теперь применяем тему ко всем графикам
            if(AvaPlot1 != null && AvaPlot2 != null)
                SetScottPlotTheme(isDark, AvaPlot1, AvaPlot2);
        }

        private static void SetScottPlotTheme(bool isDark, params AvaPlot[] plots)
        {
            foreach (var avaPlot in plots)
            {
                if (avaPlot?.Plot != null)
                {
                    if (isDark)
                    {
                        avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
                        avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
                        avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
                        avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
                        avaPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
                        avaPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
                        avaPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");
                        avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
                    }
                    else
                    {
                        avaPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                        avaPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                        avaPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#222222"));
                        avaPlot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#e5e5e5");
                        avaPlot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#f0f0f0");
                        avaPlot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#222222");
                        avaPlot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#222222");
                        avaPlot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
                    }
                    avaPlot.Refresh();
                }
            }
        }

        private ScottPlot.Plottables.Marker? _transmitterMarker;
        private void DrawTransmitterAnglePoint(double angleDeg)
        {
            if (AvaPlot2 == null || AvaPlot2.Plot == null)
                return;
            double radius = 100;
            double angleRad = (-angleDeg + 270) * Math.PI / 180.0;
            double x = radius * Math.Cos(angleRad);
            double y = radius * Math.Sin(angleRad);
            Dispatcher.UIThread.Post(() =>
            {
                if (_transmitterMarker == null)
                {
                    // Добавляем маркер впервые
                    _transmitterMarker = AvaPlot2.Plot.Add.Marker(
                        x: x,
                        y: y,
                        color: ScottPlot.Color.FromHex("#0073cf"),
                        size: 10
                    );
                }
                else
                {
                    // Обновляем координаты существующего маркера
                    _transmitterMarker.X = x;
                    _transmitterMarker.Y = y;
                }
                RequestAvaPlot2Refresh();
            });
        }

        private async void ImportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm)
            {
                await vm.ImportTableFromCsvAsync(this);
                if (vm.SelectedTab != null)
                    DrawTabPlot(vm.SelectedTab);
            }
        }

        private void RequestAvaPlot2Refresh()
        {
            _needsAvaPlot2Refresh = true;
        }
    }
}