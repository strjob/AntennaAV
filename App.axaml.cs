using AntennaAV.Services;
using AntennaAV.ViewModels;
using AntennaAV.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using System.Linq;

namespace AntennaAV
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // ������ ��������� �������
                //IComPortService comPortService = new ComPortManager();

                IComPortService comPortService = new TestComPortService();

                // ������ ViewModel � ��������� �����������
                var mainViewModel = new MainWindowViewModel(comPortService);

                // ����������� �������� ������ ����
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}