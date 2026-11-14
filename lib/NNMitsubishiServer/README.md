# NNMitsubishiServer

Library C# .NET Standard 2.0 **100% Standalone Native (Murni C#)** untuk Virtual Server MC Protocol Mitsubishi MELSEC iQ-F (FX5U), FX3U, Q-Series, dan A-Series.
**Tanpa dependensi ke HslCommunication.dll atau library pihak ketiga lainnya!**

---

## 🚀 Penggunaan

### 1. Untuk Mitsubishi FX5U / Q-Series (MC Protocol Qna-3E Frame)
Gunakan kelas **`Fx5uServer`** (atau alias **`MelsecMcServer`**):
```csharp
using NNMitsubishiServer;

using (Fx5uServer server = new Fx5uServer(isBinary: true))
{
    // Jalankan server pada port default bebas konflik (port 6000 TCP)
    server.Start(port: 6000, isTcp: true);
    Console.WriteLine("FX5U Virtual Server (Qna-3E) aktif di port 6000.");

    // Tulis data awal
    server.WriteInt16("D100", 1234);
    server.WriteBool("M10", true);

    // Baca data
    short d100 = server.ReadInt16("D100");

    Console.ReadLine();
    server.Stop();
}
```

---

### 2. Untuk Mitsubishi FX3U / A-Series (MC Protocol A-1E Frame)
Gunakan kelas **`MelsecA1EServer`**:
```csharp
using NNMitsubishiServer;

using (MelsecA1EServer server = new MelsecA1EServer(isBinary: true))
{
    // Jalankan server pada port FX3U-ENET (misal 6000 TCP)
    server.Start(port: 6000, isTcp: true);
    Console.WriteLine("FX3U Virtual Server (A-1E) aktif di port 6000.");

    // Tulis data awal
    server.WriteInt16("D100", 5432);
    server.WriteBool("M20", true);

    // Baca data
    short d100 = server.ReadInt16("D100");

    Console.ReadLine();
    server.Stop();
}
```

---

## 📋 Perbandingan Server

| Spesifikasi | **`Fx5uServer`** / **`MelsecMcServer`** | **`MelsecA1EServer`** |
| :--- | :--- | :--- |
| **Target PLC** | **FX5U (MELSEC iQ-F)**, Q-Series, L-Series | **FX3U** (dengan modul FX3U-ENET), A-Series |
| **Protokol Framing** | **MC Protocol Qna-3E Frame** | **MC Protocol A-1E Frame** |
| **Format** | Binary & ASCII (TCP / UDP) | Binary & ASCII (TCP / UDP) |
| **Register Word** | `D`, `W`, `R`, `ZR` | `D`, `W`, `R` |
| **Register Bit** | `M`, `X`, `Y`, `L`, `B`, `S`, `F` (dan `D.bit`) | `M`, `X`, `Y`, `S`, `F`, `B` |

---

## 🛠️ Fitur Umum Kedua Server
- **100% Native C#**: Zero external dependencies (.NET Standard 2.0).
- **Fungsi Read/Write Lengkap**:
  - `ReadBool` / `WriteBool` & `ReadBoolArray`
  - `ReadInt16` / `WriteInt16` & `ReadInt16Array`
  - `ReadUInt16` / `WriteUInt16` & `ReadUInt16Array`
  - `ReadInt32` / `WriteInt32` & `ReadInt32Array`
  - `ReadUInt32` / `WriteUInt32` & `ReadUInt32Array`
  - `ReadFloat` / `WriteFloat` & `ReadFloatArray`
  - `ReadDouble` / `WriteDouble` & `ReadDoubleArray`
  - `ReadString` / `WriteString`
  - `ReadBytes` / `WriteBytes`
- **Persistensi Data**: `SaveMemoryToFile` dan `LoadMemoryFromFile`.
- **Event Handling**: `OnDataReceived` dan `OnLogMessage`.
