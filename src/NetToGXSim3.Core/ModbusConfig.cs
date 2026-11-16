#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace NetToGXSim3.Core
{
    /// <summary>
    /// Konfigurasi Modbus TCP Server yang dapat disimpan dan dibaca kembali dari file modbus_config.json.
    /// </summary>
    public class ModbusConfig
    {
        public int Port { get; set; } = 5020;
        public bool IsZeroBased { get; set; } = true;
        public bool AutoStart { get; set; } = true;
        public byte SlaveId { get; set; } = 1;

        // Kamus deskripsi/keterangan yang disimpan per key device/address
        // Contoh key: "Coil_0", "Discrete_10", "Holding_100", atau nama device PLC "Y0", "M2000", "X0", "D0"
        public Dictionary<string, string> Descriptions { get; set; } = new Dictionary<string, string>();

        [JsonIgnore]
        public string StoragePath { get; private set; } = "modbus_config.json";

        public ModbusConfig()
        {
            DetermineStoragePath();
        }

        /// <summary>
        /// Menentukan lokasi penyimpanan file konfigurasi.
        /// Mengutamakan folder aplikasi lokal; jika ada batasan izin tulis, dialihkan ke AppData.
        /// </summary>
        private void DetermineStoragePath()
        {
            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modbus_config.json");
                using (var fs = File.Open(localPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    // Berhasil membuka file di direktori lokal dengan hak tulis
                }
                StoragePath = localPath;
            }
            catch
            {
                // Jika direktori instalasi Program Files diproteksi hak akses Windows UAC
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetToGXSim3");
                if (!Directory.Exists(appData))
                {
                    Directory.CreateDirectory(appData);
                }
                StoragePath = Path.Combine(appData, "modbus_config.json");
            }
        }

        /// <summary>
        /// Membaca konfigurasi dari file modbus_config.json jika file tersedia di disk.
        /// </summary>
        public static ModbusConfig Load()
        {
            var config = new ModbusConfig();

            if (File.Exists(config.StoragePath))
            {
                try
                {
                    string json = File.ReadAllText(config.StoragePath, Encoding.UTF8);
                    var loaded = JsonConvert.DeserializeObject<ModbusConfig>(json);
                    if (loaded != null)
                    {
                        config.Port = loaded.Port;
                        config.IsZeroBased = loaded.IsZeroBased;
                        config.AutoStart = loaded.AutoStart;
                        config.SlaveId = loaded.SlaveId;
                        if (loaded.Descriptions != null)
                        {
                            config.Descriptions = loaded.Descriptions;
                        }
                    }
                }
                catch
                {
                    // Jika terjadi kegagalan baca, nilai default tetap digunakan
                }
            }

            return config;
        }

        /// <summary>
        /// Menyimpan seluruh konfigurasi Modbus ke dalam file modbus_config.json.
        /// </summary>
        public bool Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(StoragePath) ?? string.Empty;
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(StoragePath, json, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
