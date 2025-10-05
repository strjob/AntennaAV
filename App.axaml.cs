using AntennaAV.Services;
using AntennaAV.ViewModels;
using AntennaAV.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using System.Linq;
using Avalonia.Styling;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Threading;

namespace AntennaAV
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void SetTheme(ThemeVariant theme)
        {
            Application.Current!.RequestedThemeVariant = theme;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Показываем SplashWindow
                var splash = new SplashWindow();
                splash.Show();

                Task.Run(async () =>
                {
                    await Task.Delay(50);

                Dispatcher.UIThread.Post(()  =>
                    {
                        //IComPortService comPortService = new TestComPortService();
                        IComPortService comPortService = new ComPortManager();
                        var mainViewModel = new MainWindowViewModel(comPortService);
                        var mainWindow = new MainWindow
                        {
                            DataContext = mainViewModel
                        };
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();
                        
                        // Загружаем настройки после показа окна
                        mainViewModel.LoadSettings();
                        
                        splash.Close();
                    });
                });
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}