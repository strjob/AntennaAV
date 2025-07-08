using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;


namespace AntennaAV.ViewModels
{
    public partial class TabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string header = string.Empty;

        public FastObservableCollection<GridAntennaData> AntennaDataCollection { get; } = new();

        [ObservableProperty]
        private bool isEditingHeader;

        public FlatTreeDataGridSource<GridAntennaData> AntennaSource { get; }

        public PlotData Plot { get; set; } = new PlotData();

        public List<ScottPlot.Plottables.Scatter> DataScatters { get; } = new();

        public bool IsPlotColorDirty { get; set; }

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
                    "V, мкВ",
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
                IsPlotColorDirty = true;
            };
        }

        public void AddAntennaData(IEnumerable<GridAntennaData> newItems)
        {
            AntennaDataCollection.AddRange(newItems);
        }

        public void ClearTableData()
        {
            AntennaDataCollection.Clear();
        }
    }


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

    public partial class PlotData : ObservableObject
    {
        public double[] Angles { get; set; } = Array.Empty<double>();
        public double[] PowerNormValues { get; set; } = Array.Empty<double>();
        public double[] VoltageNormValues { get; set; } = Array.Empty<double>();

        [ObservableProperty]
        private string colorHex = "#0000FF";
        public string colorHexPrev = "#0000FF";

        [ObservableProperty]
        private bool isVisible = true;

        // Добавляем событие
        public event Action? ColorChanged;
        partial void OnColorHexChanged(string value)
        {
            ColorChanged?.Invoke();
        }
    }
}
