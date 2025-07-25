﻿using System;

namespace AntennaAV.Models
{
    public class AntennaData
    {
        public int ReceiverAngleDeg10 { get; set; }
        public int TransmitterAngleDeg10 { get; set; }
        public double ReceiverAngleDeg { get; set; }
        public double TransmitterAngleDeg { get; set; }
        public int RxAntennaCounter { get; set; }
        public int TxAntennaCounter { get; set; }
        public double PowerDbm { get; set; }
        public double Voltage { get; set; }
        public int AntennaType { get; set; }
        public int ModeAutoHand { get; set; }
        public int Systick { get; set; }
    }
}


