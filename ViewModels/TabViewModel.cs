using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls;


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

        public TabViewModel()
        {
            

            AntennaSource = new FlatTreeDataGridSource<GridAntennaData>(AntennaDataCollection)
            {
                Columns =
                {
                    new TextColumn<GridAntennaData, string>("Угол, °", x => x.AngleStr),
                    new TextColumn<GridAntennaData, string>("P, дБм", x => x.PowerDbmStr),
                    new TextColumn<GridAntennaData, string>("V, мкВ", x => x.VoltageStr),
                    new TextColumn<GridAntennaData, string>("P норм.", x => x.PowerNormStr),
                    new TextColumn<GridAntennaData, string>("V норм.", x => x.VoltageNormStr),
                    new TextColumn<GridAntennaData, string>("Время", x => x.TimeStr),
                }
            };
        }

        public void AddAntennaData(IEnumerable<GridAntennaData> newItems)
        {
            AntennaDataCollection.AddRange(newItems);
        }
    }
}
