using AntennaAV.Services;
using AntennaAV.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering;
using Avalonia.Styling;
using Avalonia.Threading;
using HarfBuzzSharp;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace AntennaAV.Views
{
    public partial class MainWindow : Window
    {

        private bool isDark = false;
        private bool moveToZero = false;

        private readonly PlotManagerMain _plotManagerMain = new PlotManagerMain();
        private readonly PlotManagerSmall _plotManagerSmall = new PlotManagerSmall();

        // Магические числа вынесены в константы
        private const int DefaultSectorSize = 10;
        private const int DefaultSectorCenter = 0;
        private const double PointerThreshold = 20.0;
        private const int HeaderEditKeyEnter = (int)Key.Enter;

        private double _lastSnappedAngleTx;
        private double _lastSnappedAngleRx;

        public MainWindow()
        {
            InitializeComponent();
            isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
            _plotManagerMain.InitializePlotMain(AvaPlotMain, isDark);
            _plotManagerSmall.InitializeSmallPlots(AvaPlotTx, AvaPlotRx, isDark);

            this.Closing += MainWindow_Closing;
            this.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
           


            if (AvaPlotTx != null)
            {
                AvaPlotTx.PointerMoved += AvaPlotTx_PointerMoved;
                AvaPlotTx.PointerPressed += AvaPlotTx_PointerPressed;
                AvaPlotTx.PointerExited += (sender, e) => _plotManagerSmall.SetTxHoverMarkerVisibility(false);
            }
            if (AvaPlotRx != null)
            {
                AvaPlotRx.PointerMoved += AvaPlotRx_PointerMoved;
                AvaPlotRx.PointerPressed += AvaPlotRx_PointerPressed;
                AvaPlotRx.PointerExited += (sender, e) => _plotManagerSmall.SetRxHoverMarkerVisibility(false);

            }

            AvaPlotMain.SizeChanged += AvaPlotMain_SizeChanged;


            this.GetObservable(Window.ClientSizeProperty).Subscribe(_ =>
            {
                _plotManagerSmall.ResetPlotAxes();
                //_plotManagerMain.ResetPlotAxes();
            });

            NumericUpDownSectorSize.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            NumericUpDownSectorCenter.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            NumericUpDownManualLimit.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            NumericUpDownAutoLimit.AddHandler(InputElement.KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);



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
        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.IsVisible = !SettingsOverlay.IsVisible;
        }

        private void AvaPlotMain_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            _plotManagerMain.ResetPlotAxes();
        }


        private void AvaPlotTx_PointerMoved(object? sender, PointerEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AvaPlotTx == null || AvaPlotTx.Plot == null)
                    return;

                var point = e.GetPosition(AvaPlotTx);

                double plotRadiusPix = 0.6 * Math.Min(AvaPlotTx.Bounds.Width, AvaPlotTx.Bounds.Height) / 2.0;
                double threshold = PointerThreshold;
                double snappedAngle;
                _plotManagerSmall.UpdateHoverMarkerTx(AvaPlotTx, point.X, point.Y, plotRadiusPix, threshold, out snappedAngle);
                _lastSnappedAngleTx = snappedAngle;
            });
        }

        private void AvaPlotTx_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_plotManagerSmall.IsHoverMarkerTxVisible())
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.OnTransmitterAngleSelected?.Invoke((360 - _lastSnappedAngleTx) % 360);
                }
            }
        }

        private void AvaPlotRx_PointerMoved(object? sender, PointerEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AvaPlotRx == null || AvaPlotRx.Plot == null)
                    return;

                var point = e.GetPosition(AvaPlotRx);

                double plotRadiusPix = 0.6 * Math.Min(AvaPlotRx.Bounds.Width, AvaPlotRx.Bounds.Height) / 2.0;
                double threshold = PointerThreshold;
                double snappedAngle;
                _plotManagerSmall.UpdateHoverMarkerRx(AvaPlotRx, point.X, point.Y, plotRadiusPix, threshold, out snappedAngle);
                _lastSnappedAngleRx = snappedAngle;
            });
        }

        private void AvaPlotRx_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_plotManagerSmall.IsHoverMarkerRxVisible())
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.OnReceiverAngleSelected?.Invoke((360 - _lastSnappedAngleRx) % 360);
                }
            }
        }
        //private void Header_DoubleTapped(object sender, RoutedEventArgs e)
        //{
        //    if (sender is TextBlock tb && tb.DataContext is TabViewModel vm)
        //        vm.IsEditingHeader = true;
        //}
        private void Header_DoubleTapped(object sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.DataContext is TabViewModel vm)
            {
                vm.IsEditingHeader = true;
            }
        }

        private void HeaderEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is TabViewModel vm)
            {
                vm.IsEditingHeader = false;
                if (DataContext is MainWindowViewModel mainVm)
                {
                    var limitValue = mainVm.IsAutoscale ? mainVm.AutoscaleLimitValue : mainVm.ManualScaleValue;
                    RefreshAllPlots(mainVm.Tabs, mainVm.IsPowerNormSelected, isDark, limitValue);
                }
            }
                
        }

        private void HeaderEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == (Key)HeaderEditKeyEnter && sender is TextBox tb && tb.DataContext is TabViewModel vm)
            { 
                vm.IsEditingHeader = false;
                if (DataContext is MainWindowViewModel mainVm)
                {
                    var limitValue = mainVm.IsAutoscale ? mainVm.AutoscaleLimitValue : mainVm.ManualScaleValue;
                    RefreshAllPlots(mainVm.Tabs, mainVm.IsPowerNormSelected, isDark, limitValue);
                }
            }

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
                    bool wasEditing = false;
                    foreach (var tab in vm.Tabs)
                    {
                        if (tab.IsEditingHeader)
                        {
                            wasEditing = true;
                            tab.IsEditingHeader = false;
                        }
                            
                    }
                    if (wasEditing)
                    {
                        var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
                        RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
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
                Debug.WriteLine("🔄 Начинается закрытие приложения...");
                
                vm.StopMessaging();
                System.Threading.Thread.Sleep(100);
                vm.StopAntennas();
                if(moveToZero)
                {
                    vm.MoveAntennasToZero();
                }
                
                // Сохраняем настройки при закрытии
                Debug.WriteLine("💾 Сохраняем настройки при закрытии...");
                vm.SaveSettings();
                Debug.WriteLine("✅ Настройки сохранены");
            }
        }

        //private void TogglePlotVisibility_Click(object? sender, RoutedEventArgs e)
        //{
        //    if (this.DataContext is MainWindowViewModel vm && vm.SelectedTab != null && vm.SelectedTab.Plot != null)
        //    {
        //        var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
        //        Dispatcher.UIThread.Post(() =>
        //        {
        //            vm.SelectedTab.Plot.IsVisible = !vm.SelectedTab.Plot.IsVisible;
        //            RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
        //        });

        //    }
        //}

        private void TogglePlotVisibility_Click(object? sender, RoutedEventArgs e)
        {
            if (this.DataContext is MainWindowViewModel vm && vm.SelectedTab != null && vm.SelectedTab.Plot != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (vm.SelectedTab.Plot.IsVisible)
                    {
                        // Скрываем график
                        _plotManagerMain.HidePlotAndRecalculateRange(vm.SelectedTab, vm.Tabs);
                    }
                    else
                    {
                        // Показываем график
                        vm.SelectedTab.Plot.IsVisible = true;
                        var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
                        RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
                    }
                });
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {

            Dispatcher.UIThread.Post(() =>
            { 
                isDark = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
                _plotManagerMain.ApplyThemeToMainPlot(isDark, AvaPlotMain);
                _plotManagerSmall.ApplyThemeToPlotTx(AvaPlotTx, isDark);
                _plotManagerSmall.ApplyThemeToPlotRx(AvaPlotRx, isDark);
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
                            _plotManagerMain.DrawPolarPlot(
                                vm.Tabs,
                                vm.SelectedTab,
                                vm.SelectedTab.Header // label для легенды
                            );
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

        private async void SavePngButton_Click(object? sender, RoutedEventArgs e)
        {
            // Сохраняем график в PNG через PlotManager
            var window = this;
            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Сохранить график как PNG",
                SuggestedFileName = "plot.png",
                FileTypeChoices = new System.Collections.Generic.List<Avalonia.Platform.Storage.FilePickerFileType>
                {
                    new("PNG файл") { Patterns = new[] { "*.png" } }
                },
                DefaultExtension = "png"
            });
            if (file is null)
                return; // пользователь отменил
            await _plotManagerMain.SaveMainPlotToPngAsync(file.Path.LocalPath, isDark);
        }

        private void RefreshAllPlots(IEnumerable<TabViewModel> tabs, bool isPowerNormSelected, bool isDark, double? limit)
        {
            
            bool hasData = tabs.Any(tab => tab.Plot != null && tab.Plot.Angles.Length > 0 && tab.Plot.IsVisible);
            double limitValue;
            if (limit.HasValue)
                limitValue = limit.Value;
            else
                limitValue = -50;


            if (AvaPlotMain != null)
            {
                if (hasData)
                {
                    _plotManagerMain.RefreshAllVisiblePlots(tabs);
                }
                else
                {
                    if (isPowerNormSelected)
                        _plotManagerMain.UpdatePolarAxisCircles(AvaPlotMain, isPowerNormSelected, limitValue, 0, isDark);
                    else
                        _plotManagerMain.UpdatePolarAxisCircles(AvaPlotMain, isPowerNormSelected, 0, 1, isDark);
                }
            }
        }

        private void SubscribeToViewModelEvents(MainWindowViewModel vm)
        {

            vm.OnBuildRadar += (from, to) =>
            {
                Dispatcher.UIThread.Post(() => _plotManagerMain.CreateOrUpdateSectorPolygon(AvaPlotMain, to, from, vm?.ShowSector ?? true));
            };


            vm.DataFlowStatusChanged += status =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (status.Contains("обмен данными"))
                    {
                        _plotManagerSmall.DrawTransmitterAnglePoint(AvaPlotTx, vm.TransmitterAngleDeg);
                        _plotManagerSmall.DrawReceiverAnglePoint(AvaPlotRx, vm.ReceiverAngleDeg);
                        _plotManagerMain.CreateOrUpdateAngleArrow(AvaPlotMain, vm.ReceiverAngleDeg);
                    }
                });
            };
            vm.IsPowerNormSelectedChanged += isPowerNorm =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _plotManagerMain.SetScaleMode(isPowerNorm);
                    var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
                    RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
                });
                
                
                
            };
            vm.IsAutoscaleChanged += (isAutoscale, ManualScaleValue, AutoscaleLimitValue) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                     var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
                    _plotManagerMain.UpdateScaleMode(isAutoscale);
                    _plotManagerMain.SetAutoMinLimit(true, AutoscaleLimitValue!.Value);
                    _plotManagerMain.SetManualRange(ManualScaleValue!.Value, 0);
                });
            };


            vm.TransmitterAngleDegChanged += angle =>
            {
                Dispatcher.UIThread.Post(() => _plotManagerSmall.DrawTransmitterAnglePoint(AvaPlotTx, angle));
            };

            vm.ShowAntennaChanged += value =>
            {
                Dispatcher.UIThread.Post(() => _plotManagerMain.SetAngleArrowVisibility(value));
            };

            vm.ManualScaleValueChanged += value =>
            {
                Dispatcher.UIThread.Post(() => _plotManagerMain.SetManualRange(value!.Value, 0));
            };

            vm.AutoscaleLimitValueChanged += value =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _plotManagerMain.SetAutoMinLimit(true, value!.Value);
                    var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
                    RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
                });                
            };

            vm.AutoscaleMinValueChanged += value =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
                    _plotManagerMain.SetAutoscaleMinValue(value!.Value);
                    RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
                });
            };

            vm.ShowSectorChanged += value =>
            {
                Dispatcher.UIThread.Post(() => _plotManagerMain.SetSectorVisibility(value));
            };
            vm.ShowLegendChanged += value =>
            {
                Dispatcher.UIThread.Post(() => _plotManagerMain.SetLegendVisibility(value));
            };

            vm.MoveToZeroOnCloseChanged += value =>
            {
                Dispatcher.UIThread.Post(() => moveToZero = value);
            };

            vm.ReceiverAngleDegChanged += angle =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _plotManagerSmall.DrawReceiverAnglePoint(AvaPlotRx, angle);
                    _plotManagerMain.CreateOrUpdateAngleArrow(AvaPlotMain, angle);
                });
            };

            vm.RequestPlotRedraw += () =>
            {
                if (vm.SelectedTab == null || vm.SelectedTab.Plot == null || vm.SelectedTab.Plot.Angles == null || vm.SelectedTab.Plot.Angles.Length == 0)
                    return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (AvaPlotMain != null)
                    {
                        _plotManagerMain.DrawPolarPlot(
                            vm.Tabs,
                            vm.SelectedTab,
                            vm.SelectedTab.Header 
                        );
                    }
                });
            };

            vm.RequestClearCurrentPlot += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (AvaPlotMain != null && vm.SelectedTab != null)
                    {
                        var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue : vm.ManualScaleValue;
                        _plotManagerMain.ClearCurrentTabPlot(vm.SelectedTab, AvaPlotMain);
                        RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
                    }                     
                });
            };

            vm.RequestMinMaxReset += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (AvaPlotMain != null && vm.SelectedTab != null)
                    {
                        _plotManagerMain.ClearCurrentTabPlot(vm.SelectedTab, AvaPlotMain);
                        _plotManagerMain.ResetGlobalRange();
                        _plotManagerMain.RecalculateGlobalRange(vm.Tabs);
                    }

                });
            };

            vm.RequestDeleteCurrentPlot += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var limitValue = vm.IsAutoscale ? vm.AutoscaleLimitValue :vm.ManualScaleValue;
                    if (AvaPlotMain != null && vm.SelectedTab != null)
                        _plotManagerMain.ClearCurrentTabPlot(vm.SelectedTab, AvaPlotMain);
                    RefreshAllPlots(vm.Tabs, vm.IsPowerNormSelected, isDark, limitValue);
                    vm.RemoveTabInternal();
                });
            };
        }

        private void CalibrationButton_Click(object? sender, RoutedEventArgs e)
        {
            var mainWindowViewModel = DataContext as MainWindowViewModel;
            if (mainWindowViewModel != null && mainWindowViewModel._comPortService != null)
            {
                var calibrationWindow = new CalibrationWindow
                {
                    DataContext = new CalibrationWindowViewModel(mainWindowViewModel._comPortService)
                };
                calibrationWindow.ShowDialog(this);
                // Do NOT change MainWindow's DataContext!
            }
            else
            {
                throw new InvalidOperationException("MainWindowViewModel or _comPortService is null.");
            }
        }

    }
}