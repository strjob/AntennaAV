using AntennaAV.Models;
using AntennaAV.Services;
using AntennaAV.Views;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AntennaAV.Services.ComPortManager;
using static System.Runtime.InteropServices.JavaScript.JSType;



namespace AntennaAV.ViewModels
{

    public partial class MainWindowViewModel : ViewModelBase
    {
        // Константы
        private const int MinSectorSize = 10;
        private const int MaxTabCount = 10;

        // Приватные поля
        private readonly IComPortService _comPortService;
        private double powerDbm;
        private int antennaType;
        private int rxAntennaCounter;
        private int? _firstSystick = null;
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
        private IEnumerable<string> _availablePorts = Array.Empty<string>();

        public MainWindowViewModel()
    : this(Design.IsDesignMode ? new MockComPortService() : throw new InvalidOperationException("Этот конструктор используется только в дизайнере"))
        {
            if (Design.IsDesignMode)
            {
                ReceiverAngleDeg = 123.4;
                TransmitterAngleDeg = 234.5;
                PowerDbm = -30.1;
                AntennaType = 2;
                RxAntennaCounter = 7;
            }
        }


        partial void OnSelectedTabChanged(TabViewModel? oldValue, TabViewModel? newValue);

        public bool HasTabs => Tabs.Count > 0;
        public bool CanRemoveTab => Tabs.Count > 1;
        public bool CanRemoveTabWhenPortOpen => CanRemoveTab && CanUseWhenPortOpen;

        [ObservableProperty]
        private ObservableCollection<TabViewModel> tabs = new();

        [ObservableProperty]
        private int selectedTabIndex;

        [ObservableProperty]
        private string connectionStatus = "⏳ Не подключено";

        [ObservableProperty]
        private double receiverAngleDeg;
        [ObservableProperty]
        private string receiverAngleDegStr = string.Empty;

        [ObservableProperty]
        private double transmitterAngleDeg;

        [ObservableProperty]
        private string transmitterAngleDegStr = string.Empty;

        public double PowerDbm { get => powerDbm; set => powerDbm = value; }
        [ObservableProperty]
        private string powerDbmStr = string.Empty;

        public int AntennaType { get => antennaType; set => antennaType = value; }
        [ObservableProperty]
        private string antennaTypeStr = string.Empty;

        public int RxAntennaCounter { get => rxAntennaCounter; set => rxAntennaCounter = value; }
        public int TxAntennaCounter { get => rxAntennaCounter; set => rxAntennaCounter = value; }
        [ObservableProperty]
        private string rxAntennaCounterStr = string.Empty;

        [ObservableProperty]
        private string txAntennaCounterStr = string.Empty;


        //public DateTime Timestamp { get => timestamp; set => timestamp = value; }
        
        //[ObservableProperty]
        //private string timestampStr = string.Empty;

        [ObservableProperty]
        private bool isDiagramAcquisitionRunning;

        [ObservableProperty]
        private bool isPortOpen;


        public bool CanUseWhenPortOpen => !IsDiagramAcquisitionRunning && IsPortOpen;
        public bool CanUseWhenHasTabs => !IsDiagramAcquisitionRunning && HasTabs;

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

        [ObservableProperty]
        private string sectorSize = "180";

        [ObservableProperty]
        private string sectorCenter = "0";

        [ObservableProperty]
        private bool showAntenna = true;

        [ObservableProperty]
        private bool showSector = true;

        [ObservableProperty]
        private bool isPowerNormSelected = true;

        [ObservableProperty]
        private bool isRealtimeMode = true;

        [ObservableProperty]
        private string transmitterMoveAngle = "0";

        [ObservableProperty]
        private string? transmitterMoveAngleError;

        //[ObservableProperty]
        //private string transmitterSetAngle = "0";

        //[ObservableProperty]
        //private string transmitterSetAngleError = "";

        [ObservableProperty]
        private string receiverMoveAngle = "0";

