using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntennaAV.ViewModels
{
    public partial class TabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string header = string.Empty;

        [ObservableProperty]
        private ObservableCollection<GridAntennaData> antennaDataCollection = new();


        [ObservableProperty]
        private bool isEditingHeader;
    }
}
