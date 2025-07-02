using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls;
using Catel.Collections;

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
            AntennaDataCollection.Add(new GridAntennaData { Angle = 123, PowerDbm = -30, Voltage = 1.2, PowerNorm = -25, VoltageNorm = 0.987, Time = DateTime.Now });

            AntennaSource = new FlatTreeDataGridSource<GridAntennaData>(AntennaDataCollection)
            {
                Columns =
                {
                    new TextColumn<GridAntennaData, double>("Угол, °", x => x.Angle),
                    new TextColumn<GridAntennaData, double>("Мощность, дБм", x => x.PowerDbm),
                    new TextColumn<GridAntennaData, double>("Напряжение, мкВ", x => x.Voltage),
                    new TextColumn<GridAntennaData, double>("Мощность нормированная, дБм", x => x.PowerNorm),
                    new TextColumn<GridAntennaData, double>("Напряжение нормированное", x => x.VoltageNorm),
                    new TextColumn<GridAntennaData, DateTime>("Время", x => x.Time),
                }
            };
        }

        public void AddAntennaData(IEnumerable<GridAntennaData> newItems)
        {
            AntennaDataCollection.AddRange(newItems);
        }
    }
}
