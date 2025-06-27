using AntennaAV.Models;
using AntennaAV.Services;
using AntennaAV.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScottPlot;
using ScottPlot.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AntennaAV.Services.ComPortManager;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace AntennaAV.ViewModels
{
    
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IComPortService _comPortService;

        
        public MainWindowViewModel()
    :       this(Design.IsDesignMode ? new MockComPortService() : throw new InvalidOperationException("Этот конструктор используется только в дизайнере"))
        {
            if (Design.IsDesignMode)
            {
                ReceiverAngleDeg = 123.4;
                TransmitterAngleDeg = 234.5;
                PowerDbm = -30.1;
                AntennaType = 2;
                RxAntennaCounter = 7;
                Timestamp = DateTime.Now;
            }
        }



        private readonly DispatcherTimer _uiTimer = new();

        public bool IsDiagramRecording = false;

        public bool HasTabs => Tabs.Count > 0;

        [ObservableProperty]
        private ObservableCollection<TabViewModel> tabs = new();

        [ObservableProperty]
        private int selectedTabIndex;

        [ObservableProperty]
        private string connectionStatus = "⏳ Не подключено";

        [ObservableProperty]
        private double receiverAngleDeg;

        [ObservableProperty]
        private double transmitterAngleDeg;

        [ObservableProperty]
        private double powerDbm;

        [ObservableProperty]
        private int antennaType;

        [ObservableProperty]
        private int rxAntennaCounter;

        [ObservableProperty]
        private DateTime timestamp;

        [ObservableProperty]
        private bool isPortOpen;

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
        private string transmitterAngle1 = "0";

        [ObservableProperty]
        private string? angle1Error;

        [ObservableProperty]
        private double transmitterAngle2 = 0;

        [ObservableProperty]
        private bool isDiagramAcquisitionRunning;

        private DispatcherTimer? _tableUpdateTimer;

        private AntennaDiagramCollector _collector = new();

        private bool _isDiagramDataCollecting = false;
        private double _acquisitionFrom;
        private double _acquisitionTo;
        private string _acquisitionDir = "+";

        private CancellationTokenSource? _acquisitionCts;

        public string DiagramButtonText => IsDiagramAcquisitionRunning ? "Прервать" : "Начать построение диаграммы";

        public IRelayCommand DiagramButtonCommand => _diagramButtonCommand ??= new RelayCommand(async () =>
        {
            if (IsDiagramAcquisitionRunning)
                CancelDiagramAcquisition();
            else
                await StartDiagramAcquisition();
        });
        private RelayCommand? _diagramButtonCommand;

        [ObservableProperty]
        private string dataFlowStatus = "🔴 Нет данных";

        private DateTime _lastDataReceived = DateTime.MinValue;

        partial void OnSectorSizeChanged(string value)
        {
            // Проверяем на пустые значения
            if (string.IsNullOrWhiteSpace(value))
            {
                // Если значение пустое, устанавливаем минимальное значение
                SectorSize = "10";
                return;
            }
            
            if (double.TryParse(value, out double d))
            {
                // Проверяем границы
                if (d < 10)
                {
                    // Если значение меньше 10, устанавливаем 10 и выходим
                    // Это вызовет повторный вызов OnSectorSizeChanged с новым значением
                    SectorSize = "10";
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
                SectorSize = "10";
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
/*
        [RelayCommand]
        private void BuildDiagram()
        {
            // SectorSize и SectorCenter уже содержат введённые пользователем значения
            // Можно преобразовать в число:
            if (double.TryParse(SectorSize, out var size) && double.TryParse(SectorCenter, out var center))
            {
                // Вычисляем from и to из размера и центра сектора
                var (from, to) = CalculateSectorRange(size, center);
                
                // Здесь можно добавить логику для построения диаграммы
                // Например, вызвать OnBuildRadarPlot с данными
                OnBuildRadarPlot?.Invoke(new double[] { from, to }, new double[] { 0, 0 });
            }
        }
*/
        public event Action<double[], double[]>? OnBuildRadarPlot;


        [RelayCommand]
        private void AddTab()
        {
            var tab = new TabViewModel
            {
                Header = $"Антенна {Tabs.Count + 1}",
                AntennaDataCollection = new ObservableCollection<GridAntennaData>
            {
                //new() { Angle = Tabs.Count, PowerDbm = -30, Voltage = 1.2, PowerNorm = -25, VoltageNorm = 0.987, Time = DateTime.Now },
                //new() { Angle = Tabs.Count, PowerDbm = -28, Voltage = 1.5, PowerNorm = -23, VoltageNorm = 1.123, Time = DateTime.Now }
            }
            };

            Tabs.Add(tab);
            SelectedTabIndex = Tabs.Count - 1;

            UpdateTabCommands();

        }




        public async Task ExportSelectedTabAsync(Window window)
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

            foreach (var row in SelectedTab.AntennaDataCollection)
            {
                sb.AppendLine($"{row.Angle},{row.PowerDbm},{row.Voltage},{row.PowerNorm},{row.VoltageNorm},{row.Time:O}");
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(sb.ToString());

            ConnectionStatus = $"✅ Файл сохранён: {file.Name}";
        }

        private void UpdateTabCommands()
        {
            EditTabCommand.NotifyCanExecuteChanged();
            RemoveTabCommand.NotifyCanExecuteChanged();
        }


        public TabViewModel? SelectedTab => Tabs.ElementAtOrDefault(SelectedTabIndex);

        [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
        private void EditTab()
        {
            if (SelectedTab is not null)
            {
                SelectedTab.Header = $"Редактировано {DateTime.Now:T}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
        private void RemoveTab()
        {
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
            Tabs.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasTabs));
            };

            AddTab();

            _ = ConnectToPortAsync();


            _uiTimer.Interval = TimeSpan.FromMilliseconds(200);
            _uiTimer.Tick += (_, _) => OnUiTimerTick();
            _uiTimer.Start();
        }



        private bool _isReconnecting = false;

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
                            _collector.AddPoint(data.ReceiverAngleDeg10, data.PowerDbm, data.Timestamp);
                        }
                        lastData = data;
                        dataReceived = true;
                    }
                    if (OnBuildRadarPlot != null)
                    {
                        var angles = _collector.GetGraphAngles();
                        double[] values;
                        if (IsPowerNormSelected)
                            values = _collector.GetGraphValues(d => d.PowerDbm);
                        else
                            values = _collector.GetGraphValues(d => d.Voltage);
                        OnBuildRadarPlot.Invoke(angles, values);
                    }
                }
                else
                {
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
                    AntennaType = lastData.AntennaType;
                    RxAntennaCounter = lastData.RxAntennaCounter;
                    Timestamp = lastData.Timestamp;
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

        partial void OnTransmitterAngle1Changed(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                Angle1Error = "Поле не может быть пустым";
            else if (!double.TryParse(value, out var d) || d < 0 || d > 359.9)
                Angle1Error = "Введите число от 0 до 359.9";
            else
                Angle1Error = null;
        }

        [RelayCommand]
        public void MoveTransmitterToAngle()
        {
            if (string.IsNullOrWhiteSpace(TransmitterAngle1) || Angle1Error != null || !_comPortService.IsOpen)
                return;
            if (double.TryParse(TransmitterAngle1, out var angle) && angle >= 0 && angle <= 360)
            {
                _comPortService.SetAntennaAngle(angle, "T", "G");
            }
        }

        [RelayCommand]
        public void SetTransmitterAngle()
        {
            if (TransmitterAngle2 >= 0 && TransmitterAngle2 <= 359.9 && _comPortService.IsOpen)
            {
                _comPortService.SetAntennaAngle(TransmitterAngle2, "T", "S");
            }
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



        public void CancelDiagramAcquisition()
        {
            _acquisitionCts?.Cancel();
            _acquisitionCts?.Dispose();
            _acquisitionCts = null;
            _comPortService.StopAntenna("R");
        }

        public async Task StartDiagramAcquisitionAsync(double from, double to, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"Начинаем сбор диаграммы: размер сектора = {to - from:F1}°, центр = {(from + to) / 2:F1}°");

            if (IsDiagramAcquisitionRunning)
            {
                Debug.WriteLine("❌ Диаграмма уже запущена, выход");
                return;
            }
            
            IsDiagramAcquisitionRunning = true;
            _isDiagramDataCollecting = false;
            OnPropertyChanged(nameof(DiagramButtonText));
            OnPropertyChanged(nameof(DiagramButtonCommand));
            
            try
            {
                // Определяем текущее положение антенны
                double currentAngle = ReceiverAngleDeg;
                int currentCounter = RxAntennaCounter;
                Debug.WriteLine($"Текущее положение: угол={currentAngle:F1}°, counter={currentCounter}");
                
                // Выбираем начальную точку (ближайшую к текущему положению)
                double startAngle, endAngle;
                if (Math.Abs(currentAngle - from) <= Math.Abs(currentAngle - to))
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


                // Определяем направление движения с учетом ограничений RxAntennaCounter


                // Двигаемся к конечной точке с overshoot
                double overshootEnd;
                string direction;

                if (Math.Abs(from - to) < 0.1)
                {
                    // Особый случай - полный круг (360°)
                    // Проверяем, можно ли сделать полный оборот
                    int fullCircleMovement = 3600; // 360° в единицах 0.1°

                    // Пробуем по часовой стрелке
                    if (currentCounter + fullCircleMovement <= 5400)
                    {
                        direction = "+";
                        overshootEnd = endAngle;
                    }
                    // Пробуем против часовой стрелки
                    else
                    {
                        direction = "-";
                        overshootEnd = endAngle;
                    }
                }

                else
                {
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
                }






                //direction = GetOptimalDirection(currentCounter, startAngle, endAngle);
                Debug.WriteLine($"Направление движения: {direction} (counter: {currentCounter} -> {currentCounter + (direction == "+" ? 1 : -1) * (int)Math.Round(Math.Abs(endAngle - startAngle) * 10)})");

                Debug.WriteLine($"Движение к конечной точке: {overshootEnd:F1}° (overshoot +3°)");
                _comPortService.SetAntennaAngle(overshootEnd, "R", direction);


                while (Math.Abs(tempRxAntennaAngle - ReceiverAngleDeg) < 2.0)
                { 
                    await Task.Delay(10, cancellationToken);
                }    

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
                StopTableUpdateTimer();
                _collector.FinalizeData();
                UpdateTable();
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
                _isDiagramDataCollecting = false;
                StopTableUpdateTimer();
            }
            finally
            {
                IsDiagramAcquisitionRunning = false;
                OnPropertyChanged(nameof(DiagramButtonText));
                OnPropertyChanged(nameof(DiagramButtonCommand));
                Debug.WriteLine("=== КОНЕЦ СНЯТИЯ ДИАГРАММЫ ===");
            }
        }

        private string GetOptimalDirection(int currentCounter, double startAngle, double endAngle)
        {
            if (startAngle == endAngle)
            {
                // Особый случай - полный круг (360°)
                // Проверяем, можно ли сделать полный оборот
                int fullCircleMovement = 3600; // 360° в единицах 0.1°
                
                // Пробуем по часовой стрелке
                if (currentCounter + fullCircleMovement <= 5400)
                {
                    return "+";
                }
                // Пробуем против часовой стрелки
                else if (currentCounter - fullCircleMovement >= -5400)
                {
                    return "-";
                }
                else
                {
                    // Если ни одно направление не подходит, выбираем то, которое ближе к центру
                    return Math.Abs(currentCounter) <= Math.Abs(currentCounter - fullCircleMovement) ? "+" : "-";
                }
            }
            
            // Получаем текущее положение антенны
            double currentAngle = ReceiverAngleDeg;
            
            // Вычисляем расстояния до границ сектора
            double distanceToStart = Math.Abs(currentAngle - startAngle);
            double distanceToEnd = Math.Abs(currentAngle - endAngle);
            
            // Учитываем переход через 0°/360°
            if (distanceToStart > 180) distanceToStart = 360 - distanceToStart;
            if (distanceToEnd > 180) distanceToEnd = 360 - distanceToEnd;
            
            // Определяем предпочтительное направление по геометрии
            string preferredDirection;
            if (distanceToStart <= distanceToEnd)
            {
                // Ближе к start, идём start → end (по часовой +)
                preferredDirection = "+";
            }
            else
            {
                // Ближе к end, идём end → start (против часовой -)
                preferredDirection = "-";
            }
            
            Debug.WriteLine($"🔍 GetOptimalDirection: current={currentAngle:F1}°, start={startAngle:F1}°, end={endAngle:F1}°");
            Debug.WriteLine($"🔍 GetOptimalDirection: distanceToStart={distanceToStart:F1}°, distanceToEnd={distanceToEnd:F1}°");
            Debug.WriteLine($"🔍 GetOptimalDirection: preferredDirection={preferredDirection}");
            
            // Рассчитываем изменение RxAntennaCounter в единицах 0.1 градуса
            double angleDiff = Math.Abs(endAngle - startAngle);
            int requiredMovement = (int)Math.Round(angleDiff * 10);
            
            Debug.WriteLine($"🔍 GetOptimalDirection: angleDiff={angleDiff:F1}°, requiredMovement={requiredMovement}");
            
            if (preferredDirection == "+")
            {
                // Движение по часовой стрелке (увеличение counter)
                int newCounter = currentCounter + requiredMovement;
                if (newCounter <= 5400)
                {
                    Debug.WriteLine($"🔍 GetOptimalDirection: можно двигаться в предпочтительном направлении + (counter: {currentCounter} -> {newCounter})");
                    return "+"; // можно двигаться в предпочтительном направлении
                }
                else
                {
                    Debug.WriteLine($"🔍 GetOptimalDirection: нельзя двигаться в направлении + (counter: {currentCounter} -> {newCounter} > 5400), выбираем -");
                    return "-"; // нужно двигаться в противоположном направлении
                }
            }
            else
            {
                // Движение против часовой стрелки (уменьшение counter)
                int newCounter = currentCounter - requiredMovement;
                if (newCounter >= -5400)
                {
                    Debug.WriteLine($"🔍 GetOptimalDirection: можно двигаться в предпочтительном направлении - (counter: {currentCounter} -> {newCounter})");
                    return "-"; // можно двигаться в предпочтительном направлении
                }
                else
                {
                    Debug.WriteLine($"🔍 GetOptimalDirection: нельзя двигаться в направлении - (counter: {currentCounter} -> {newCounter} < -5400), выбираем +");
                    return "+"; // нужно двигаться в противоположном направлении
                }
            }
        }

        private bool IsAngleInRange(double angle, double from, double to)
        {
            double originalAngle = angle;
            double originalFrom = from;
            double originalTo = to;

            //angle = angle % 360;
            //from = from % 360;
            //to = to % 360;
            angle = (angle + 360) % 360;
            from = (from + 360) % 360;
            to = (to + 360) % 360;

            bool result;

            double t_from_to = (from + 360 - to) % 360;
            double t_from_angle= (from + 360 - angle) % 360;


            //double diffFromTo = (from - to + 360) % 360;
            //double diffFromAngle = (from - angle + 360) % 360;

            //return diffFromAngle <= diffFromTo;

            if (Math.Abs(from - to) < 0.1) return true;



            result = t_from_angle <= t_from_to;
            /*
            // Если сектор не пересекает 0°
            if (from <= to)
                result = angle >= from && angle <= to;
            // Если сектор пересекает 0°
            else
                result = angle >= from || angle <= to;

            // Логируем только если результат false, чтобы не засорять консоль
            */
                Debug.WriteLine($"🔍 IsAngleInRange: angle={originalAngle:F1}°→{angle:F1}°, from={originalFrom:F1}°→{from:F1}°, to={originalTo:F1}°→{to:F1}°, result={result}");
            
            
            return result;
        }

        private void StartTableUpdateTimer()
        {
            if (_tableUpdateTimer == null)
            {
                _tableUpdateTimer = new DispatcherTimer();
                _tableUpdateTimer.Interval = TimeSpan.FromSeconds(1);
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
            if (SelectedTab != null)
            {
                var newData = _collector.GetTableData();
                SelectedTab.AntennaDataCollection = new ObservableCollection<GridAntennaData>(newData);
            }
            /*
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ScrollActiveDataGridToEnd();
            }*/
        }

        public event Action<bool>? ShowAntennaChanged;
        public event Action<bool>? ShowSectorChanged;
    }
}