        [ObservableProperty]
        private string receiverMoveAngleError = "";

        [ObservableProperty]
        private string receiverSetAngle = "0";

        [ObservableProperty]
        private string receiverSetAngleError = "";

        [ObservableProperty]
        private string txAntennaCounterErrorStr = "";
        // private string transmitterAngle2Error = "";

        partial void OnSectorSizeChanged(string value)
        {
            // Проверяем на пустые значения
            if (string.IsNullOrWhiteSpace(value))
            {
                // Если значение пустое, устанавливаем минимальное значение
                SectorSize = MinSectorSize.ToString();
                return;
            }

            if (double.TryParse(value, out double d))
            {
                // Проверяем границы
                if (d < MinSectorSize)
                {
                    // Если значение меньше 10, устанавливаем 10 и выходим
                    // Это вызовет повторный вызов OnSectorSizeChanged с новым значением
                    SectorSize = MinSectorSize.ToString();
                    return;
                }
                else if (d > 360)
                {
                    // Если значение больше 360, устанавливаем 360 и выходим
                    SectorSize = "360";
                    return;
                }

                // Если значение в допустимых пределах, обновляем график
                BuildRadar();
            }
            else
            {
                // Если не удалось распарсить число, устанавливаем минимальное значение
                SectorSize = MinSectorSize.ToString();
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

        partial void OnShowAntennaChanged(bool value)
        {
            ShowAntennaChanged?.Invoke(value);
        }

        partial void OnShowSectorChanged(bool value)
        {
            ShowSectorChanged?.Invoke(value);
        }

        [RelayCommand]
        public void BuildRadar()
        {
            // SectorSize и SectorCenter уже содержат введённые пользователем значения
            // Можно преобразовать в число:
            if (double.TryParse(SectorSize, out var size) && double.TryParse(SectorCenter, out var center))
            {
                // Вычисляем from и to из размера и центра сектора
                var (to, from) = CalculateSectorRange(size, center);
                OnBuildRadar?.Invoke(from, to);
            }
        }

        public event Action<double, double>? OnBuildRadar;

        public Action<double>? OnTransmitterAngleSelected;

        public event Action<double[], double[]>? OnBuildRadarPlot;


        private static readonly string[] DefaultColors = new[]
        {
            "#E60000", // Красный
            "#00E600", // Зелёный
            "#0000E6", // Синий
            "#E69400", // Оранжевый
            "#800080", // Фиолетовый
            "#00FFFF", // Голубой
            "#FFC0CB", // Розовый
            "#E6E600", // Жёлтый
            // ... можно добавить больше цветов
        };

        [RelayCommand]
        private void AddTab()
        {
            if (Tabs.Count >= MaxTabCount)
                return;
            int colorIndex = Tabs.Count % DefaultColors.Length;
            var tab = new TabViewModel { Header = $"Антенна {Tabs.Count + 1}" };
            tab.Plot.ColorHex = DefaultColors[colorIndex];
            tab.AddAntennaData(new List<GridAntennaData>());
            Tabs.Add(tab);
            SelectedTabIndex = Tabs.Count - 1;
            UpdateTabCommands();
        }




        public async Task ExportSelectedTabAsync(Window window)
        {
            try
            {
                if (SelectedTab is null || window is null)
                    return;

                var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Сохранить таблицу",
                    SuggestedFileName = $"Вкладка_{SelectedTab.Header}.csv",
                    FileTypeChoices = new List<FilePickerFileType>
            {
                new("CSV файл") { Patterns = new[] { "*.csv" } }
            },
                    DefaultExtension = "csv"
                });

                if (file is null)
                    return; // пользователь отменил

                var sb = new StringBuilder();
                sb.AppendLine("Angle,PowerDbm,Voltage,PowerNorm,VoltageNorm,Time");

                foreach (var row in SelectedTab.AntennaDataCollection.ToArray())
                {
                    sb.AppendLine($"{row.AngleStr},{row.PowerDbmStr},{row.VoltageStr},{row.PowerNormStr},{row.VoltageNormStr},{row.TimeStr}");
                }

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(sb.ToString());

                LastEvent = $"✅ Файл сохранён: {file.Name}";
            }
            catch (Exception ex)
            {
                LastEvent = $"Ошибка экспорта: {ex.Message}";
            }
        }
        
