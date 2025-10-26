using AntennaAV.Services;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;

namespace AntennaAV.ViewModels
{
    // ViewModel для отдельной вкладки с данными диаграммы направленности
    // Управляет таблицей данных, заголовком вкладки и настройками отображения графика
    public partial class TabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string header = string.Empty;

        [ObservableProperty]
        private double shiftAngleValue = 10.0;

        // Event raised when ShiftAngleValue changes (source-generated partial will call OnShiftAngleValueChanged)
        public event Action<double>? ShiftAngleValueChanged;

        // Коллекция данных антенны для отображения в таблице
        public FastObservableCollection<GridAntennaData> AntennaDataCollection { get; } = new();

        [ObservableProperty]
        private bool isEditingHeader;

        // Источник данных для TreeDataGrid компонента
        public FlatTreeDataGridSource<GridAntennaData> AntennaSource { get; }

        // Данные для построения графика диаграммы направленности
        public PlotData Plot { get; set; } = new PlotData();

        // Флаг изменения цвета графика для обновления отображения
        public bool IsPlotColorChanged { get; set; }

        // Инициализирует новую вкладку с настройками таблицы данных
        public TabViewModel()
        {
            AntennaSource = new FlatTreeDataGridSource<GridAntennaData>(AntennaDataCollection)
            {
                Columns =
                {
                new FormattedStringColumn<GridAntennaData>(
                    "Угол, °",
                    x => x.AngleStr,
                    x => x.Angle),

                new FormattedStringColumn<GridAntennaData>(
                    "P, дБм",
                    x => x.PowerDbmStr,
                    x => x.PowerDbm),

                new FormattedStringColumn<GridAntennaData>(
                    "V, мВ",
                    x => x.VoltageStr,
                    x => x.Voltage),

                new FormattedStringColumn<GridAntennaData>(
                    "P норм.",
                    x => x.PowerNormStr,
                    x => x.PowerNorm),

                new FormattedStringColumn<GridAntennaData>(
                    "V норм.",
                    x => x.VoltageNormStr,
                    x => x.VoltageNorm),

                new TextColumn<GridAntennaData, string>("Время", x => x.TimeStr)
                }
            };

            Plot.ColorChanged += () =>
            {
                IsPlotColorChanged = true;
            };
        }

        // Добавляет новые данные антенны в коллекцию
        public void AddAntennaData(IEnumerable<GridAntennaData> newItems)
        {
            AntennaDataCollection.AddRange(newItems);
        }

        // Очищает все данные в таблице
        public void ClearTableData()
        {
            AntennaDataCollection.Clear();
        }

        // source generator will call this partial on property change
        partial void OnShiftAngleValueChanged(double value)
        {
            ShiftAngleValueChanged?.Invoke(value);
        }
    }

    // Кастомная колонка для TreeDataGrid с форматированным отображением и корректной сортировкой
    // Позволяет показывать отформатированные строки, но сортировать по числовым значениям
    public class FormattedStringColumn<TModel> : TextColumn<TModel, string?> where TModel : class
    {
        private readonly Func<TModel, IComparable?> _sortKeySelector;

        public FormattedStringColumn(
            string header,
            Expression<Func<TModel, string?>> displaySelector,
            Func<TModel, IComparable?> sortKeySelector)
            : base(header, displaySelector)
        {
            _sortKeySelector = sortKeySelector;
        }

        public override Comparison<TModel>? GetComparison(ListSortDirection direction)
        {
            return (x, y) =>
            {
                var a = _sortKeySelector(x);
                var b = _sortKeySelector(y);
                var result = Comparer<IComparable?>.Default.Compare(a, b);
                return direction == ListSortDirection.Ascending ? result : -result;
            };
        }
    }

    // Данные для отображения графика диаграммы направленности
    // Содержит массивы углов, мощностей, напряжений и настройки визуализации
    public partial class PlotData : ObservableObject
    {
        // Массив углов в градусах
        public double[] Angles { get; set; } = Array.Empty<double>();

        // Массив нормализованных значений мощности
        public double[] PowerNormValues { get; set; } = Array.Empty<double>();

        // Массив нормализованных значений напряжения
        public double[] VoltageNormValues { get; set; } = Array.Empty<double>();



        [ObservableProperty]
        private string colorHex = "#0000FF";

        [ObservableProperty]
        private bool isVisible = true;

        public int MarkerSize { get; set; } = 0;

        // Сегменты данных для составных графиков
        public List<PlotSegmentData>? PlotSegments { get; set; }

        // Событие изменения цвета графика
        public event Action? ColorChanged;

        // Вызывается при изменении цвета для уведомления подписчиков
        partial void OnColorHexChanged(string value)
        {
            ColorChanged?.Invoke();
        }


    }
}