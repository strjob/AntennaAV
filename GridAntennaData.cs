using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntennaAV
{
    public class GridAntennaData
    {
        public double Angle { get; set; }
        public double PowerDbm { get; set; }
        public double Voltage { get; set; }
        public double PowerNorm { get; set; }
        public double VoltageNorm { get; set; }
        public DateTime Time { get; set; }

        public string AngleStr => Angle.ToString("F1");
        public string PowerDbmStr => PowerDbm.ToString("F2");
        public string VoltageStr => Voltage.ToString("F2");
        public string PowerNormStr => PowerNorm.ToString("F2");
        public string VoltageNormStr => VoltageNorm.ToString("F4");
        public string TimeStr => Time.ToString("mm:ss.ff");
    }
}
