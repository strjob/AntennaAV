using AntennaAV.Services;
using AntennaAV.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using HarfBuzzSharp;
using ScottPlot;
using ScottPlot.ArrowShapes;
using ScottPlot.Avalonia;
using ScottPlot.Colormaps;
using ScottPlot.Hatches;
using ScottPlot.Plottables;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using System.Windows.Markup;

namespace AntennaAV.Views
{
    public partial class MainWindow : Window
    {
        private ScottPlot.Plottables.PolarAxis? _polarAxis;
        private ScottPlot.Plottables.PolarAxis? _polarAxisTx;
        private ScottPlot.Plottables.Arrow? _angleArrow;

        private bool _needsAvaPlot2Refresh = false;
        private Timer? _avaPlot2RefreshTimer;
        private Timer? _avaPlot1RefreshTimer;

        private readonly object _plotLock = new();
        private bool _needAvaPlot1Refresh = false;

        private ScottPlot.Plottables.Polygon? _sectorPolygon;
        private double? _lastSectorStart = null;
        private double? _lastSectorEnd = null;

        // Удаляем локальные методы InitializeSectorPolygons и ApplySectorRange

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
            this.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            AvaPlot2.PointerMoved += AvaPlot2_PointerMoved;
            AvaPlot2.PointerPressed += AvaPlot2_PointerPressed;

            bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
            if (AvaPlot1 != null)
            {
                _polarAxis = Plots.Initialize(AvaPlot1, isDark);
                Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, true, -50, 0, isDark);
                // _sectorPolygon = Plots.InitializeSectorPolygon(AvaPlot1); // Удаляем старую инициализацию
                _sectorPolygon = null;
            }
            if (AvaPlot2 != null)
                _polarAxisTx = Plots.InitializeSmall(AvaPlot2, isDark);

            if (AvaPlot1 != null && AvaPlot2 != null)
            {
                Plots.SetScottPlotTheme(Application.Current!.ActualThemeVariant == ThemeVariant.Dark, true, AvaPlot1);
                Plots.SetScottPlotTheme(Application.Current!.ActualThemeVariant == ThemeVariant.Dark, false, AvaPlot2);
            }


