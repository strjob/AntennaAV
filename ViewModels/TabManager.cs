using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace AntennaAV.ViewModels
{
    public partial class TabManager : ObservableObject
    {
        private const int MaxTabCount = 10;
        private static readonly string[] DefaultColors = new[]
        {
            "#CC0000", // Красный
            "#00CC00", // Зелёный
            "#0000CC", // Синий
            "#CC8300", // Оранжевый
            "#660066", // Фиолетовый
            "#266489", // Синий
            "#E7298A", // Розовый
            "#E6AB02", // Жёлтый
        };

        public ObservableCollection<TabViewModel> Tabs { get; } = new();

        [ObservableProperty]
        private int selectedTabIndex;

        public TabViewModel? SelectedTab => Tabs.ElementAtOrDefault(SelectedTabIndex);

        public bool HasTabs => Tabs.Count > 0;
        public bool CanRemoveTab => Tabs.Count > 1;

        public TabManager()
        {
            Tabs.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasTabs));
                OnPropertyChanged(nameof(CanRemoveTab));
            };
        }

        public void AddTab()
        {
            if (Tabs.Count >= MaxTabCount)
                return;
            int colorIndex = Tabs.Count % DefaultColors.Length;
            var tab = new TabViewModel { Header = $"Антенна {Tabs.Count + 1}" };
            tab.Plot.ColorHex = DefaultColors[colorIndex];
            tab.AddAntennaData(new System.Collections.Generic.List<GridAntennaData>());
            Tabs.Add(tab);
            SelectedTabIndex = Tabs.Count - 1;
            OnPropertyChanged(nameof(SelectedTab));
        }

        public void RemoveTab()
        {
            if (Tabs.Count <= 1)
                return;
            if (SelectedTab != null)
            {
                int idx = SelectedTabIndex;
                Tabs.Remove(SelectedTab);
                if (idx >= Tabs.Count)
                    SelectedTabIndex = Tabs.Count - 1;
                SelectTab(SelectedTabIndex);
                OnPropertyChanged(nameof(SelectedTab));
            }
        }

        public void SelectTab(int index)
        {
            if (index >= 0 && index < Tabs.Count)
            {
                SelectedTabIndex = index;
                OnPropertyChanged(nameof(SelectedTab));
            }
        }
    }
} 