        public async Task ImportTableFromCsvAsync(Window window)
        {
            try
            {
                if (SelectedTab is null || window is null)
                    return;

                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Загрузить таблицу из CSV",
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("CSV файл") { Patterns = new[] { "*.csv" } }
                    }
                });

                var file = files?.FirstOrDefault();
                if (file is null)
                    return;

                var newRows = new List<GridAntennaData>();
                using (var stream = await file.OpenReadAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string? line;
                    bool isFirst = true;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (isFirst)
                        {
                            isFirst = false; // пропускаем заголовок
                            continue;
                        }
                        var parts = line.Split(',');
                        if (parts.Length < 6) continue;
                        if (!double.TryParse(parts[0], out var angle)) continue;
                        if (!double.TryParse(parts[1], out var powerDbm)) continue;
                        if (!double.TryParse(parts[2], out var voltage)) continue;
                        if (!double.TryParse(parts[3], out var powerNorm)) continue;
                        if (!double.TryParse(parts[4], out var voltageNorm)) continue;
                        if (!DateTime.TryParse(parts[5], out var time)) time = DateTime.Now;
                        newRows.Add(new GridAntennaData
                        {
                            Angle = angle,
                            PowerDbm = powerDbm,
                            Voltage = voltage,
                            PowerNorm = powerNorm,
                            VoltageNorm = voltageNorm,
                            Time = time
                        });
                    }
                }
                SelectedTab.ClearTableData();
                SelectedTab.AddAntennaData(newRows.ToArray());
                // Формируем PlotData по новым данным
                var anglesArr = newRows.Select(x => x.Angle).ToArray();
                var powerNormArr = newRows.Select(x => x.PowerNorm).ToArray();
                var voltageNormArr = newRows.Select(x => x.VoltageNorm).ToArray();
                SelectedTab.Plot.Angles = anglesArr;
                SelectedTab.Plot.PowerNormValues = powerNormArr;
                SelectedTab.Plot.VoltageNormValues = voltageNormArr;
                // Обновить график по новым данным
                OnPropertyChanged(nameof(SelectedTabIndex));
                LastEvent = $"✅ Таблица загружена: {file.Name}";
            }
            catch (Exception ex)
            {
                LastEvent = $"Ошибка импорта: {ex.Message}";
            }
        }

       

        private void UpdateTabCommands()
        {
            OnPropertyChanged(nameof(CanRemoveTab));
            OnPropertyChanged(nameof(CanRemoveTabWhenPortOpen));
            RemoveTabCommand.NotifyCanExecuteChanged();
        }


        public TabViewModel? SelectedTab => Tabs.ElementAtOrDefault(SelectedTabIndex);



        [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
        private void RemoveTab()
        {
            if (Tabs.Count == 1)
                return;
            if (SelectedTab is not null)
            {
                Tabs.Remove(SelectedTab);
                if (SelectedTabIndex >= Tabs.Count)
                    SelectedTabIndex = Tabs.Count - 1;
            }
            UpdateTabCommands();
        }

        private bool CanEditOrDelete() => SelectedTab != null;

        public MainWindowViewModel(IComPortService comPortService)
        {
            _comPortService = comPortService;
            _availablePorts = _comPortService.GetAvailablePortNames();

            // Синхронизация состояния переключателя с реальной темой
            var actualTheme = Avalonia.Application.Current?.ActualThemeVariant;
            IsDarkTheme = actualTheme == Avalonia.Styling.ThemeVariant.Dark;

            Tabs.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasTabs));
                OnPropertyChanged(nameof(CanRemoveTab));
                OnPropertyChanged(nameof(CanRemoveTabWhenPortOpen));
            };

            AddTab();

            


            _ = ConnectToPortAsync();

            _uiTimer.Interval = TimeSpan.FromMilliseconds(100);
            _uiTimer.Tick += (_, _) => OnUiTimerTick();
            _uiTimer.Start();


            OnTransmitterAngleSelected += angle =>
            {
                _comPortService.SetAntennaAngle(angle, "T", "G");
            };
        }



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




        private void OnUiTimerTick()
        {
            bool dataReceived = false;
            bool needRedraw = false;

            
            
            if (_comPortService.IsOpen && !Design.IsDesignMode)
            {
                AntennaData? lastData = null;
                if (_isDiagramDataCollecting)
                {
                    while (_comPortService.TryDequeue(out var data))
                    {
                        if (data == null) continue;
                        double angle = data.ReceiverAngleDeg;
                        if (IsAngleInRange(angle, _acquisitionFrom, _acquisitionTo))
                        {
                            //_collector.AddPoint(data.ReceiverAngleDeg10, data.PowerDbm, data.Timestamp);
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
                    ReceiverAngleDegStr = ReceiverAngleDeg.ToString("F1");
                    TransmitterAngleDeg = lastData.TransmitterAngleDeg;
                    TransmitterAngleDegStr = TransmitterAngleDeg.ToString("F1") + "°";
                    PowerDbm = lastData.PowerDbm;
                    PowerDbmStr = PowerDbm.ToString("F2");
                    AntennaType = lastData.AntennaType;
                    AntennaTypeStr = AntennaType.ToString();
                    RxAntennaCounter = lastData.RxAntennaCounter;
                    TxAntennaCounter = lastData.TxAntennaCounter;
                    TxAntennaCounterStr = TxAntennaCounter.ToString();
                    //RxAntennaCounterStr = RxAntennaCounter.ToString();
                    //Timestamp = lastData.Timestamp;
                    //TimestampStr = Timestamp.ToString("HH:mm:ss.fff");
                }

                if (Math.Abs(TxAntennaCounter) >= 5400)
                {
                    TxAntennaCounterErrorStr = "Защита от перекручивания кабеля";
                }
                else
                {
                    TxAntennaCounterErrorStr = "";
                }

            }
            // Статус потока данных: только по текущему срабатыванию таймера
            if (dataReceived)
            {
                DataFlowStatus = "🟢 Данные идут";
            }
            else
            {
                DataFlowStatus = "🔴 Нет данных";
                if (_comPortService.IsOpen)
                {
                    _comPortService.StartMessaging();
                }
            }
            // Если связь потеряна и не идёт попытка переподключения
            if (!_comPortService.IsOpen && !_isReconnecting)
            {
                _isReconnecting = true;
                _ = Task.Run(async () =>
                {
                    await ConnectToPortAsync();
                    _isReconnecting = false;
                });
            }

            foreach (var tab in Tabs)
            {
                if (tab.IsPlotColorDirty)
                {
                    tab.IsPlotColorDirty = false;
                    needRedraw = true;
                }
            }
            if (needRedraw)
                RequestPlotRedraw?.Invoke();
        }
        [RelayCommand]
        public void Set120Degrees()
        {
            SectorSize = "120";
            SectorCenter = "0";
        }

        [RelayCommand]
        public void Set180Degrees()
        {
            SectorSize = "180";
            SectorCenter = "0";
        }

        [RelayCommand]
        public void Set360Degrees()
        {
            SectorSize = "360";
            SectorCenter = "0";
        }

        partial void OnTransmitterMoveAngleChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                TransmitterMoveAngleError = "Поле не может быть пустым";
            else if (!double.TryParse(value, out var d) || d < 0 || d > 359.9)
                TransmitterMoveAngleError = "Введите число от 0 до 359.9";
            else
                TransmitterMoveAngleError = "";
        }

        [RelayCommand]
        public void MoveTransmitterToAngle()
        {
            if (string.IsNullOrWhiteSpace(TransmitterMoveAngle) || !string.IsNullOrEmpty(TransmitterMoveAngleError) || !_comPortService.IsOpen)
                return;
            if (double.TryParse(TransmitterMoveAngle, out var angle) && angle >= 0 && angle <= 360)
            {
                _comPortService.SetAntennaAngle(angle, "T", "G");
            }
        }
        /*
        partial void OnTransmitterSetAngleChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                TransmitterSetAngleError = "Поле не может быть пустым";
            else if (!double.TryParse(value, out var d) || d < 0 || d > 359.9)
                TransmitterSetAngleError = "Введите число от 0 до 359.9";
            else
                TransmitterSetAngleError = "";
        }
        */
        [RelayCommand]
        public void SetTransmitterAngle()
        {
            if (string.IsNullOrWhiteSpace(TransmitterMoveAngle) || !string.IsNullOrEmpty(TransmitterMoveAngleError) || !_comPortService.IsOpen)
                return;
            if (double.TryParse(TransmitterMoveAngle, out var angle) && angle >= 0 && angle <= 360)
            {
                _comPortService.SetAntennaAngle(angle, "T", "S");
            }
        }

        partial void OnReceiverSetAngleChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                ReceiverSetAngleError = "Поле не может быть пустым";
            else if (!double.TryParse(value, out var d) || d < 0 || d > 359.9)
                ReceiverSetAngleError = "Введите число от 0 до 359.9";
            else
                ReceiverSetAngleError = "";
        }

        public void AddPointForWarmup(int angle, double powerDbm, DateTime time)
        {
            _collector.AddPoint(angle, powerDbm, time);
        }



        [RelayCommand]
        public async Task StartDiagramAcquisition()
        {
            if (double.TryParse(SectorSize, out var size) && double.TryParse(SectorCenter, out var center))
            {
                // Вычисляем from и to из размера и центра сектора
                var (from, to) = CalculateSectorRange(size, center);

                if (Math.Abs(from - to) < 0.1)
                {
                    from = ReceiverAngleDeg;
                    to = from;
                }

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

        private (double from, double to) CalculateSectorRange(double size, double center)
        {
            // Нормализуем центр к диапазону [0, 360)
            center = center % 360.0;
            if (center < 0) center += 360.0;

            // Вычисляем половину размера сектора
            double halfSize = size / 2.0;

            // Вычисляем начальный и конечный углы
            double from = center + halfSize;
            double to = center - halfSize;

            // Нормализуем углы к диапазону [0, 360)
            from = from % 360.0;
            if (from < 0) from += 360.0;

            to = to % 360.0;
            if (to < 0) to += 360.0;

            return (from, to);
        }



        [RelayCommand]
        public void CancelDiagramAcquisition()
        {
            _acquisitionCts?.Cancel();
            _acquisitionCts?.Dispose();
            _acquisitionCts = null;
            _isDiagramDataCollecting = false;
            _collector.FinalizeData();
            UpdatePlotWithNormalizedData();
            _comPortService.StopAntenna("R");
        }

        public async Task StartDiagramAcquisitionAsync(double from, double to, CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine($"Начинаем сбор диаграммы: размер сектора = {to - from:F1}°, центр = {(from + to) / 2:F1}°");

                if (IsDiagramAcquisitionRunning)
                {
                    Debug.WriteLine("❌ Диаграмма уже запущена, выход");
                    return;
                }

                IsDiagramAcquisitionRunning = true;
                _isDiagramDataCollecting = false;
                if (SelectedTab != null && !SelectedTab.Plot.IsVisible)
                    SelectedTab.Plot.IsVisible = true;
                // Определяем текущее положение антенны
                double currentAngle = ReceiverAngleDeg;
                int currentCounter = RxAntennaCounter;
                Debug.WriteLine($"Текущее положение: угол={currentAngle:F1}°, counter={currentCounter}");

                // Выбираем начальную точку (ближайшую к текущему положению)
                double startAngle, endAngle;
                if (AngleDiff(currentAngle, from) <= AngleDiff(currentAngle, to))
                {
                    startAngle = from;
                    endAngle = to;
                    Debug.WriteLine($"Выбрана начальная точка: start={startAngle:F1}° (ближе к текущему)");
                }
                else
                {
                    startAngle = to;
                    endAngle = from;
                    Debug.WriteLine($"Выбрана начальная точка: start={startAngle:F1}° (дальше от текущего)");
                }


                // Устанавливаем параметры сбора
                _acquisitionFrom = from;
                _acquisitionTo = to;

                // Двигаемся к начальной точке с overshoot
                double overshootStart;

                if (Math.Abs(from - to) < 0.1)
                {
                    overshootStart = startAngle;
                }
                else
                {
                    if (IsAngleInRange(startAngle + 1, from, to))
                        overshootStart = startAngle - 2;
                    else
                        overshootStart = startAngle + 2;
                }

                overshootStart = (overshootStart + 360.0) % 360.0;



                Debug.WriteLine($"Движение к начальной точке: {overshootStart:F1}° (overshoot -3°)");
                _comPortService.SetAntennaAngle(overshootStart, "R", "G");

                int waitCount = 0;
                while (Math.Abs(ReceiverAngleDeg - overshootStart) > 1.0)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.WriteLine("❌ Отмена при ожидании начальной точки");
                        return;
                    }
                    await Task.Delay(50, cancellationToken);
                    waitCount++;
                    if (waitCount % 20 == 0) // Логируем каждую секунду
                    {
                        Debug.WriteLine($"Ожидание начальной точки: текущий угол={ReceiverAngleDeg:F1}°, цель={overshootStart:F1}°, разность={Math.Abs(ReceiverAngleDeg - overshootStart):F1}°");
                    }
                }
                Debug.WriteLine($"✅ Достигнута начальная точка: {ReceiverAngleDeg:F1}°");
                await Task.Delay(500, cancellationToken);

                double tempRxAntennaAngle = ReceiverAngleDeg;

                _isDiagramDataCollecting = true;
                Debug.WriteLine("🔄 Начинаем сбор данных");

                // Начинаем сбор данных
                _collector.Reset();
                StartTableUpdateTimer();


                double overshootEnd;
                string direction;

                if (IsAngleInRange(endAngle + 1, from, to))
                {
                    overshootEnd = endAngle - 2;
                    direction = "-";
                }

                else
                {
                    overshootEnd = endAngle + 2;
                    direction = "+";
                }

                if (Math.Abs(from - to) < 0.1)
                {
                    // Особый случай - полный круг (360°)
                    // Проверяем, можно ли сделать полный оборот
                    int fullCircleMovement = 3600; // 360° в единицах 0.1°

                    // Пробуем по часовой стрелке
                    if (currentCounter + fullCircleMovement <= 5400)
                    {
                        direction = "+";
                        overshootEnd = endAngle + 2;
                    }
                    // Пробуем против часовой стрелки
                    else
                    {
                        direction = "-";
                        overshootEnd = endAngle - 2;
                    }
                }


                overshootEnd = (overshootEnd + 360.0) % 360.0;
                //direction = GetOptimalDirection(currentCounter, startAngle, endAngle);
                Debug.WriteLine($"Направление движения: {direction} (counter: {currentCounter} -> {currentCounter + (direction == "+" ? 1 : -1) * (int)Math.Round(Math.Abs(endAngle - startAngle) * 10)})");

                Debug.WriteLine($"Движение к конечной точке: {overshootEnd:F1}° (overshoot +3°)");
                _comPortService.SetAntennaAngle(endAngle, "R", direction);


                // Перед циклом ожидания
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (AngleDiff(tempRxAntennaAngle, ReceiverAngleDeg) < 5.0)
                {
                    await Task.Delay(10, cancellationToken);
                    // Проверка на превышение времени ожидания
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.WriteLine("❌ Отмена при ожидании начальной точки");
                        break;
                    }
                    if (stopwatch.Elapsed.TotalSeconds > 2)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        LastEvent = "Антенна не начала движение. Отмена";
                        _acquisitionCts?.Cancel();
                        break;
                    }
                }

                _comPortService.SetAntennaAngle(overshootEnd, "R", direction);

                while (Math.Abs(ReceiverAngleDeg - overshootEnd) > 1.0)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.WriteLine("❌ Отмена в основном цикле");
                        break;
                    }

                    // Проверяем состояние порта - если связь потеряна, прерываем процесс
                    if (!_comPortService.IsOpen)
                    {
                        Debug.WriteLine("❌ Связь потеряна во время снятия диаграммы");
                        ConnectionStatus = "⚠ Связь потеряна во время снятия диаграммы";
                        break;
                    }

                    await Task.Delay(10, cancellationToken);
                }

                Debug.WriteLine($"🔄 Завершение сбора данных");
                _isDiagramDataCollecting = false;

                //UpdateTable();
                StopTableUpdateTimer();
                _collector.FinalizeData();
                UpdatePlotWithNormalizedData();
                Debug.WriteLine("✅ Диаграмма успешно завершена");
                _comPortService.StopAntenna("R");
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("❌ Операция была отменена пользователем");
                _isDiagramDataCollecting = false;
                StopTableUpdateTimer();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("❌ Операция была отменена");
                _isDiagramDataCollecting = false;
                StopTableUpdateTimer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"💥 Неожиданная ошибка: {ex.Message}");
                LastEvent = $"Ошибка сбора диаграммы: {ex.Message}";
                _isDiagramDataCollecting = false;
                StopTableUpdateTimer();
            }
            finally
            {
                IsDiagramAcquisitionRunning = false;
                StopTableUpdateTimer();
                Debug.WriteLine("=== КОНЕЦ СНЯТИЯ ДИАГРАММЫ ===");
            }
        }

        public static double AngleDiff(double a, double b)
        {
            double diff = Math.Abs(a - b) % 360.0;
            return diff > 180.0 ? 360.0 - diff : diff;
        }
        private bool IsAngleInRange(double angle, double from, double to)
        {



            angle = (angle + 360) % 360;
            from = (from + 360) % 360;
            to = (to + 360) % 360;


            double t_from_to = (from + 360 - to) % 360;
            double t_from_angle = (from + 360 - angle) % 360;

            if (Math.Abs(from - to) < 0.1) return true;
            double diffFromTo = (from - to + 360) % 360;
            double diffFromAngle = (from - angle + 360) % 360;

            return diffFromAngle <= diffFromTo;

        }

        public IEnumerable<string> AvailablePorts
        {
            get => _availablePorts;
            set
            {
                _availablePorts = value;
                OnPropertyChanged(nameof(AvailablePorts));
            }
        }
        [RelayCommand]
        public void RefreshPorts()
        {
            AvailablePorts = _comPortService.GetAvailablePortNames();
        }

        private void StartTableUpdateTimer()
        {
            if (_tableUpdateTimer == null)
            {
                _tableUpdateTimer = new DispatcherTimer();
                _tableUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
                _tableUpdateTimer.Tick += (s, e) => UpdateTable();
            }
            _tableUpdateTimer.Start();
        }

        private void StopTableUpdateTimer()
        {
            _tableUpdateTimer?.Stop();
        }

        private void UpdateTable()
        {
            lock (_dataLock)
            {
                if (_isFinalizingDiagram)
                    return;
                if (SelectedTab != null && IsRealtimeMode)
                {
                    var newData = _collector.GetTableData();
                    SelectedTab.AntennaDataCollection.ReplaceRange(newData);
                    _collector.FinalizeData();  //

                }
                if (OnBuildRadarPlot != null && IsRealtimeMode)
                {
                    var angles = _collector.GetGraphAngles();
                    double[] values;
                    if (IsPowerNormSelected)
                        values = _collector.GetGraphValues(d => d.PowerNorm);
                    else
                        values = _collector.GetGraphValues(d => d.VoltageNorm);
                    OnBuildRadarPlot.Invoke(angles.ToArray(), values.ToArray());
                    // Сохраняем сырые данные для графика в PlotData активной вкладки
                    if (SelectedTab != null)
                    {
                        SelectedTab.Plot.Angles = angles;
                        //SelectedTab.Plot.PowerNormValues = _collector.GetGraphValues(d => d.PowerDbm);
                        //SelectedTab.Plot.VoltageNormValues = _collector.GetGraphValues(d => d.Voltage);

                        SelectedTab.Plot.PowerNormValues = _collector.GetGraphValues(d => d.PowerNorm);
                        SelectedTab.Plot.VoltageNormValues = _collector.GetGraphValues(d => d.VoltageNorm);
                    }
                }
            }
        }

        private void UpdatePlotWithNormalizedData()
        {
            lock (_dataLock)
            {
                _isFinalizingDiagram = true;
                if (SelectedTab != null)
                {
                    SelectedTab.Plot.Angles = _collector.GetGraphAngles();
                    SelectedTab.Plot.PowerNormValues = _collector.GetGraphValues(d => d.PowerNorm);
                    SelectedTab.Plot.VoltageNormValues = _collector.GetGraphValues(d => d.VoltageNorm);
                    RequestPlotRedraw?.Invoke();
                }
                _isFinalizingDiagram = false;
            }
        }

        public event Action<bool>? ShowAntennaChanged;
        public event Action<bool>? ShowSectorChanged;
        public event Action? RequestPlotRedraw;
        public event Action? RequestClearCurrentPlot;


        public void StopMessaging()
        {
            _comPortService.StopMessaging();
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            OnPropertyChanged(nameof(SelectedTab));
        }

        partial void OnReceiverMoveAngleChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                ReceiverMoveAngleError = "Поле не может быть пустым";
            else if (!double.TryParse(value, out var d) || d < 0 || d > 359.9)
                ReceiverMoveAngleError = "Введите число от 0 до 359.9";
            else
                ReceiverMoveAngleError = "";
        }

        [RelayCommand]
        public void MoveReceiverToAngle()
        {
            if (string.IsNullOrWhiteSpace(ReceiverMoveAngle) || !string.IsNullOrEmpty(ReceiverMoveAngleError) || !_comPortService.IsOpen)
                return;
            if (double.TryParse(ReceiverMoveAngle, out var angle) && angle >= 0 && angle <= 359.9)
            {
                _comPortService.SetAntennaAngle(angle, "R", "G");
            }
        }

        [RelayCommand]
        public void SetReceiverAngle()
        {
            if (string.IsNullOrWhiteSpace(ReceiverSetAngle) || !string.IsNullOrEmpty(ReceiverSetAngleError) || !_comPortService.IsOpen)
                return;
            if (double.TryParse(ReceiverSetAngle, out var angle) && angle >= 0 && angle <= 360)
            {
                _comPortService.SetAntennaAngle(angle, "R", "S");
            }
        }

        [RelayCommand]
        public void ClearTable()
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
            //OnBuildRadarPlot?.Invoke(Array.Empty<double>(), Array.Empty<double>());
            RequestClearCurrentPlot?.Invoke();
        }

        [ObservableProperty]
        private bool isDarkTheme;

        partial void OnIsDarkThemeChanged(bool value)
        {
            ((App)Avalonia.Application.Current!).SetTheme(
                value ? ThemeVariant.Dark : ThemeVariant.Light);
        }

        [ObservableProperty]
        private string lastEvent = "";

        [ObservableProperty]
        private string dataFlowStatus = "🔴 Нет данных";


    }
}