            NumericUpDownSectorSize.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            NumericUpDownSectorCenter.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm)
                {
                    vm.OnBuildRadarPlot += (angles, values) =>
                    {
                        //Dispatcher.UIThread.Post(() =>
                        //{
                        //    if (AvaPlot1 != null && _polarAxis != null)
                        //    {// Сохраняем лимиты
                        //        var limits = AvaPlot1.Plot.Axes.GetLimits();

                        //        // Проверка на пустой массив и вывод длины
                        //        //System.Diagnostics.Debug.WriteLine($"OnBuildRadarPlot: values.Length={values?.Length ?? -1}");
                        //        if (values == null || values.Length == 0)
                        //        {
                        //            // Удаляем все старые графики, если данных нет
                        //            if (vm.SelectedTab != null)
                        //            {
                        //                foreach (var scatter in vm.SelectedTab.DataScatters)
                        //                    AvaPlot1.Plot.Remove(scatter);
                        //                vm.SelectedTab.DataScatters.Clear();

                        //            }
                        //            _needAvaPlot1Refresh = true;
                        //            return;
                        //        }

                        //        // Сортировка по углу
                        //        // var zipped = angles.Zip(values, (a, v) => new { Angle = a, Value = v })
                        //        //                    .OrderBy(x => x.Angle)
                        //        //                    .ToList();
                        //        // var sortedAngles = zipped.Select(x => x.Angle).ToArray();
                        //        // var sortedValues = zipped.Select(x => x.Value).ToArray();

                        //        // Разбиваем на сегменты по разрывам углов
                        //        List<List<Coordinates>> segments = new();
                        //        List<Coordinates> current = new();
                        //        double min = values.Min();
                        //        double max = values.Max();
                        //        double r_max = 100;
                        //        bool allRadiiEqual = Math.Abs(max - min) < 1e-8;
                        //        double angleGapThreshold = allRadiiEqual ? 30.0 : 1.0; // если мощности одинаковые, разрешаем большие разрывы, но не более 30°
                        //        for (int i = 0; i < angles.Length; i++)
                        //        {
                        //            double mirroredAngle = (360 - angles[i]) % 360; // если нужно отзеркалить
                        //            double r = (max - min) > 0 ? r_max * (values[i] - min) / (max - min) : r_max;
                        //            var pt = _polarAxis.GetCoordinates(r, mirroredAngle);
                        //            if (i > 0 && Math.Abs(angles[i] - angles[i - 1]) > angleGapThreshold)
                        //            {
                        //                if (current.Count > 0)
                        //                    segments.Add(current);
                        //                current = new List<Coordinates>();
                        //            }
                        //            current.Add(pt);
                        //        }
                        //        if (current.Count > 0)
                        //            segments.Add(current);

                        //        // Удаляем все старые графики
                        //        if (vm.SelectedTab != null)
                        //        {
                        //            foreach (var scatter in vm.SelectedTab.DataScatters)
                        //                AvaPlot1.Plot.Remove(scatter);
                        //            vm.SelectedTab.DataScatters.Clear();
                        //        }

                        //        // Рисуем каждый сегмент отдельно
                        //        if (vm.SelectedTab != null)
                        //        {
                        //            foreach (var seg in segments)
                        //            {
                        //                if (seg.Count > 1)
                        //                {
                        //                    var color = ScottPlot.Color.FromHex(vm.SelectedTab.Plot?.ColorHex ?? "#0000FF");
                        //                    var scatter = AvaPlot1.Plot.Add.Scatter(seg, color: color);
                        //                    scatter.MarkerSize = 0;
                        //                    scatter.LineWidth = 2;
                        //                    vm.SelectedTab.DataScatters.Add(scatter);
                        //                }
                        //            }
                        //        }
                        //        // Восстанавливаем лимиты
                        //        AvaPlot1.Plot.Axes.SetLimits(limits);

                        //        // === Добавлено: обновление кругов полярной оси ===
                        //        bool isLogScale = vm.IsPowerNormSelected;
                        //        Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, min, max, isDark);
                        //        _needAvaPlot1Refresh = true;
                        //    }


                        //});
                        if(vm.SelectedTab != null && _polarAxis != null && AvaPlot1 != null)
                        {
                            DrawPolarPlot(angles, values, _polarAxis, AvaPlot1, vm.SelectedTab.DataScatters, vm.SelectedTab.Plot.ColorHex, vm.IsPowerNormSelected, isDark);

                        }

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
                            bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                            if (_polarAxis != null && AvaPlot1 != null)
                            {
                                if (hasData)
                                {
                                    DrawAllVisiblePlots();                                
                                }
                                else
                                {
                                    if (vm.IsPowerNormSelected)
                                        Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, true, -50, 0, isDark);
                                    else
                                        Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, false, 0, 1, isDark);
                                }
                                _needAvaPlot1Refresh = true;
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
                        Dispatcher.UIThread.Post(() =>
                        {
                            double end = (from + 360) % 360;
                            double start = (to + 360) % 360;
                            _lastSectorStart = start;
                            _lastSectorEnd = end;
                            var vm = this.DataContext as MainWindowViewModel;
                            if (AvaPlot1 != null)
                            {
                                if (_sectorPolygon != null)
                                {
                                    AvaPlot1.Plot.Remove(_sectorPolygon);
                                    _sectorPolygon = null;
                                }
                                _sectorPolygon = Plots.CreateSectorPolygon(AvaPlot1, start, end, vm?.ShowSector ?? true);
                                if (_angleArrow != null)
                                    AvaPlot1.Plot.MoveToFront(_angleArrow);
                                _needAvaPlot1Refresh = true;
                            }
                        });
                    };
                    
                    vm.ShowAntennaChanged += (show) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            lock (_plotLock)
                            {
                                if (_angleArrow != null)
                                {
                                    if (show)
                                    {
                                        // Показываем стрелку антенны
                                        _angleArrow.IsVisible = true;
                                    }
                                    else
                                    {
                                        // Скрываем стрелку антенны
                                        _angleArrow.IsVisible = false;
                                    }
                                    if (AvaPlot1 != null)
                                        _needAvaPlot1Refresh = true;

                                }
                            }
                        });
                    };
                    
                    vm.ShowSectorChanged += (show) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            lock (_plotLock)
                            {
                                if (AvaPlot1 != null && _sectorPolygon != null)
                                {
                                    _sectorPolygon.IsVisible = show;
                                    _needAvaPlot1Refresh = true;
                                }
                            }
                        });
                    };
                    
                    vm.BuildRadar();
                }
            };

            //     
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
                            if (!vm.IsDiagramAcquisitionRunning && vm.SelectedTab != null && AvaPlot1 != null)
                            {
                                if (vm.SelectedTab != null && vm.SelectedTab.Plot != null && _polarAxis != null)
                                {
                                    double[] angles = vm.SelectedTab.Plot.Angles;
                                    double[] values = vm.IsPowerNormSelected ? vm.SelectedTab.Plot.PowerNormValues : vm.SelectedTab.Plot.VoltageNormValues;
                                    bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                                    DrawPolarPlot(angles, values, _polarAxis, AvaPlot1, vm.SelectedTab.DataScatters, vm.SelectedTab.Plot.ColorHex, vm.IsPowerNormSelected, isDark);
                                }
                                //DrawAllVisiblePlots();
                            }
                        }
                    };
                    vm.Tabs.CollectionChanged += (s2, e2) =>
                    {
                        DrawAllVisiblePlots();
                    };
                    vm.RequestPlotRedraw += () =>
                    {
                        if (vm.SelectedTab == null || vm.SelectedTab.Plot == null)
                            return;
                        if (vm.SelectedTab.Plot.Angles == null || vm.SelectedTab.Plot.Angles.Length == 0)
                            return;
                        var values = vm.IsPowerNormSelected ? vm.SelectedTab.Plot.PowerNormValues : vm.SelectedTab.Plot.VoltageNormValues;
                        if (values == null || values.Length == 0)
                            return;
                        if (_polarAxis != null && AvaPlot1 != null)
                        {
                            bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                            DrawPolarPlot(vm.SelectedTab.Plot.Angles, values, _polarAxis, AvaPlot1, vm.SelectedTab.DataScatters, vm.SelectedTab.Plot.ColorHex, vm.IsPowerNormSelected, isDark);
                        }
                    };
                }
            };
            Application.Current!.ActualThemeVariantChanged += OnThemeChanged;

            _avaPlot1RefreshTimer = new Timer(100); // 10 Гц
            _avaPlot1RefreshTimer.Elapsed += (s, e) =>
            {
                if (_needAvaPlot1Refresh)
                {
                    _needAvaPlot1Refresh = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        lock (_plotLock)
                        {
                            if (AvaPlot1 != null)
                                AvaPlot1.Refresh();
                        }
                    });
                }
            };
            _avaPlot1RefreshTimer.Start();




            _avaPlot2RefreshTimer = new Timer(100); // 10 Гц
            _avaPlot2RefreshTimer.Elapsed += (s, e) =>
            {
                if (_needsAvaPlot2Refresh)
                {
                    _needsAvaPlot2Refresh = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (AvaPlot2 == null)
                        {
                            System.Diagnostics.Debug.WriteLine("[AvaPlot2RefreshTimer] AvaPlot2 == null");
                            return;
                        }
                        if (AvaPlot2.Plot == null)
                        {
                            System.Diagnostics.Debug.WriteLine("[AvaPlot2RefreshTimer] AvaPlot2.Plot == null");
                            return;
                        }
                        AvaPlot2.Refresh();
                    });
                }
                
                
            };
            _avaPlot2RefreshTimer.Start();
        }

        private void DrawReceiverAngleArrow(double angleDeg)
        {
            Dispatcher.UIThread.Post(() =>
            {
                lock (_plotLock)
                {
                    if (AvaPlot1 == null || AvaPlot1.Plot == null || _polarAxis == null)
                        return;
                    double radius = 100;
                    double angleRad = (-angleDeg + 90) * Math.PI / 180.0;
                    double x = radius * Math.Cos(angleRad);
                    double y = radius * Math.Sin(angleRad);

                    if (_angleArrow == null)
                    {
                        _angleArrow = AvaPlot1.Plot.Add.Arrow(
                            new ScottPlot.Coordinates(0, 0),
                            new ScottPlot.Coordinates(x, y)

                        );
                        _angleArrow.ArrowLineWidth = 0;
                        _angleArrow.ArrowWidth = 4;
                        _angleArrow.ArrowheadWidth = 10;
                        _angleArrow.ArrowFillColor = ScottPlot.Color.FromHex("#0073cf");

                    }
                    else
                    {
                        _angleArrow.Base = new ScottPlot.Coordinates(0, 0);
                        _angleArrow.Tip = new ScottPlot.Coordinates(x, y);
                    }
                    lock (_plotLock)
                    {
                        AvaPlot1.Refresh();
                    }
                }
            });
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
                        _hoverMarker.IsVisible = false;
                    }
                });
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
                    _hoverMarker.IsVisible = true;
                }
                RequestAvaPlot2Refresh();
            });
        }

        private void AvaPlot2_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Клик срабатывает только если маркер есть и угол валиден
            if (_hoverMarker != null && _hoverMarker.IsVisible)
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
                _needAvaPlot1Refresh = true;
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

        /// <summary>
        /// Общий метод для построения и отрисовки полярного графика
        /// </summary>
        private void DrawPolarPlot(
            double[] angles,
            double[] values,
            ScottPlot.Plottables.PolarAxis axis,
            ScottPlot.Avalonia.AvaPlot plot,
            List<ScottPlot.Plottables.Scatter> dataScatters,
            string colorHex,
            bool isLogScale,
            bool isDark,
            double? min = null,
            double? max = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                lock (_plotLock)
                {

                    if (angles == null || values == null || angles.Length == 0 || values.Length == 0 || angles.Length != values.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DrawPolarPlot] Нет данных для построения: angles={angles?.Length ?? -1}, values={values?.Length ?? -1}");
                        return;
                    }
                    double actualMin = min ?? values.Min();
                    double actualMax = max ?? values.Max();
                    double r_max = 100;
                    bool allRadiiEqual = Math.Abs(actualMax - actualMin) < 1e-8;
                    double angleGapThreshold = allRadiiEqual ? 30.0 : 1.0;

                    List<List<ScottPlot.Coordinates>> segments = new();

                    // Если все значения одинаковые и углов больше одной — строим линию по всем точкам (замкнутый круг)
                    if (allRadiiEqual && angles.Length > 1)
                    {
                        List<ScottPlot.Coordinates> circle = new();
                        for (int i = 0; i < angles.Length; i++)
                        {
                            double mirroredAngle = (360 - angles[i]) % 360;
                            double r = r_max;
                            var pt = axis.GetCoordinates(r, mirroredAngle);
                            circle.Add(pt);
                        }
                        if (circle.Count > 2)
                            circle.Add(circle[0]);
                        segments.Add(circle);
                    }
                    //else
                    //{
                    //    List<ScottPlot.Coordinates> current = new();
                    //    for (int i = 0; i < angles.Length; i++)
                    //    {
                    //        double mirroredAngle = (360 - angles[i]) % 360;
                    //        double r = (actualMax - actualMin) > 0 ? r_max * (values[i] - actualMin) / (actualMax - actualMin) : r_max;
                    //        var pt = axis.GetCoordinates(r, mirroredAngle);
                    //        if (i > 0 && Math.Abs(angles[i] - angles[i - 1]) > angleGapThreshold)
                    //        {
                    //            if (current.Count > 0)
                    //                segments.Add(current);
                    //            current = new List<ScottPlot.Coordinates>();
                    //        }
                    //        current.Add(pt);
                    //    }
                    //    if (current.Count > 0)
                    //        segments.Add(current);
                    //}

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
                            var pt = axis.GetCoordinates(r, mirroredAngle);
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

                    // Обновляем круги полярной оси
                    Plots.AutoUpdatePolarAxisCircles(plot, axis, isLogScale, actualMin, actualMax, isDark);

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
                    //plot.Refresh();
                    _needAvaPlot1Refresh = true;
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
            Dispatcher.UIThread.Post(() =>
            {
                lock (_plotLock)
                {
                    try
                    {
                        if (this.DataContext is not MainWindowViewModel vm)
                        {
                            System.Diagnostics.Debug.WriteLine("[DrawAllVisiblePlots] DataContext не MainWindowViewModel");
                            return;
                        }
                        if (_polarAxis == null)
                        {
                            System.Diagnostics.Debug.WriteLine("[DrawAllVisiblePlots] _polarAxis не инициализирован");
                            return;
                        }
                        // Очистить старые графики
                        foreach (var tab in vm.Tabs)
                        {
                            foreach (var scatter in tab.DataScatters)
                                AvaPlot1.Plot.Remove(scatter);
                            tab.DataScatters.Clear();
                        }

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
                            _needAvaPlot1Refresh = true;
                            return;
                        }
                        // Теперь строим все графики с общей нормализацией
                        bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                        foreach (var tab in vm.Tabs)
                        {
                            if (tab.Plot != null && tab.Plot.IsVisible)
                            {
                                double[] angles = tab.Plot.Angles;
                                double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                                DrawPolarPlot(angles, values, _polarAxis, AvaPlot1, tab.DataScatters, tab.Plot.ColorHex, isLogScale, isDark, globalMin, globalMax);
                            }
                        }
                        _needAvaPlot1Refresh = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DrawAllVisiblePlots] Exception: {ex}");
                    }
                }
            });
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {

            Dispatcher.UIThread.Post(() =>
            {
                var isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                if (_polarAxis != null)
                {
                    Plots.UpdatePolarAxisTheme(_polarAxis, isDark);
                    Plots.AddCustomSpokeLines(AvaPlot1, _polarAxis, isDark);
                    _needAvaPlot1Refresh = true;
                }
                if (_polarAxisTx != null)
                {
                    Plots.UpdatePolarAxisThemeSmall(_polarAxisTx, AvaPlot2, isDark);
                    AvaPlot2?.Refresh();
                }
                if (AvaPlot1 != null && AvaPlot2 != null)
                {
                    Plots.SetScottPlotTheme(isDark, true, AvaPlot1);
                    Plots.SetScottPlotTheme(isDark, false, AvaPlot2);
                }
            });
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
                lock (_plotLock)
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
                }
            });
        }

        private async void ImportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm)
            {
                await vm.ImportTableFromCsvAsync(this);
                Dispatcher.UIThread.Post(() =>
                {
                    if (vm.SelectedTab != null)
                    {
                        if (vm.SelectedTab != null && vm.SelectedTab.Plot != null && _polarAxis != null)
                        {
                            double[] angles = vm.SelectedTab.Plot.Angles;
                            double[] values = vm.IsPowerNormSelected ? vm.SelectedTab.Plot.PowerNormValues : vm.SelectedTab.Plot.VoltageNormValues;
                            bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                            DrawPolarPlot(angles, values, _polarAxis, AvaPlot1, vm.SelectedTab.DataScatters, vm.SelectedTab.Plot.ColorHex, vm.IsPowerNormSelected, isDark);
                        }
                    }
                });
            }
        }

        private void RequestAvaPlot2Refresh()
        {
            _needsAvaPlot2Refresh = true;
        }

    }
}