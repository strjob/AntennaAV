using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

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

        // Добавлено: варианты усиления
        public ObservableCollection<int> GainOptions { get; } = new() { 1, 2, 4, 8, 16, 32, 64, 128 };

        [ObservableProperty]
        private int selectedGain = 1;

        public CalibrationWindowViewModel(IComPortService comPortService)
        {
            _comPortService = comPortService;
        }


        [RelayCommand]
        public void SendCalibrationPoint(string? valueFromTextBox)
        {

            if (String.IsNullOrEmpty(valueFromTextBox))
            {
                CalibrationErrorText = "Поле не должно быть пустым";
                return;
            }
            else
            {
                var normalized = valueFromTextBox.Replace(',', '.');
                if (double.TryParse(normalized.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedValue))
                {
                    _comPortService.SetCalibrationPoint(parsedValue);
                    CalibrationErrorText = "";
                }
                else
                {
                    CalibrationErrorText = "Не удлось распознать число";
                }
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

        [RelayCommand]
        private void SetGain()
        {
            _comPortService.SetAdcGain(SelectedGain);
        }

        [RelayCommand]
        private void SetDefaultRfGain()
        {
            _comPortService.SetDefaultRFGain(SelectedGain);
        }
    }
}