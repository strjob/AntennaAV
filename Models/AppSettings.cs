namespace AntennaAV.Models
{
    public class AppSettings
    {
        public bool IsDarkTheme { get; set; } = false;
        public bool ShowLegend { get; set; } = true;
        public bool ShowMarkers { get; set; } = false;
        public bool IsAutoscale { get; set; } = true;
        public bool ShowAntenna { get; set; } = true;
        public bool ShowSector { get; set; } = true;
        public bool MoveToZeroOnClose { get; set; } = false;
        public int? ManualScaleValue { get; set; } = -30;
        public int? MarkerSize { get; set; } = 6;
        public int? LineWidth { get; set; } = 2;
        public double? AutoscaleLimitValue { get; set; } = -50;
        public double? AutoscaleMinValue { get; set; } = 1;
    }
}

