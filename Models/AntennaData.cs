using System;

namespace AntennaAV.Models
{
    // Модель данных, получаемых от антенной установки через COM порт
    // Содержит углы позиционирования, измеренные значения мощности и служебную информацию
    public class AntennaData
    {
        // Угол приемной антенны в десятых долях градуса
        public int ReceiverAngleDeg10 { get; set; }
        
        // Угол передающей антенны в десятых долях градуса
        public int TransmitterAngleDeg10 { get; set; }
        
        // Угол приемной антенны в градусах
        public double ReceiverAngleDeg { get; set; }
        
        // Угол передающей антенны в градусах
        public double TransmitterAngleDeg { get; set; }
        
        // Счетчик оборотов приемной антенны для защиты от перекручивания кабеля
        public int RxAntennaCounter { get; set; }
        
        // Счетчик оборотов передающей антенны для защиты от перекручивания кабеля
        public int TxAntennaCounter { get; set; }
        
        // Измеренная мощность сигнала в дБм
        public double PowerDbm { get; set; }
        
        // Измеренное напряжение в микровольтах
        public double Voltage { get; set; }
        
        // Тип антенны/режим работы: 0=УКВ СИНХР, 1=УКВ, 2=СВЧ СИНХР, 3=СВЧ
        public int AntennaType { get; set; } = 4;
        
        // Режим работы: 0=ручной, 1=автоматический
        public int ModeAutoHand { get; set; } = 4;
        
        // Системный счетчик времени от устройства
        public int Systick { get; set; }
        
        // Состояние генератора: 0=включен, 1=выключен
        public int GenOnOff { get; set; }
    }
}


