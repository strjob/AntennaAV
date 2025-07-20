using System;
using System.Collections.Generic;

namespace AntennaAV.Services
{

    public class AntennaDiagramCollector
    {
        private readonly Dictionary<int, GridAntennaData> _data = new();
        private readonly SortedList<DateTime, int> _timeToAngleIndex = new(); // Автоматически сортирует по времени!
        private double _maxPowerDbm = double.NegativeInfinity;
        private double _maxVoltage = double.NegativeInfinity;

        // Отслеживание изменений максимумов
        private double _lastNormalizedMaxPower = double.NegativeInfinity;
        private double _lastNormalizedMaxVoltage = double.NegativeInfinity;
        private bool _powerMaxChanged = false;
        private bool _voltageMaxChanged = false;

        // Кэширование
        private List<GridAntennaData>? _cachedTableData;
        private bool _tableDataInvalidated = true;
        private double[]? _cachedAngles;
        private bool _anglesInvalidated = true;

        public void Reset()
        {
            _data.Clear();
            _timeToAngleIndex.Clear();
            _maxPowerDbm = double.NegativeInfinity;
            _maxVoltage = double.NegativeInfinity;
            _lastNormalizedMaxPower = double.NegativeInfinity;
            _lastNormalizedMaxVoltage = double.NegativeInfinity;
            _powerMaxChanged = false;
            _voltageMaxChanged = false;
            InvalidateCache();
        }

        public void AddPoint(int receiverAngleDeg10, double powerDbm, DateTime timestamp)
        {
            if (receiverAngleDeg10 < 0 || receiverAngleDeg10 >= 3600)
                return;

            // Пересчёт PowerDbm -> Voltage (uV)
            double powerWatt = Math.Pow(10, (powerDbm - 30) / 10.0);
            double voltageRms = Math.Sqrt(powerWatt * 50);
            double voltageMicroV = Math.Round(voltageRms * 1_000_000, 1);

            // Проверяем, изменились ли максимумы
            bool powerMaxChangedNow = powerDbm > _maxPowerDbm;
            bool voltageMaxChangedNow = voltageMicroV > _maxVoltage;

            if (powerMaxChangedNow)
            {
                _maxPowerDbm = powerDbm;
                _powerMaxChanged = true;
            }
            if (voltageMaxChangedNow)
            {
                _maxVoltage = voltageMicroV;
                _voltageMaxChanged = true;
            }

            var data = new GridAntennaData
            {
                Angle = Math.Round(receiverAngleDeg10 / 10.0, 1),
                PowerDbm = powerDbm,
                Voltage = voltageMicroV,
                Time = timestamp
            };

            // Если максимумы не изменились, сразу вычисляем нормализованные значения
            if (!powerMaxChangedNow && !voltageMaxChangedNow && _lastNormalizedMaxPower != double.NegativeInfinity)
            {
                data.PowerNorm = data.PowerDbm - _lastNormalizedMaxPower;
                data.VoltageNorm = _lastNormalizedMaxVoltage > 0 ? Math.Round(data.Voltage / _lastNormalizedMaxVoltage, 3) : 0;
            }

            // Обновляем индексы
            if (_data.ContainsKey(receiverAngleDeg10))
            {
                var oldData = _data[receiverAngleDeg10];
                _timeToAngleIndex.Remove(oldData.Time); // Удаляем старую запись
            }

            _data[receiverAngleDeg10] = data;
            _timeToAngleIndex[timestamp] = receiverAngleDeg10; // SortedList автоматически сортирует!

            InvalidateCache();
        }

        public NormalizationResult FinalizeData()
        {
            var result = new NormalizationResult
            {
                PowerMaxChanged = _powerMaxChanged,
                VoltageMaxChanged = _voltageMaxChanged,
                ItemsProcessed = 0
            };

            if (!_powerMaxChanged && !_voltageMaxChanged)
                return result;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (_powerMaxChanged)
            {
                foreach (var key in _data.Keys)
                {
                    var d = _data[key];
                    d.PowerNorm = d.PowerDbm - _maxPowerDbm;
                    _data[key] = d;
                }
                _lastNormalizedMaxPower = _maxPowerDbm;
                _powerMaxChanged = false;
            }

            if (_voltageMaxChanged)
            {
                foreach (var key in _data.Keys)
                {
                    var d = _data[key];
                    d.VoltageNorm = _maxVoltage > 0 ? Math.Round(d.Voltage / _maxVoltage, 3) : 0;
                    _data[key] = d;
                }
                _lastNormalizedMaxVoltage = _maxVoltage;
                _voltageMaxChanged = false;
            }

            result.ItemsProcessed = _data.Count;
            result.TimeMs = sw.ElapsedMilliseconds;
            InvalidateCache();

            return result;
        }

        public List<GridAntennaData> GetTableData()
        {
            if (_tableDataInvalidated || _cachedTableData == null)
            {
                // Используем уже отсортированный SortedList - БЕЗ дополнительной сортировки!
                _cachedTableData = new List<GridAntennaData>(_timeToAngleIndex.Count);

                // Проходим в обратном порядке (самые новые сначала)
                for (int i = _timeToAngleIndex.Count - 1; i >= 0; i--)
                {
                    var angleIndex = _timeToAngleIndex.Values[i];
                    _cachedTableData.Add(_data[angleIndex]);
                }

                _tableDataInvalidated = false;
            }
            return _cachedTableData;
        }

        public double[] GetGraphAngles()
        {
            if (_anglesInvalidated || _cachedAngles == null)
            {
                var angles = new List<double>(_data.Count);
                foreach (var d in _data.Values)
                    angles.Add(d.Angle);
                angles.Sort(); // Здесь сортировка всё ещё нужна (по углу, не по времени)
                _cachedAngles = angles.ToArray();
                _anglesInvalidated = false;
            }
            return _cachedAngles;
        }

        public double[] GetGraphValues(Func<GridAntennaData, double> selector)
        {
            // Для графика данные нужно сортировать по углу
            var values = new List<(double angle, double value)>(_data.Count);
            foreach (var d in _data.Values)
                values.Add((d.Angle, selector(d)));
            values.Sort((a, b) => a.angle.CompareTo(b.angle));

            var result = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
                result[i] = values[i].value;
            return result;
        }

        private void InvalidateCache()
        {
            _tableDataInvalidated = true;
            _anglesInvalidated = true;
        }
    }

    public struct NormalizationResult
    {
        public bool PowerMaxChanged;
        public bool VoltageMaxChanged;
        public int ItemsProcessed;
        public long TimeMs;

        public bool HasChanges => PowerMaxChanged || VoltageMaxChanged;
    }
} 