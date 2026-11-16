#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetToGXSim3.Core
{
    /// <summary>
    /// Model item data baris untuk tabel pemetaan Modbus TCP Server.
    /// Mewakili satu titik coil, discrete input, holding register, atau input register.
    /// </summary>
    public class ModbusItemModel : INotifyPropertyChanged
    {
        private int _no;
        private int _modbusAddress;
        private string _plcDevice = string.Empty;
        private string _description = string.Empty;
        private string _valueText = "0";

        public int No
        {
            get => _no;
            set
            {
                if (_no != value)
                {
                    _no = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ModbusAddress
        {
            get => _modbusAddress;
            set
            {
                if (_modbusAddress != value)
                {
                    _modbusAddress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PlcDevice
        {
            get => _plcDevice;
            set
            {
                if (_plcDevice != value)
                {
                    _plcDevice = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ValueText
        {
            get => _valueText;
            set
            {
                if (_valueText != value)
                {
                    _valueText = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
