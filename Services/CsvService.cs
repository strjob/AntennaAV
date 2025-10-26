using AntennaAV.Models;
using AntennaAV.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntennaAV.Services
{
    public class CsvService
    {
        public async Task<bool> ExportTabAsync(TabViewModel tab, Window window)
        {
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить таблицу",
                SuggestedFileName = $"{tab.Header}.csv",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new("CSV файл") { Patterns = new[] { "*.csv" } }
                },
                DefaultExtension = "csv"
            });

            if (file is null)
                return false; // пользователь отменил

            var sb = new StringBuilder();
            sb.AppendLine("Angle;PowerDbm;Voltage;PowerNorm;VoltageNorm;Time");

            foreach (var row in tab.AntennaDataCollection.ToArray())
            {
                sb.AppendLine($"{row.AngleStr};{row.PowerDbmStr};{row.VoltageStr};{row.PowerNormStr};{row.VoltageNormStr};{row.TimeStr}");
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(sb.ToString());
            return true;
        }

        public async Task<List<GridAntennaData>?> ImportTableAsync(Window window)
        {
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
                return null;

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
                    var parts = line.Split(';');
                    if (parts.Length < 6) continue;
                    if (!double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var angle)) continue;
                    if (!double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var powerDbm)) continue;
                    if (!double.TryParse(parts[2].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var voltage)) continue;
                    if (!double.TryParse(parts[3].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var powerNorm)) continue;
                    if (!double.TryParse(parts[4].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var voltageNorm)) continue;
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
            return newRows;
        }
    }
}
