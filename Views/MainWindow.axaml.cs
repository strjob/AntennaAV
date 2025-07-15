using AntennaAV.Services;
using AntennaAV.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Linq;

namespace AntennaAV.Views
{
    public partial class MainWindow : Window
    {

        private bool isDark = false;

        private readonly PlotManager _plotManager = new PlotManager();


        // Магические числа вынесены в константы
        private const int DefaultSectorSize = 10;
        private const int DefaultSectorCenter = 0;
        private const double PointerThreshold = 20.0;
        private const int HeaderEditKeyEnter = (int)Key.Enter;

        private double _lastSnappedAngle;
        public MainWindow()
        {
            InitializeComponent();
            isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;

            _plotManager.InitializePlot1(AvaPlot1, isDark);
            _plotManager.InitializePlot2(AvaPlot2, isDark);
            this.Closing += MainWindow_Closing;
            this.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
           


            if (AvaPlot2 != null)
            {
                AvaPlot2.PointerMoved += AvaPlot2_PointerMoved;
                AvaPlot2.PointerPressed += AvaPlot2_PointerPressed;
            }

            //OnThemeChanged(this, EventArgs.Empty);

            this.GetObservable(Window.ClientSizeProperty).Subscribe(_ => _plotManager.ResetPlotAxes(AvaPlot1, AvaPlot2));
            NumericUpDownSectorSize.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            NumericUpDownSectorCenter.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm)
                {
                    SubscribeToViewModelEvents(vm);
                    vm.BuildRadar();
                    
                }
            };

