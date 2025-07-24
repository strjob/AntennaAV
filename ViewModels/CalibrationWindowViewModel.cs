using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AntennaAV.Services;

namespace AntennaAV.ViewModels
{
    public partial class CalibrationWindowViewModel : ObservableObject
    {
        private readonly IComPortService _comPortService;

        [ObservableProperty]
        private string calibrationErrorText = "";

        public CalibrationWindowViewModel(IComPortService comPortService)
        {
            _comPortService = comPortService;
        }


        [RelayCommand]
        public void SendCalibrationPoint(string valueFromTextBox)
        {
            // Заменяем запятую на точку для универсального парсинга
            var normalized = valueFromTextBox.Replace(',', '.');
            if (double.TryParse(normalized.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedValue))
            {
                _comPortService.SetCalibrationPoint(parsedValue);
                CalibrationErrorText = "";
            }
            else
            {
                CalibrationErrorText = "Ошибка: не удлось распознать число";
            }
        }

        [RelayCommand]
        public void SaveCalibration()
        {
            // Сохранить калибровку
            _comPortService.SaveCalibration();
        }

        [RelayCommand]
        public void ClearCalibration()
        {
            // Очистить калибровку
            _comPortService.ClearCalibration();
        }

        [RelayCommand]
        public void ReadCalibration()
        {
            // Прочитать калибровку
            _comPortService.ReadCalibration();
        }
    }
}