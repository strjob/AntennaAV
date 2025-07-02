using System;
using System.Collections.Generic;
using AntennaAV.ViewModels;

namespace AntennaAV.Services
{
    public class AntennaDiagramCollector
    {
        private readonly Dictionary<int, GridAntennaData> _data = new();
        private double _maxPowerDbm = double.NegativeInfinity;
        private double _maxVoltage = double.NegativeInfinity;

        public void Reset()
        {
            _data.Clear();
            _maxPowerDbm = double.NegativeInfinity;
            _maxVoltage = double.NegativeInfinity;
        }

        public void AddPoint(int receiverAngleDeg10, double powerDbm, DateTime timestamp)
        {
            if (receiverAngleDeg10 < 0 || receiverAngleDeg10 >= 3600)
                return;

            // Пересчёт PowerDbm -> Voltage (uV)
            double powerWatt = Math.Pow(10, (powerDbm - 30) / 10.0);
            double voltageRms = Math.Sqrt(powerWatt * 50);
            double voltageMicroV = voltageRms * 1_000_000;

            var data = new GridAntennaData
            {
                Angle = receiverAngleDeg10 / 10.0,
                PowerDbm = powerDbm,
                Voltage = voltageMicroV,
                Time = timestamp
            };
            _data[receiverAngleDeg10] = data;

            if (powerDbm > _maxPowerDbm) _maxPowerDbm = powerDbm;
            if (voltageMicroV > _maxVoltage) _maxVoltage = voltageMicroV;
        }

        public void FinalizeData()
        {
            // После завершения сбора — рассчитываем PowerNorm и VoltageNorm
            foreach (var key in _data.Keys)
            {
                var d = _data[key];
                d.PowerNorm = d.PowerDbm - _maxPowerDbm;
                d.VoltageNorm = _maxVoltage > 0 ? d.Voltage / _maxVoltage : 0;
                _data[key] = d;
            }
        }

        public Dictionary<int, GridAntennaData>.ValueCollection GetTableData()
        {
            //var list = new List<GridAntennaData>(_data.Values);
            var list = _data.Values;
            //list.Sort((a, b) => double.Parse(a.Angle).CompareTo(double.Parse(b.Angle)));
            return list;
        }

        public double[] GetGraphAngles()
        {
            var angles = new List<double>();
            foreach (var d in _data.Values)
                angles.Add(d.Angle);
            angles.Sort();
            return angles.ToArray();
        }

        public double[] GetGraphValues(Func<GridAntennaData, double> selector)
        {
            var values = new List<(double angle, double value)>();
            foreach (var d in _data.Values)
                values.Add((d.Angle, selector(d)));
            values.Sort((a, b) => a.angle.CompareTo(b.angle));
            var result = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
                result[i] = values[i].value;
            return result;
        }
    }
} 