using System;
using System.IO;
using System.Text.Json;
using AntennaAV.Models;
using System.Diagnostics;

namespace AntennaAV.Services
{
    public interface ISettingsService
    {
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _settingsPath;

        public SettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "AntennaAV");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true  // Игнорирует разницу в регистре (isDarkTheme == IsDarkTheme)
                    };
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
                    Debug.WriteLine($"✅ Настройки загружены из: {_settingsPath}");

                    return settings ?? new AppSettings();
                }
                else
                {
                    Debug.WriteLine($"📁 Файл настроек не найден: {_settingsPath}, используются настройки по умолчанию");
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но возвращаем настройки по умолчанию
                Debug.WriteLine($"❌ Ошибка загрузки настроек: {ex.Message}");
            }

            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                File.WriteAllText(_settingsPath, json);
                Debug.WriteLine($"💾 Настройки сохранены в: {_settingsPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка сохранения настроек: {ex.Message}");
            }
        }
    }
}
