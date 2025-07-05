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
using Avalonia.Styling;

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
            // Удаляем старую ось, если есть (на старте не нужно, но для универсальности)
            if (_polarAxis != null)
            {
                AvaPlot1.Plot.Remove(_polarAxis);
                _polarAxis = null;
            }
            bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
            _polarAxis = Plots.Initialize(AvaPlot1, isDark);
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
                        // Сохраняем лимиты
                        var limits = AvaPlot1.Plot.Axes.GetLimits();

                        // Проверка на пустой массив и вывод длины
                        //System.Diagnostics.Debug.WriteLine($"OnBuildRadarPlot: values.Length={values?.Length ?? -1}");
                        if (values == null || values.Length == 0)
                            return;

                        // Разбиваем на сегменты по разрывам углов
                        List<List<Coordinates>> segments = new();
                        List<Coordinates> current = new();
                        double min = values.Min();
                        double max = values.Max();
                        double r_max = 100;
                        for (int i = 0; i < angles.Length; i++)
                        {
                            double mirroredAngle = (360 - angles[i]) % 360; // если нужно отзеркалить
                            double r = (max - min) > 0 ? r_max * (values[i] - min) / (max - min) : r_max;
                           // System.Diagnostics.Debug.WriteLine($"min={min}, max={max}, value={values[i]}, r={r}");
                            var pt = _polarAxis.GetCoordinates(r, mirroredAngle);
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
                                var color = ScottPlot.Color.FromHex(vm.SelectedTab?.Plot?.ColorHex ?? "#0000FF");
                                var scatter = AvaPlot1.Plot.Add.Scatter(seg, color: color);
                                scatter.MarkerSize = 0;
                                scatter.LineWidth = 2;
                                scatter.Smooth = true;
                                _dataScatters.Add(scatter);
                            }
                        }
                        // Восстанавливаем лимиты
                        AvaPlot1.Plot.Axes.SetLimits(limits);

                        // === Добавлено: обновление кругов полярной оси ===
                        bool isLogScale = vm.IsPowerNormSelected; 
                        Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, min, max, isDark);
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
                            DrawAllVisiblePlots();
                        }
                    };
                }
            };
            Application.Current!.ActualThemeVariantChanged += OnThemeChanged;
            SetScottPlotTheme(Application.Current!.ActualThemeVariant == ThemeVariant.Dark);
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
                double min = values.Min();
                double max = values.Max();
                double r_max = 100;
                List<ScottPlot.Coordinates> coords = new();
                for (int i = 0; i < angles.Length; i++)
                {
                    double mirroredAngle = (360 - angles[i]) % 360;
                    double r = (max - min) > 0 ? r_max * (values[i] - min) / (max - min) : r_max;
                    var pt = _polarAxis.GetCoordinates(r, mirroredAngle);
                    coords.Add(pt);
                }
                if (coords.Count > 1)
                {
                    var color = ScottPlot.Color.FromHex(tab.Plot.ColorHex);
                    var scatter = AvaPlot1.Plot.Add.Scatter(coords, color: color);
                    scatter.MarkerSize = 0;
                    scatter.LineWidth = 2;
                    _dataScatters.Add(scatter);
                    // === Обновление кругов ===
                    bool isLogScale = vm.IsPowerNormSelected;
                    bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                    Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, min, max, isDark);
                    AvaPlot1.Refresh();
                }
            }
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
            if (this.DataContext is not MainWindowViewModel vm)
                return;
            // Очистить старые графики
            foreach (var scatter in _dataScatters)
                AvaPlot1.Plot.Remove(scatter);
            _dataScatters.Clear();

            if (_polarAxis == null) return;
            bool isLogScale = vm.IsPowerNormSelected;
            double? globalMin = null, globalMax = null;
            // Сначала ищем общий min/max по всем видимым графикам
            foreach (var tab in vm.Tabs)
            {
                if (tab.Plot != null && tab.Plot.IsVisible)
                {
                    double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                    if (values.Length > 1)
                    {
                        double min = values.Min();
                        double max = values.Max();
                        globalMin = globalMin.HasValue ? Math.Min(globalMin.Value, min) : min;
                        globalMax = globalMax.HasValue ? Math.Max(globalMax.Value, max) : max;
                    }
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
                    double[] angles = tab.Plot.Angles;
                    double[] values = isLogScale ? tab.Plot.PowerNormValues : tab.Plot.VoltageNormValues;
                    if (angles.Length == 0 || values.Length == 0 || angles.Length != values.Length)
                        continue;
                    double r_max = 100;
                    List<ScottPlot.Coordinates> coords = new();
                    for (int i = 0; i < angles.Length; i++)
                    {
                        double mirroredAngle = (360 - angles[i]) % 360;
                        double r = (globalMax.Value - globalMin.Value) > 0 ? r_max * (values[i] - globalMin.Value) / (globalMax.Value - globalMin.Value) : r_max;
                        var pt = _polarAxis.GetCoordinates(r, mirroredAngle);
                        coords.Add(pt);
                    }
                    if (coords.Count > 1)
                    {
                        var color = ScottPlot.Color.FromHex(tab.Plot.ColorHex);
                        var scatter = AvaPlot1.Plot.Add.Scatter(coords, color: color);
                        scatter.MarkerSize = 0;
                        scatter.LineWidth = 2;
                        _dataScatters.Add(scatter);
                    }
                }
            }
            // Обновляем круги по общему диапазону
            bool isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
            Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, globalMin.Value, globalMax.Value, isDark);
            AvaPlot1.Refresh();
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            var isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
            SetScottPlotTheme(isDark);
            // Удаляем старую полярную ось и создаём новую с нужной темой
            if (_polarAxis != null)
            {
                AvaPlot1.Plot.Remove(_polarAxis);
                _polarAxis = null;
            }
            if (this.DataContext is MainWindowViewModel vm)
            {
                _polarAxis = Plots.Initialize(AvaPlot1, isDark);
                bool isLogScale = vm.IsPowerNormSelected;
                Plots.AutoUpdatePolarAxisCircles(AvaPlot1, _polarAxis, isLogScale, -50, 0, isDark);
            }
        }

        private void SetScottPlotTheme(bool isDark)
        {
            if (AvaPlot1?.Plot != null)
            {
                if (isDark)
                {
                    AvaPlot1.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
                    AvaPlot1.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
                    AvaPlot1.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
                    AvaPlot1.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
                    AvaPlot1.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
                    AvaPlot1.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
                    AvaPlot1.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");
                    AvaPlot1.Plot.Add.Palette = new ScottPlot.Palettes.Penumbra();
                }
                else
                {
                    AvaPlot1.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                    AvaPlot1.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#ffffff");
                    AvaPlot1.Plot.Axes.Color(ScottPlot.Color.FromHex("#222222"));
                    AvaPlot1.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#e5e5e5");
                    AvaPlot1.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#f0f0f0");
                    AvaPlot1.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#222222");
                    AvaPlot1.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#222222");
                    AvaPlot1.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
                }
                AvaPlot1.Refresh();
            }
        }
    }
}