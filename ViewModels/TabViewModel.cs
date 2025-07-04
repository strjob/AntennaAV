using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


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

        public TabViewModel()
        {
            

            AntennaSource = new FlatTreeDataGridSource<GridAntennaData>(AntennaDataCollection)
            {
                Columns =
                {
                    new TextColumn<GridAntennaData, double>("Угол, °", x => x.Angle),
                    new TextColumn<GridAntennaData, double>("P, дБм", x => x.PowerDbm),
                    new TextColumn<GridAntennaData, double>("V, мкВ", x => x.Voltage), 
                    new TextColumn<GridAntennaData, double>("P норм.", x => x.PowerNorm),
                    new TextColumn<GridAntennaData, double>("V норм.", x => x.VoltageNorm),
                    new TextColumn<GridAntennaData, String>("Время", x => x.TimeStr),
                }
            };
            
        }

        public void AddAntennaData(IEnumerable<GridAntennaData> newItems)
        {
            AntennaDataCollection.AddRange(newItems);
        }
    }

    public class PlotData
    {
        public double[] Angles { get; set; } = Array.Empty<double>();
        public double[] PowerNormValues { get; set; } = Array.Empty<double>();
        public double[] VoltageNormValues { get; set; } = Array.Empty<double>();
        public string ColorHex { get; set; } = "#0000FF";
        public bool IsVisible { get; set; } = true;
    }
}
