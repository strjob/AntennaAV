using AntennaAV.Helpers;
using AntennaAV.Models;
using AntennaAV.Services;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HarfBuzzSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AntennaAV.Services.ComPortManager;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AntennaAV.ViewModels
{
    // Главная ViewModel для основного окна приложения AntennaAV
    // Управляет данными антенн, построением диаграмм направленности, интерфейсом пользователя
    // Обеспечивает связь между View и моделями данных через MVVM паттерн
    public partial class MainWindowViewModel : ViewModelBase
    {
        // 1. приватные поля
        private int? _firstSystick = null;
        private DateTime _lastDataReceivedTime = DateTime.MinValue;
        private readonly DispatcherTimer _uiTimer = new();
        private DispatcherTimer? _tableUpdateTimer;
        private AntennaDiagramCollector _collector = new();
        private bool _isDiagramDataCollecting = false;
        private double _acquisitionFrom;
        private double _acquisitionTo;
        private CancellationTokenSource? _acquisitionCts;
        private bool _isReconnecting = false;
        private bool _isFinalizingDiagram = false;
        private readonly object _dataLock = new();
        private readonly CsvService _csvService = new CsvService();
        private readonly ISettingsService _settingsService = new SettingsService();

        // 2. ObservableProperty
        [ObservableProperty] private string connectionStatus = "⏳ Не подключено";
        [ObservableProperty] private string dataFlowStatus = "🔴 Нет данных";
        [ObservableProperty] private double receiverAngleDeg;
        [ObservableProperty] private bool isGenOn;
        [ObservableProperty] private double transmitterAngleDeg;
        [ObservableProperty] private double powerDbm;
        [ObservableProperty] private double voltage;
        [ObservableProperty] private int deviceMode;
        [ObservableProperty] private string deviceModeStr = "-"; 
        [ObservableProperty] private double rxAntennaCounter = 0;
        [ObservableProperty] private double txAntennaCounter = 0;
        [ObservableProperty] private bool isDiagramAcquisitionRunning;
        [ObservableProperty] private bool isPortOpen;
        [ObservableProperty] private string voltageHeaderStr = "Напряжение: ";
        [ObservableProperty] private string sectorSize = "180";
        [ObservableProperty] private string sectorCenter = "0";
        [ObservableProperty] private bool showAntenna = true;
        [ObservableProperty] private bool showSector = true;
        [ObservableProperty] private bool isPowerNormSelected = true;
        [ObservableProperty] private bool isAutoscale = true;
        [ObservableProperty] private bool isRealtimeMode = true;
        [ObservableProperty] private bool showLegend = true;
        [ObservableProperty] private bool moveToZeroOnClose = false;
        [ObservableProperty] private bool showMarkers = false;
        [ObservableProperty] private string transmitterAngle = "0";
        [ObservableProperty] private int? manualScaleValue = -30;
        [ObservableProperty] private double? autoscaleLimitValue = -50;
        [ObservableProperty] private double? autoscaleMinValue = 1;
        [ObservableProperty] private string transmitterAngleError = "";
        [ObservableProperty] private string receiverAngle = "0";
        [ObservableProperty] private string receiverAngleError = "";
        [ObservableProperty] private string txAntennaCounterErrorStr = "";
        [ObservableProperty] private string rxAntennaCounterErrorStr = "";
        [ObservableProperty] private bool isDarkTheme;
        [ObservableProperty] private string lastEvent = "";
        [ObservableProperty] private string handModeErrorStr = "";
        


        // 3. Публичные свойства
        public readonly IComPortService _comPortService;
        public TabManager TabManager { get; } = new TabManager();
        public ObservableCollection<TabViewModel> Tabs => TabManager.Tabs;
        public int SelectedTabIndex { get => TabManager.SelectedTabIndex; set { TabManager.SelectedTabIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedTab)); } }
        public TabViewModel? SelectedTab => TabManager.SelectedTab;
        public bool HasTabs => TabManager.HasTabs;
        public bool CanRemoveTab => TabManager.CanRemoveTab;
        public bool CanRemoveTabWhenPortOpen => CanRemoveTab && CanUseWhenPortOpen;
        public bool CanUseWhenPortOpen => !IsDiagramAcquisitionRunning && IsPortOpen;
        public bool CanUseWhenHasTabs => !IsDiagramAcquisitionRunning && HasTabs;
        public string ChevronRightIconPath => IsDarkTheme ? "/Assets/chevron-right-dark.svg" : "/Assets/chevron-right-light.svg";
        public string ChevronLeftIconPath => IsDarkTheme ? "/Assets/chevron-left-dark.svg" : "/Assets/chevron-left-light.svg";
        public string ChevronsRightIconPath => IsDarkTheme ? "/Assets/chevrons-right-dark.svg" : "/Assets/chevrons-right-light.svg";
        public string ChevronsLeftIconPath => IsDarkTheme ? "/Assets/chevrons-left-dark.svg" : "/Assets/chevrons-left-light.svg";
        public double MovePlus1 => 1;
        public double MoveMinus1 => -1;
        public double MovePlus01 => 0.1;
        public double MoveMinus01 => -0.1;


        // 4. События
        public event Action<double, double>? OnBuildRadar;
        public Action<double>? OnTransmitterAngleSelected;
        public Action<double>? OnReceiverAngleSelected;
        public event Action<bool>? ShowAntennaChanged;
        public event Action<bool>? ShowSectorChanged;
        public event Action? RequestPlotRedraw;
        public event Action? RequestClearCurrentPlot;
        public event Action? RequestDeleteCurrentPlot;
        public event Action? RequestMinMaxReset;
        public event Action<double>? ReceiverAngleDegChanged;
        public event Action<string>? DataFlowStatusChanged;
        public event Action<bool>? IsPowerNormSelectedChanged;
        public event Action<bool, int?, double?>? IsAutoscaleChanged;
        public event Action<bool>? ShowLegendChanged;
        public event Action<bool>? MoveToZeroOnCloseChanged;
        public event Action<double>? TransmitterAngleDegChanged; 
        public event Action<int?>? ManualScaleValueChanged;
        public event Action<double?>? AutoscaleLimitValueChanged;
        public event Action<double?>? AutoscaleMinValueChanged;

        // 5. RelayCommand
        // Строит полярную диаграмму на основе настроек размера и центра сектора
        public void BuildRadar()
        {
            if (double.TryParse(SectorSize, out var size) && double.TryParse(SectorCenter, out var center))
            {
                // Вычисляем from и to из размера и центра сектора
                var (to, from) = AngleUtils.CalculateSectorRange(size, center);
                OnBuildRadar?.Invoke(from, to);
            }
        }

        // Добавляет новую вкладку для отображения данных диаграммы
        [RelayCommand] private void AddTab() => TabManager.AddTab();

        // Удаляет выбранную вкладку и очищает связанные с ней данные графика
        [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
        private void RemoveTab()
        {
            if(SelectedTab != null)
            {
                SelectedTab.Plot.Angles = Array.Empty<double>();
                SelectedTab.Plot.PowerNormValues = Array.Empty<double>();
                SelectedTab.Plot.VoltageNormValues = Array.Empty<double>();
            }
            RequestDeleteCurrentPlot?.Invoke();
        }

        // Устанавливает размер сектора в 120 градусов с центром в 0
        [RelayCommand]
        public void Set120Degrees()
        {
            SectorSize = "120";
            SectorCenter = "0";
        }

        // Устанавливает размер сектора в 180 градусов с центром в 0
        [RelayCommand]
        public void Set180Degrees()
        {
            SectorSize = "180";
            SectorCenter = "0";
        }

        // Устанавливает размер сектора в 360 градусов (полный круг) с центром в 0
        [RelayCommand]
        public void Set360Degrees()
        {
            SectorSize = "360";
            SectorCenter = "0";
        }

        // Экспортирует данные выбранной вкладки в CSV файл
        [RelayCommand]
        public async Task ExportSelectedTabAsync(Window window)
        {
            if (SelectedTab is null || window is null)
                return;
            try
            {
                bool result = await _csvService.ExportTabAsync(SelectedTab, window);
                LastEvent = result ? $"✅ Файл сохранён: {SelectedTab.Header}" : "Ошибка экспорта";
            }
            catch (Exception ex)
            {
                LastEvent = $"Ошибка экспорта: {ex.Message}";
            }
        }

        // Импортирует данные из CSV файла в текущую вкладку
        [RelayCommand]
        public async Task ImportTableFromCsvAsync(Window window)
        {
            if (SelectedTab is null || window is null)
                return;
            try
            {
                var newRows = await _csvService.ImportTableAsync(window);
                if (newRows != null)
                {
                    SelectedTab.ClearTableData();
                    SelectedTab.AddAntennaData(newRows.ToArray());
                    UpdatePlotFromTable(); // строим график по таблице
                    LastEvent = $"✅ Импортировано строк: {newRows.Count}";
                }
            }
            catch (Exception ex)
            {
                LastEvent = $"Ошибка импорта: {ex.Message}";
            }
        }
        [RelayCommand] public void CalibrateZeroSvch() => _comPortService.CalibrateZeroSVCH();

        // Устанавливает состояние генератора (включен/выключен)
        [RelayCommand] public void SetGenState(bool setGenOff) => _comPortService.SetGenState(setGenOff);

        // Переключает состояние генератора
        [RelayCommand]
        public void ToggleGenerator()
        {
            SetGenState(IsGenOn); 
        }

        // Сбрасывает счетчик приемной антенны в ноль
        [RelayCommand] public void ResetRxAntennaCounter() => SetAntennaAngle("0", "R", "Z");

        // Перемещает передающую антенну к указанному углу
        [RelayCommand] public void MoveTransmitterToAngle() => SetAntennaAngle(TransmitterAngle, "T", "G");

        // Устанавливает угол передающей антенны напрямую
        [RelayCommand] public void SetTransmitterAngle() => SetAntennaAngle(TransmitterAngle, "T", "S");

        // Перемещает приемную антенну к указанному углу
        [RelayCommand] public void MoveReceiverToAngle() => SetAntennaAngle(ReceiverAngle, "R", "G");

        // Устанавливает угол приемной антенны напрямую  
        [RelayCommand] public void SetReceiverAngle() => SetAntennaAngle(ReceiverAngle, "R", "S");

        // Останавливает движение указанной антенны
        [RelayCommand] public void StopAntenna(string antenna) => _comPortService.StopAntenna(antenna);

        // Перемещает передающую антенну на относительный угол от текущей позиции
        [RelayCommand] public void MoveTxAntennaToRelativeAngle(double angle) => _comPortService.SetAntennaAngle(AngleUtils.NormalizeAngle(angle + TransmitterAngleDeg), "T", "G");

        // Перемещает приемную антенну на относительный угол от текущей позиции
        [RelayCommand] public void MoveRxAntennaToRelativeAngle(double angle) => _comPortService.SetAntennaAngle(AngleUtils.NormalizeAngle(angle + ReceiverAngleDeg), "R", "G");

        // Запускает автоматический сбор диаграммы направленности антенны
        [RelayCommand]
        public async Task StartDiagramAcquisition()
        {
            if (double.TryParse(SectorSize, out var size) && double.TryParse(SectorCenter, out var center))
            {
                // Вычисляем from и to из размера и центра сектора
                var (from, to) = AngleUtils.CalculateSectorRange(size, center);

                _acquisitionCts = new CancellationTokenSource();
                try
                {
                    await StartDiagramAcquisitionAsync(from, to, _acquisitionCts.Token);
                }
                catch (TaskCanceledException)
                {
                    // Операция была отменена пользователем - это нормально
                }
                catch (OperationCanceledException)
                {
                    // Операция была отменена - это нормально
                }
                finally
                {
                    _acquisitionCts?.Dispose();
                    _acquisitionCts = null;
                }
            }
        }

        // Отменяет текущий процесс сбора диаграммы направленности
        [RelayCommand]
        public void CancelDiagramAcquisition()
        {
            _acquisitionCts?.Cancel();
            _acquisitionCts?.Dispose();
            _acquisitionCts = null;
            _isDiagramDataCollecting = false;
            _comPortService.StopAntenna("R");
        }

        // Очищает все данные в таблице и на графике текущей вкладки
        [RelayCommand]
        public void ClearTable()
        {
            lock (_dataLock)
            {
                if (SelectedTab != null)
                {
                    SelectedTab.ClearTableData();
                    // Очистить данные графика
                    SelectedTab.Plot.Angles = Array.Empty<double>();
                    SelectedTab.Plot.PowerNormValues = Array.Empty<double>();
                    SelectedTab.Plot.VoltageNormValues = Array.Empty<double>();
                }
                // Сообщить View, чтобы график исчез
                RequestClearCurrentPlot?.Invoke();
            }
        }

        // 6. Публичные методы
        

        public void RemoveTabInternal()
        {
            TabManager.RemoveTab();
        }

        // Вспомогательные методы
        public bool GenToggleState
        {
            get => IsGenOn;
            set
            {
                if (value != IsGenOn)
                {
                    ToggleGenerator();
                }
            }
        }

        partial void OnIsGenOnChanged(bool value)
        {
            OnPropertyChanged(nameof(GenToggleState));
        }
        private async Task WaitStartAngleAsync(double start, CancellationToken cancellationToken)
        {
            while (AngleUtils.AngleDiff(ReceiverAngleDeg, start) > 1.0)
            {
                await Task.Delay(50, cancellationToken);
            }
            LastEvent = $"✅ Достигнута начальная точка: {ReceiverAngleDeg:F1}°";
            await Task.Delay(500, cancellationToken);
        }

        // Инициализирует процесс сбора данных диаграммы направленности
        private void StartDataCollection()
        {
            _isDiagramDataCollecting = true;
            LastEvent = "Идет сбор данных";
            RequestMinMaxReset?.Invoke();
            _collector.Reset();
            StartTableUpdateTimer();
        }

        // Ожидает начала движения антенны с таймаутом
        private async Task WaitForMovementStartAsync(double initialAngle, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            while (AngleUtils.AngleDiff(initialAngle, ReceiverAngleDeg) < 0.5)
            {
                await Task.Delay(10, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine("❌ Отмена при ожидании начальной точки");
                    throw new TaskCanceledException();
                }
                if (stopwatch.Elapsed.TotalSeconds > 2)
                {
                    LastEvent = "Антенна не начала движение. Отмена";
                    _acquisitionCts?.Cancel();
                    throw new OperationCanceledException("Антенна не начала движение");
                }
            }
        }

        // Ожидает достижения конечного угла измерения
        private async Task WaitForEndAngleAsync(double overshootEnd, CancellationToken cancellationToken)
        {
            while (AngleUtils.AngleDiff(ReceiverAngleDeg, overshootEnd) > 1.0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }
                if (!_comPortService.IsOpen)
                {
                    ConnectionStatus = "⚠ Связь потеряна во время снятия диаграммы";
                    throw new OperationCanceledException("Связь потеряна");
                }
                await Task.Delay(10, cancellationToken);
            }
        }

        // Останавливает обмен сообщениями с COM портом
        public void StopMessaging()
        {
            _comPortService.StopMessaging();
        }

        // Останавливает движение всех антенн
        public void StopAntennas()
        {
            _comPortService.StopAntenna("T");
            Thread.Sleep(100);
            _comPortService.StopAntenna("R");
        }

        public void MoveAntennasToZero()
        {

            _comPortService.SetAntennaAngle(0.0, "R", "Z");
            Thread.Sleep(10);
            _comPortService.SetAntennaAngle(0.0, "T", "Z");
            Thread.Sleep(10);
            _comPortService.SetAntennaAngle(0.0, "R", "G");
            Thread.Sleep(10);
            _comPortService.SetAntennaAngle(0.0, "T", "G");

        }

        // 7. Приватные методы
        private bool CanEditOrDelete() => SelectedTab != null;

        // Асинхронно подключается к COM порту устройства
        private async Task ConnectToPortAsync()
        {
            var result = await Task.Run(() => _comPortService.AutoDetectAndConnect());
            IsPortOpen = _comPortService.IsOpen;
            ConnectionStatus = result switch
            {
                ConnectResult.Success => "🟢 Устройство подключено",
                ConnectResult.PortNotFound => "❌ Порт не найден",
                ConnectResult.DeviceNotResponding => "🔴 Устройство не найдено",
                ConnectResult.InvalidResponse => "⚠ Некорректный ответ",
                ConnectResult.PortBusy => "⚠ Порт занят другим процессом",
                ConnectResult.ExceptionOccurred => "💥 Ошибка при подключении",
                _ => "❓ Неизвестный результат"
            };
            if (result == ConnectResult.Success)
                _comPortService.StartReading();
        }

        // Выполняет полный цикл автоматического сбора диаграммы направленности
        private async Task StartDiagramAcquisitionAsync(double from, double to, CancellationToken cancellationToken)
        {
            try
            {
                if (IsDiagramAcquisitionRunning) return;
                
                IsDiagramAcquisitionRunning = true;
                _isDiagramDataCollecting = false;

                if (SelectedTab != null && !SelectedTab.Plot.IsVisible)
                    SelectedTab.Plot.IsVisible = true;

                double currentAngle = ReceiverAngleDeg;
                double currentCounter = RxAntennaCounter;

                (double startAngleOvershoot, double stopAngleOvershoot, string direction, bool isFullCircle) = AngleUtils.DetermineStartEndDir(currentAngle, from, to, currentCounter);
                _acquisitionFrom = from;
                _acquisitionTo = to;

                if (isFullCircle)
                {
                    StartDataCollection();
                    _comPortService.SetAntennaAngle(currentAngle, "R", direction);
                    await WaitForMovementStartAsync(ReceiverAngleDeg, cancellationToken);
                    await Task.Delay(2000, cancellationToken);
                }
                else
                {
                    _comPortService.SetAntennaAngle(startAngleOvershoot, "R", "G");
                    LastEvent = $"Ожидание начальной точки: {startAngleOvershoot:F1}°";
                    await WaitStartAngleAsync(startAngleOvershoot, cancellationToken);
                    StartDataCollection();
                }

                _comPortService.SetAntennaAngle(stopAngleOvershoot, "R", direction);
                await WaitForEndAngleAsync(stopAngleOvershoot, cancellationToken);
                LastEvent = "✅ Построение диаграммы завершено";
            }
            catch (TaskCanceledException)
            {
                LastEvent = "❌ Операция была отменена пользователем";
            }
            catch (OperationCanceledException)
            {
                LastEvent = "❌ Операция была отменена";
            }
            catch (Exception ex)
            {
                LastEvent = $"Ошибка сбора диаграммы: {ex.Message}";
            }
            finally
            {
                _isDiagramDataCollecting = false;
                IsDiagramAcquisitionRunning = false;
                _comPortService.StopAntenna("R");
                _isFinalizingDiagram = true;
                UpdateTable();
                _isFinalizingDiagram = false;
                StopTableUpdateTimer();
                RequestPlotRedraw?.Invoke();
                
            }
        }

        // Обработчик тика UI таймера для обновления интерфейса
        private void OnUiTimerTick()
        {
            ProcessComPortData();
            TxAntennaCounterErrorStr = CheckAntennaCounter(TxAntennaCounter);
            RxAntennaCounterErrorStr = CheckAntennaCounter(RxAntennaCounter);
            bool isDataFlow = (DateTime.Now - _lastDataReceivedTime) < TimeSpan.FromSeconds(1);
            UpdateDataFlowStatus(isDataFlow);
            HandleReconnection();
            CheckForPlotRedraw();
        }

        // Запускает таймер обновления таблицы данных
        private void StartTableUpdateTimer()
        {
            if (_tableUpdateTimer == null)
            {
                _tableUpdateTimer = new DispatcherTimer();
                _tableUpdateTimer.Interval = TimeSpan.FromMilliseconds(Constants.TableTimerUpdateIntervalMs);
                _tableUpdateTimer.Tick += (s, e) => UpdateTable();
            }
            _tableUpdateTimer.Start();
        }

        // Останавливает таймер обновления таблицы
        private void StopTableUpdateTimer()
        {
            _tableUpdateTimer?.Stop();
        }

        // Обновляет данные таблицы и график в реальном времени
        private void UpdateTable()
        {
            lock (_dataLock)
            {
                if (_isFinalizingDiagram || !IsRealtimeMode || SelectedTab == null)
                    return;

                //_collector.FinalizeData();

                var plotSw = System.Diagnostics.Stopwatch.StartNew();
                if (RequestPlotRedraw != null)
                {
                    SelectedTab.Plot.Angles = _collector.GetGraphAngles();
                    SelectedTab.Plot.PowerNormValues = _collector.GetGraphValues(d => d.PowerNorm);
                    SelectedTab.Plot.VoltageNormValues = _collector.GetGraphValues(d => d.VoltageNorm);
                    RequestPlotRedraw.Invoke();
                }
                var newData = _collector.GetTableData();
                SelectedTab.AntennaDataCollection.ReplaceRange(newData);
            }
        }

        // Устанавливает угол антенны с проверкой валидности данных
        private void SetAntennaAngle(string angleStr, string antennaType, string command)
        {
            if (string.IsNullOrWhiteSpace(angleStr) || !_comPortService.IsOpen) return;
            if (double.TryParse(angleStr, out var angle) && angle >= 0 && angle <= Constants.MaxAngleInput)
            {
                _comPortService.SetAntennaAngle(angle, antennaType, command);
            }
        }

        // Обрабатывает входящие данные от COM порта и обновляет свойства
        private void ProcessComPortData()
        {
            bool dataReceived = false;
            if (_comPortService.IsOpen && !Design.IsDesignMode)
            {
                AntennaData? lastData = null;
                if (_isDiagramDataCollecting)
                {
                    while (_comPortService.TryDequeue(out var data))
                    {
                        if (data == null) continue;
                        double angle = data.ReceiverAngleDeg;
                        if (AngleUtils.IsAngleInRange(angle, _acquisitionFrom, _acquisitionTo))
                        {
                            if (_firstSystick == null)
                                _firstSystick = data.Systick;

                            int deltaMs = data.Systick - _firstSystick.Value;
                            DateTime timestamp = DateTime.MinValue.AddMilliseconds(deltaMs);
                            _collector.AddPoint(data.ReceiverAngleDeg10, data.PowerDbm, timestamp);
                        }
                        lastData = data;
                        dataReceived = true;
                    }
                }
                else
                {
                    _firstSystick = null;
                    while (_comPortService.TryDequeue(out var data))
                    {
                        lastData = data;
                        dataReceived = true;
                    }
                }
                
                if (lastData != null)
                {
                    ReceiverAngleDeg = lastData.ReceiverAngleDeg;
                    TransmitterAngleDeg = lastData.TransmitterAngleDeg;
                    PowerDbm = lastData.PowerDbm;
                    DeviceMode = lastData.AntennaType - 4;
                    RxAntennaCounter = Math.Round(lastData.RxAntennaCounter / 10.0, 1);
                    TxAntennaCounter = Math.Round(lastData.TxAntennaCounter/10.0, 1);
                    
                    if (DeviceModeStr.Contains("СВЧ"))
                    {
                        Voltage = lastData.Voltage;
                        VoltageHeaderStr = "Напряжение, мкВ: ";
                    }
                    else if(DeviceModeStr.Contains("УКВ"))
                    {
                        Voltage = AntennaDiagramCollector.CalculateVoltageFromDBm(PowerDbm);
                        VoltageHeaderStr = "Напряжение, мВ: "; 
                    }
                    else
                    {
                        Voltage = -1; 
                    }

                    HandModeErrorStr = lastData.ModeAutoHand switch
                    {
                        0 => "🔴 Установка находится в ручном режиме",
                        _ => ""
                    };

                    IsGenOn = lastData.GenOnOff switch
                    {
                        0 => true,
                        _ => false
                    };
                }
                
                if (dataReceived)
                {
                    _lastDataReceivedTime = DateTime.Now;
                }
            }
        }

        // Проверяет счетчик антенны на предмет защиты от перекручивания кабеля
        private string CheckAntennaCounter(double antennaCounter)
        {
            return Math.Abs(antennaCounter) >= Constants.MaxAntennaCounter
                ? "Защита от перекручивания кабеля"
                : string.Empty;
        }

        // Обновляет статус потока данных
        private void UpdateDataFlowStatus(bool dataReceived)
        {
            if (dataReceived)
            {
                DataFlowStatus = "🟢 Идет обмен данными";
            }
            else
            {
                DataFlowStatus = "🔴 Нет данных";
                if (_comPortService.IsOpen)
                {
                    _comPortService.StartMessaging();
                }
            }
        }

        // Обрабатывает автоматическое переподключение при потере связи
        private void HandleReconnection()
        {
            if (!_comPortService.IsOpen && !_isReconnecting)
            {
                _isReconnecting = true;
                _ = Task.Run(async () =>
                {
                    await ConnectToPortAsync();
                    _isReconnecting = false;
                });
            }
        }

        // Проверяет необходимость перерисовки графика
        private void CheckForPlotRedraw()
        {
            bool needRedraw = false;
            foreach (var tab in Tabs)
            {
                if (tab.IsPlotColorChanged)
                {
                    tab.IsPlotColorChanged = false;
                    needRedraw = true;
                }
            }
            if (needRedraw)
            {
                RequestPlotRedraw?.Invoke();
            }
        }

        // Строит график по данным из таблицы (AntennaDataCollection), а не из _collector
        private void UpdatePlotFromTable()
        {
            lock (_dataLock)
            {
                if (SelectedTab != null)
                {
                    var data = SelectedTab.AntennaDataCollection;
                    SelectedTab.Plot.Angles = data.Select(d => d.Angle).ToArray();
                    SelectedTab.Plot.PowerNormValues = data.Select(d => d.PowerNorm).ToArray();
                    SelectedTab.Plot.VoltageNormValues = data.Select(d => d.VoltageNorm).ToArray();
                    RequestPlotRedraw?.Invoke();
                }
            }
        }

        // 8. partial-методы
        partial void OnIsDiagramAcquisitionRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanUseWhenPortOpen));
            OnPropertyChanged(nameof(CanUseWhenHasTabs));
            OnPropertyChanged(nameof(CanRemoveTabWhenPortOpen));
        }
        partial void OnIsPortOpenChanged(bool value)
        {
            OnPropertyChanged(nameof(CanUseWhenPortOpen));
            OnPropertyChanged(nameof(CanRemoveTab));
            OnPropertyChanged(nameof(CanRemoveTabWhenPortOpen));
        }
        partial void OnSectorSizeChanged(string value)
        {
            // Проверяем на пустые значения
            if (string.IsNullOrWhiteSpace(value))
            {
                // Если значение пустое, устанавливаем минимальное значение
                SectorSize = Constants.MinSectorSize.ToString();
                return;
            }
            if (double.TryParse(value, out double d))
            {
                // Проверяем границы
                if (d < Constants.MinSectorSize)
                {
                    SectorSize = Constants.MinSectorSize.ToString();
                    return;
                }
                else if (d > 360)
                {
                    SectorSize = "360";
                    return;
                }
                // Если значение в допустимых пределах, обновляем график
                BuildRadar();
            }
            else
            {
                // Если не удалось распарсить число, устанавливаем минимальное значение
                SectorSize = Constants.MinSectorSize.ToString();
            }
        }
        partial void OnSectorCenterChanged(string value)
        {
            // Проверяем на пустые значения
            if (string.IsNullOrWhiteSpace(value))
            {
                // Если значение пустое, устанавливаем 0
                SectorCenter = "0";
                return;
            }

            if (double.TryParse(value, out double d))
            {
                if (d < 0) SectorCenter = "355";
                else if (d > 359) SectorCenter = "0";
                else BuildRadar();
            }
            else
            {
                // Если не удалось распарсить число, устанавливаем 0
                SectorCenter = "0";
            }
        }

        partial void OnDeviceModeChanged(int value)
        {
            DeviceModeStr = value switch
            {
                0 => "УКВ СИНХР.",
                1 => "УКВ",
                2 => "СВЧ СИНХР.",
                3 => "СВЧ",
                _ => "-"
            };
        }
        partial void OnManualScaleValueChanged(int? value)
        {
            if(value != null)
                ManualScaleValueChanged?.Invoke(value.Value);
        }

        partial void OnAutoscaleLimitValueChanged(double? value)
        {
            if (value != null)
                AutoscaleLimitValueChanged?.Invoke(value.Value);
        }
        partial void OnAutoscaleMinValueChanged(double? value)
        {
            if (value != null)
                AutoscaleMinValueChanged?.Invoke(value.Value);
        }
        partial void OnShowAntennaChanged(bool value)
        {
            ShowAntennaChanged?.Invoke(value);
        }
        partial void OnShowSectorChanged(bool value)
        {
            ShowSectorChanged?.Invoke(value);
        }
        partial void OnTransmitterAngleChanged(string value)
        {
            TransmitterAngleError = AngleUtils.ValidateAngle(value, out _);
        }
        
        partial void OnReceiverAngleChanged(string value)
        {
            ReceiverAngleError = AngleUtils.ValidateAngle(value, out _);
        }
        partial void OnIsDarkThemeChanged(bool value)
        {
            ((App)Avalonia.Application.Current!).SetTheme(
                value ? ThemeVariant.Dark : ThemeVariant.Light);
            OnPropertyChanged(nameof(ChevronRightIconPath));
            OnPropertyChanged(nameof(ChevronLeftIconPath));
            OnPropertyChanged(nameof(ChevronsRightIconPath));
            OnPropertyChanged(nameof(ChevronsLeftIconPath));
        }
        partial void OnReceiverAngleDegChanged(double value)
        {
            ReceiverAngleDegChanged?.Invoke(value);
        }
        partial void OnDataFlowStatusChanged(string value)
        {
            DataFlowStatusChanged?.Invoke(value);
        }
        partial void OnIsPowerNormSelectedChanged(bool value)
        {
            IsPowerNormSelectedChanged?.Invoke(value);
        }
        partial void OnIsAutoscaleChanged(bool value)
        {
            if(ManualScaleValue.HasValue && AutoscaleLimitValue.HasValue)
                IsAutoscaleChanged?.Invoke(value, ManualScaleValue.Value, AutoscaleLimitValue.Value);
        }
        partial void OnTransmitterAngleDegChanged(double value)
        {
            TransmitterAngleDegChanged?.Invoke(value);
        }
        partial void OnShowLegendChanged(bool value)
        {
            ShowLegendChanged?.Invoke(value);
        }
        partial void OnMoveToZeroOnCloseChanged(bool value)
        {
            MoveToZeroOnCloseChanged?.Invoke(value);
        }


        // 9. Конструктор
        public MainWindowViewModel()
            : this(Design.IsDesignMode ? new MockComPortService() : throw new InvalidOperationException("Этот конструктор используется только в дизайнере"))
        {
            if (Design.IsDesignMode)
            {
                ReceiverAngleDeg = 123.4;
                TransmitterAngleDeg = 234.5;
                PowerDbm = -30.1;
                RxAntennaCounter = 7;
                DeviceModeStr = "СВЧ";
            }
        }

        // Основной конструктор с dependency injection COM порта
        public MainWindowViewModel(IComPortService comPortService)
        {
            _comPortService = comPortService;

            // Подписка на события изменения коллекции вкладок
            Tabs.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasTabs));
                OnPropertyChanged(nameof(CanRemoveTab));
                OnPropertyChanged(nameof(CanRemoveTabWhenPortOpen));
            };

            // Подписка на события изменения TabManager
            TabManager.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TabManager.SelectedTabIndex))
                    OnPropertyChanged(nameof(SelectedTabIndex));
                if (e.PropertyName == nameof(TabManager.SelectedTab))
                    OnPropertyChanged(nameof(SelectedTab));
                if (e.PropertyName == nameof(TabManager.HasTabs))
                    OnPropertyChanged(nameof(HasTabs));
                if (e.PropertyName == nameof(TabManager.CanRemoveTab))
                    OnPropertyChanged(nameof(CanRemoveTab));
            };

            AddTab();
            _ = ConnectToPortAsync();

            // Инициализация UI таймера
            _uiTimer.Interval = TimeSpan.FromMilliseconds(Constants.UiTimerUpdateIntervalMs);
            _uiTimer.Tick += (_, _) => OnUiTimerTick();
            _uiTimer.Start();

            // Подписка на события выбора углов антенн
            OnTransmitterAngleSelected += angle => _comPortService.SetAntennaAngle(angle, "T", "G");
            OnReceiverAngleSelected += angle => _comPortService.SetAntennaAngle(angle, "R", "G");
        }

        // Методы для работы с настройками
        public void LoadSettings()
        {
            var settings = _settingsService.LoadSettings();
            IsDarkTheme = settings.IsDarkTheme;
            ShowLegend = settings.ShowLegend;
            IsAutoscale = settings.IsAutoscale;
            MoveToZeroOnClose = settings.MoveToZeroOnClose;
            ManualScaleValue = settings.ManualScaleValue;
            AutoscaleLimitValue = settings.AutoscaleLimitValue;
            AutoscaleMinValue = settings.AutoscaleMinValue;
            
            //Debug.WriteLine($"🎨 Загружены настройки: тема={IsDarkTheme}, легенда={ShowLegend}, автоподбор={IsAutoscale}, перемещение в 0°={MoveToZeroOnClose}");
            
            // Применяем загруженную тему к приложению
            ((App)Avalonia.Application.Current!).SetTheme(
                IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light);
        }

        public void SaveSettings()
        {
            var settings = new AppSettings
            {
                IsDarkTheme = IsDarkTheme,
                ShowLegend = ShowLegend,
                IsAutoscale = IsAutoscale,
                MoveToZeroOnClose = MoveToZeroOnClose,
                ManualScaleValue = ManualScaleValue,
                AutoscaleLimitValue = AutoscaleLimitValue,
                AutoscaleMinValue = AutoscaleMinValue
            };
            //Debug.WriteLine($"💾 Сохраняются настройки: тема={IsDarkTheme}, легенда={ShowLegend}, автоподбор={IsAutoscale}, перемещение в 0°={MoveToZeroOnClose}");
            _settingsService.SaveSettings(settings);
        }
    }
}