            Application.Current!.ActualThemeVariantChanged += OnThemeChanged;

        }

        private void DrawCurrentTabPlot(MainWindowViewModel vm)
        {
            if (vm.SelectedTab == null || vm.SelectedTab.Plot == null)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                var values = vm.IsPowerNormSelected ? vm.SelectedTab.Plot.PowerNormValues : vm.SelectedTab.Plot.VoltageNormValues;
                if (AvaPlot1 != null)
                {
                    _plotManager.DrawPolarPlot(
                        vm.SelectedTab.Plot.Angles.ToArray(),
                        values.ToArray(),
                        AvaPlot1,
                        vm.SelectedTab.DataScatters,
                        vm.SelectedTab.Plot.ColorHex,
                        vm.IsPowerNormSelected,
                        isDark
                    );
                }
            });
        }

        private void DrawReceiverAngleArrow(double angleDeg)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _plotManager.CreateOrUpdateAngleArrow(AvaPlot1, angleDeg, true);
            });
        }


        private void AvaPlot2_PointerMoved(object? sender, PointerEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AvaPlot2 == null || AvaPlot2.Plot == null)
                    return;

                var point = e.GetPosition(AvaPlot2);

                // Радиус внешнего круга в пикселях (например, 90% от половины минимального размера)
                double plotRadiusPix = 0.6 * Math.Min(AvaPlot2.Bounds.Width, AvaPlot2.Bounds.Height) / 2.0;
                double threshold = PointerThreshold;
                double snappedAngle;
                _plotManager.UpdateHoverMarker(AvaPlot2, point.X, point.Y, plotRadiusPix, threshold, out snappedAngle);
                _lastSnappedAngle = snappedAngle;
            });
        }

        private void AvaPlot2_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_plotManager.IsHoverMarkerVisible())
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.OnTransmitterAngleSelected?.Invoke((360 - _lastSnappedAngle) % 360);
                }
            }

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
            if (e.Key == (Key)HeaderEditKeyEnter && sender is TextBox tb && tb.DataContext is TabViewModel vm)
                vm.IsEditingHeader = false;
        }

        private bool IsDescendantOfTextBox(object? source)
        {
            if (source is Control c)
            {
                Control? control = c;
                while (control != null)
                {
                    if (control is TextBox)
                        return true;
                    control = control.Parent as Control;
                }
            }
            return false;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsDescendantOfTextBox(e.Source))
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
                        numericUpDown.Value = DefaultSectorSize;
                        vm.SectorSize = DefaultSectorSize.ToString();
                    }
                    else if (numericUpDown.Name == "NumericUpDownSectorCenter")
                    {
                        numericUpDown.Value = DefaultSectorCenter;
                        vm.SectorCenter = DefaultSectorCenter.ToString();
                    }
                }
            }
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm)
            {
                vm.StopMessaging();
                System.Threading.Thread.Sleep(100);
                vm.StopAntennas();
            }
        }

        private void TogglePlotVisibility_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm && vm.SelectedTab != null && vm.SelectedTab.Plot != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    vm.SelectedTab.Plot.IsVisible = !vm.SelectedTab.Plot.IsVisible;
                    if (AvaPlot1 != null)
                    {
                        _plotManager.DrawAllVisiblePlots(
                            vm,
                            AvaPlot1,
                            vm.IsPowerNormSelected,
                            isDark
                        );
                    }
                });

            }
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {

            Dispatcher.UIThread.Post(() =>
            { 
                isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                _plotManager.ApplyThemeToMainPlot(
                    isDark,
                    AvaPlot1
                );

                _plotManager.ApplyThemeToPlot2(
                    isDark,
                    AvaPlot2
                );
            });
        }

        private void DrawTransmitterAnglePoint(double angleDeg)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AvaPlot2 != null)
                    _plotManager.DrawTransmitterAnglePoint(AvaPlot2, angleDeg);
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
                        if (vm.SelectedTab != null && vm.SelectedTab.Plot != null)
                        {
                            double[] angles = vm.SelectedTab.Plot.Angles.ToArray();
                            double[] values = (vm.IsPowerNormSelected ? vm.SelectedTab.Plot.PowerNormValues : vm.SelectedTab.Plot.VoltageNormValues).ToArray();
                            _plotManager.DrawPolarPlot(angles, values, AvaPlot1, vm.SelectedTab.DataScatters, vm.SelectedTab.Plot.ColorHex, vm.IsPowerNormSelected, isDark);
                        }
                    }
                });
            }
        }

        private async void ExportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                if (vm.SelectedTab != null)
                    await vm.ExportSelectedTabAsync(this);
        }

        private void SubscribeToViewModelEvents(MainWindowViewModel vm)
        {
            vm.OnBuildRadarPlot += (angles, values) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (vm.SelectedTab != null && AvaPlot1 != null)
                    {
                        var plot = AvaPlot1!;
                        _plotManager.DrawPolarPlot(angles, values, plot, vm.SelectedTab.DataScatters, vm.SelectedTab.Plot.ColorHex, vm.IsPowerNormSelected, isDark);
                    }
                });
            };

            vm.PropertyChanged += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
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
                        bool hasData = vm.Tabs.Any(tab => tab.Plot != null && tab.Plot.Angles.Length > 0);
                        if (AvaPlot1 != null)
                        {
                            if (hasData)
                            {
                                _plotManager.DrawAllVisiblePlots(
                                    vm,
                                    AvaPlot1,
                                    vm.IsPowerNormSelected,
                                    isDark
                                );
                            }
                            else
                            {
                                if (vm.IsPowerNormSelected)
                                {
                                    if (AvaPlot1 != null)
                                        _plotManager.UpdatePolarAxisCircles(AvaPlot1, true, -50, 0, isDark);
                                }
                                else
                                {
                                    if (AvaPlot1 != null)
                                        _plotManager.UpdatePolarAxisCircles(AvaPlot1, false, 0, 1, isDark);
                                }
                            }
                        }
                    }
                    else if (e.PropertyName == nameof(vm.TransmitterAngleDeg))
                    {
                        DrawTransmitterAnglePoint(vm.TransmitterAngleDeg);
                    }
                });
            };

            vm.OnBuildRadar += (from, to) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    double end = (from + 360) % 360;
                    double start = (to + 360) % 360;
                    if (AvaPlot1 != null)
                    {
                        _plotManager.CreateOrUpdateSectorPolygon(AvaPlot1, start, end, vm?.ShowSector ?? true);
                        _plotManager.MoveAngleArrowToFront(AvaPlot1);
                    }
                });
            };

            vm.ShowAntennaChanged += (show) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _plotManager.SetAngleArrowVisibility(show);
                });
            };

            vm.ShowSectorChanged += (show) =>
            {

                Dispatcher.UIThread.Post(() =>
                {
                    _plotManager.SetSectorVisibility(show);
                });

            };

            vm.Tabs.CollectionChanged += (s2, e2) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (AvaPlot1 != null)
                    {
                        _plotManager.DrawAllVisiblePlots(
                            vm,
                            AvaPlot1,
                            vm.IsPowerNormSelected,
                            isDark
                        );
                    }
                });
            };
            vm.RequestPlotRedraw += () =>
            {
                DrawCurrentTabPlot(vm);
            };
            vm.RequestClearCurrentPlot += () =>
            {
                ClearCurrentTabPlot(vm);
            };
            // Можно добавить другие подписки, если появятся
        }

        private void ClearCurrentTabPlot(MainWindowViewModel vm)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AvaPlot1 != null)
                    _plotManager.ClearCurrentTabPlot(vm, AvaPlot1);
            });
        }

    }
}