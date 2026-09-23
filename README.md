# NetToGXSim3

**Mitsubishi GX Works 3 Simulator Network Protocol Bridge**  
*Developed by Ismail Lowkey*

[![Platform](https://img.shields.io/badge/Platform-Windows%20x86%20%7C%20x64-007acc.svg)]()
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple.svg)]()
[![Target](https://img.shields.io/badge/Target-MELSOFT%20GX%20Simulator%203-red.svg)]()
[![Protocol](https://img.shields.io/badge/Protocol-MC%20Protocol%20(3E%20%26%201E%20TCP%2FUDP)-green.svg)]()
[![License](https://img.shields.io/badge/License-MIT-gray.svg)]()

---

## 📥 Download Installer Terbaru

Unduh installer setup versi terbaru pada halaman [GitHub Releases](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3/releases/latest):

| Berkas | Platform | Tautan Unduhan |
| :--- | :--- | :--- |
| **NetToGXSim3 (Setup Installer)** | Windows 7 / 8 / 10 / 11 (32-bit / 64-bit) | [Download Setup (.exe)](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3/releases/latest) |

---

## 📖 Overview

**NetToGXSim3** is a lightweight, high-performance network bridge application that connects **Mitsubishi GX Simulator 3** (the simulation engine bundled with **GX Works 3** for FX5U, FX5UJ, FX5S, iQ-R, L series) to external Ethernet networks via the standard **MELSEC Communication (MC) Protocol** (TCP & UDP).

Traditionally, GX Simulator 3 runs as isolated simulation processes (`FSim3Dlg.exe`, `FSimRun3.exe`, `Sim3Dlg.exe`), making it impossible for external HMIs, SCADA packages, or IoT gateways to communicate with the simulated PLC without physical hardware. **NetToGXSim3** removes this limitation by connecting directly to the running GX Simulator 3 instance and hosting dual Ethernet TCP & UDP servers that translate MC Protocol requests into simulator memory operations with sub-millisecond response times.

> [!TIP]
> **Zero-Configuration (No MX Component Station Needed)**: Starting in **v0.5.0**, NetToGXSim3 features a **Direct Simulator Driver Engine**. You do **not** need to manually configure Logical Stations in MX Component Communication Setup Utility. NetToGXSim3 automatically discovers and binds directly to the active GX Simulator 3 engine!

---

## 🏛 Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Mitsubishi GX Works 3                    │
│                 (Ladder / FBD / ST Program)                 │
└──────────────────────────────┬──────────────────────────────┘
                               │ Virtual COM / IPC Bus
┌──────────────────────────────▼──────────────────────────────┐
│                    GX Simulator 3 Engine                    │
│       (FSimRun3.exe / Sim3Dlg.exe / Virtual FX5 / iQ-R)     │
└──────────────────────────────┬──────────────────────────────┘
                               │ Direct Shared Memory MMF (RX600.RAM / QXBUS.ASIC)
┌──────────────────────────────▼──────────────────────────────┐
│                        NetToGXSim3                          │
│  ┌─────────────────────────┐     ┌────────────────────────┐ │
│  │   MC Server 1 (TCP/UDP) │     │   MC Server 2 (TCP/UDP)│ │
│  │   (Default: Port 5001)  │     │   (Default: Port 6000) │ │
│  └────────────┬────────────┘     └───────────┬────────────┘ │
│               │ Pure On-Demand Passthrough   │              │
│               └──────────────┬───────────────┘              │
└──────────────────────────────┼──────────────────────────────┘
                               │ Ethernet TCP/UDP (MC Protocol)
    ┌──────────────────────────┴───────────────────┐
    │                                              │
┌───▼─────────────┐                      ┌─────────▼───────────┐
│  Weintek HMI    │                      │  Python / SCADA /   │
│ (EasyBuilder)   │                      │  OPC-UA / IoT       │
└─────────────────┘                      └─────────────────────┘
```

---

## 🔬 Cara Kerja GX Simulator 2 & GX Simulator 3 serta Arsitektur Memori Hex

### 1. Perbedaan Fundamental Arsitektur: GX Simulator 2 vs GX Simulator 3
| Dimensi | GX Simulator 2 (FX3U - GX Works 2) | GX Simulator 3 (FX5U / iQ-R - GX Works 3) |
| :--- | :--- | :--- |
| **Engine Executable** | `FXSimRun2.exe` / `SimManager.exe` | `FSimRun3.exe` (FX5U) / `RSimRun3.exe` (iQ-R) |
| **Model Memori** | **Private Heap Memory** di dalam proses virtual CPU | **Windows Shared Memory (Memory-Mapped Files / MMF)** |
| **Metode Akses** | `OpenProcess` + `ReadProcessMemory` / `WriteProcessMemory` via Master Pointer `0x0051DFAC` | `MemoryMappedFile.OpenExisting(...)` direct pointer mapping |
| **Lookup Alamat** | Compiled 18-byte struct table di VA `0x00442A70` | Direct hardware-mapped memory partitions |
| **Kecepatan / Latency** | ~1–2 µs (Win32 syscall context switch) | **Sub-microsecond (< 0.1 µs)** via direct RAM pointer dereferencing |
| **Monitoring GX Works** | Sepenuhnya independen | **Nol interferensi** dengan komunikasi online debug GX Works |

---

### 2. Cara Kerja GX Simulator 2 (`FXSimRun2.exe`) & Tabel Offset Hex

#### A. Model Private Heap & Master Pointer `0x0051DFAC`
Berbeda dengan GX Simulator 3 yang menggunakan kernel shared memory, GX Simulator 2 (`FXSimRun2.exe`) mengemulasikan CPU FX3U di dalam **Private Heap Memory** prosesnya sendiri (~680 KB contiguous RAM buffer).

Alamat dasar (*base address*) dari buffer RAM virtual ini dialokasikan secara dinamis saat simulasi dinyalakan, namun alamat pointer-nya disimpan pada alamat statis di data segment `FXSimRun2.exe`:
- **Master Pointer Virtual Address**: **`0x0051DFAC`**

Software membaca master pointer ini menggunakan Win32 API:
```csharp
// Membaca pointer dasar virtual CPU RAM FX3U
IntPtr pBase = (IntPtr)ReadProcessMemoryInt32(hProcess, (IntPtr)0x0051DFAC);
```
Dari alamat `pBase` tersebut, seluruh register PLC (X, Y, M, D, R, dll.) berada pada offset byte yang deterministik.

#### B. Penemuan Struktur Tabel Perangkat (VA `0x00442A70`)
Bagaimana software mengetahui letak offset setiap perangkat secara presisi? Di dalam binary `FXSimRun2.exe`, Mitsubishi menyertakan tabel deskriptor perangkat terkompilasi pada Virtual Address **`0x00442A70`**. Setiap entri berukuran **18 byte**:
- **4 byte**: ASCII Device Tag (contoh: `"D   "`, `"M   "`, `"R   "`, `"TI  "`, `"TO  "`, `"CI  "`, `"CO  "`, `"CHN "`)
- **2 byte**: Tipe Device Code internal
- **4 byte**: **Word Offset** (dikalikan 2 untuk mendapatkan Byte Offset)
- **4 byte**: Kapasitas / batas jumlah perangkat
- **4 byte**: Flag / atribut akses

#### C. Tabel Lengkap Offset Hex Memori GX Simulator 2 (`FXSimRun2`)
| Device | Nama / Fungsi | Word Offset (Hex) | Byte Offset (Hex) | Byte Offset (Desimal) | Format / Tipe Data | Kapasitas Standar FX3U |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **X** | Input Relays | `0x0000` | `0x000000` | 0 | Octal Bit buffer | X0 – X377 (oktal) |
| **Y** | Output Relays | `0x0010` | `0x000020` | 32 | Octal Bit buffer | Y0 – Y377 (oktal) |
| **PY** | Output Latch / Force | `0x0020` | `0x000040` | 64 | Octal Bit buffer | Force Latch Y |
| **M** | Internal Relays | `0x0030` | `0x000060` | 96 | 1-bit per relay | M0 – M3071 (384 byte) |
| **PM** | Relay Force Table | `0x0210` | `0x000420` | 1.056 | 1-bit per relay | Force Latch M |
| **SM** | Special Relays | `0x03F0` | `0x0007E0` | 2.016 | 1-bit per relay | SM0 – SM255 (32 byte) |
| **S** | State Relays (SFC) | `0x0430` | `0x000860` | 2.144 | 1-bit per relay | S0 – S999 (125 byte) |
| **TS (TI)** | Timer Contact (Bit) | `0x0540` | `0x000A80` | 2.688 | 1-bit per kontak | T0 – T255 (32 byte) |
| **TC (TO)** | Timer Coil (Bit) | `0x0570` | `0x000AE0` | 2.784 | 1-bit per coil | T0 – T255 (32 byte) |
| **TN** | Timer Current Value | `0x05A0` | `0x000B40` | 2.880 | 16-bit Word | T0 – T255 (512 byte) |
| **CS (CI)** | Counter Contact (Bit) | `0x0B00` | `0x001600` | 5.632 | 1-bit per kontak | C0 – C255 (32 byte) |
| **CC (CO)** | Counter Coil (Bit) | `0x0B10` | `0x001620` | 5.664 | 1-bit per coil | C0 – C255 (32 byte) |
| **CN** | 16-bit Counter Value | `0x0B20` | `0x001640` | 5.696 | 16-bit Word | C0 – C199 (400 byte) |
| **CHN** | 32-bit Counter Value | `0x0BE8` | `0x0017D0` | 6.096 | **32-bit DWord (2 words)** | C200 – C255 (224 byte) |
| **D** | Data Registers | `0x01000` | `0x002000` | 8.192 | 16-bit Word | D0 – D7999 (16.000 byte) |
| **SD** | Special Data Registers| `0x03000` | `0x006000` | 24.576 | 16-bit Word | SD0 – SD255 (RTC: SD13-SD18) |
| **B** | Link Relays | `0x004700` | `0x008E00` | 36.352 | Heksadesimal Bit | B0 – B1FFF |
| **W** | Link Registers | `0x004800` | `0x009000` | 36.864 | Heksadesimal Word | W0 – W1FFF |
| **R** | File Registers | `0x044E00` | **`0x089C00`** | **564.224** | 16-bit Word | R0 – R6999 (14.000 byte) |

> [!IMPORTANT]
> **Catatan Krusial Register R**: Word offset R adalah `0x44E00` (bukan `0x4E00`). Dalam byte offset, letaknya berada di `0x089C00` (desimal 564.224 byte dari `pBase`). Menggunakan `0x4E00` akan menyebabkan overlap ke area register lain.
>
> **Catatan Counter 32-bit CHN (`C200 - C255`)**: Counter `C200` s/d `C255` pada PLC FX3U bertipe 32-bit bertanda (signed 32-bit integer). Nilai counter disimpan dalam format DWord (4 byte / 2 word per nomor counter) mulai dari offset `0x0017D0`.

---

### 3. Cara Kerja GX Simulator 3 (`FSimRun3.exe` / `RSimRun3.exe`) & Tabel Offset Hex

#### A. Dual Shared Memory MMF Section
Saat simulasi diaktifkan di GX Works 3 (**Debug** → **Start Simulation**), engine secara otomatis membuat dua kernel shared memory mapped files tingkat OS:
1. **`GXS3.SYS01.CPU01.RX600.RAM`**:
   - Merepresentasikan **RAM fisik prosesor Renesas RX600** (mikrokontroler hardware embedded FX5U).
   - Seluruh memori kerja PLC berada di sini: Data Registers (`D`), Internal Relays (`M`), Input Relays (`X`), Output Relays (`Y`), dan Simulation Force Tables.
2. **`GXS3.SYS01.QXBUS.PHS00.ASIC`**:
   - Merepresentasikan **ASIC Physical I/O Hardware Bus** (emulasi terminal sekrup hardware fisik PLC).

#### B. Peta Offset Memori Hex (`RX600.RAM` & `QXBUS.ASIC`)
| Device | Lokasi / Bagian MMF | Offset (Hex) | Offset (Desimal) | Ukuran / Kapasitas | Deskripsi |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **D** | `RX600.RAM` | **`0x061A50`** | 399.952 | 16.000 byte (8.000 words) | Data Register D0 s/d D7999 |
| **X (RAM)** | `RX600.RAM` | **`0x066000`** | 417.792 | Octal bit buffer | Base Input Relays di RAM CPU |
| **X (Force)** | `RX600.RAM` | **`0x066184`** | 418.180 | Octal bit buffer | Tabel Force Latch Input GX Works 3 |
| **X (ASIC)** | `QXBUS.PHS00.ASIC` | **`0x001510`** | 5.392 | Octal bit buffer | Physical Input Image di Hardware Bus ASIC |
| **Y (RAM)** | `RX600.RAM` | **`0x066204`** | 418.308 | Octal bit buffer | Base Output Relays di RAM CPU |
| **Y (Force)** | `RX600.RAM` | **`0x066388`** | 418.696 | Octal bit buffer | Tabel Force Latch Output GX Works 3 |
| **M** | `RX600.RAM` | **`0x066408`** | 418.824 | 512 byte (4.096 bit) | Internal Relay M0 s/d M4095 |

#### C. Mekanisme "Triple-Write" pada Input Digital (X)
Di GX Simulator 3 (`FSimRun3`), virtual CPU menjalankan scan loop hardware secara real-time:
1. Scan pin input dari ASIC bus
2. Eksekusi Ladder logic
3. Tulis output ke ASIC bus

Jika software pihak ketiga hanya menulis ke `OFFSET_X_RAM` (`0x066000`), siklus scan hardware berikutnya akan langsung menimpa bit kembali menjadi OFF karena ASIC bus masih bernilai 0.
Untuk mengatasinya, software melakukan **Triple-Write** serentak:
```csharp
// 1. Tulis ke Base RAM CPU
_ram.Write(OFFSET_X_RAM + bytePos, bRam);

// 2. Tulis ke Force Latch Table (simulasi Force ON di GX Works 3)
_ram.Write(OFFSET_X_FORCE + bytePos, bForce);

// 3. Tulis ke Emulasi Chipset ASIC Bus Fisik
if (_asic != null) {
    _asic.Write(OFFSET_X_ASIC + bytePos, bAsic);
}
```
Hasilnya, input X dapat dinyalakan dan dimatikan secara stabil dari HMI atau script eksternal tanpa mental kembali ke OFF.

---

### 4. Cara Software Ini Membaca & Menulis Address GX Simulator (Pure On-Demand Passthrough)

#### A. Konsep Pure On-Demand Passthrough (Nol Polling Background)
Software ini didesain dengan filosofi **Zero CPU Overhead**:
- Software **TIDAK** melakukan polling berkala atau menduplikasi (mirroring) seluruh isi memori PLC ke memori lokal secara terus-menerus di background.
- Penggunaan CPU 0% saat tidak ada request jaringan.
- Memori simulator hanya dibaca atau ditulis secara instan tepat pada saat ada request yang masuk dari client MC Protocol.

#### B. Alur Eksekusi End-to-End dari Request Jaringan hingga Balasan
```
Client (HMI / SCADA)               NetToGXSim Server                    GX Simulator Engine
       │                                   │                                     │
       │─── 1. MC Protocol Request ───────>│                                     │
       │    (Contoh: Batch Read D100, 10W) │                                     │
       │                                   ├─── 2. Parse Frame (Device & Offset) │
       │                                   │    Hitung Target Hex Address        │
       │                                   │                                     │
       │                                   ├─── 3. Direct Memory Read ──────────>│
       │                                   │    (MMF Pointer atau Win32 RPM)     │
       │                                   │<── 4. Kembalikan 20 Byte Raw ───────│
       │                                   │                                     │
       │                                   ├─── 5. Kemas Respons MC Protocol     │
       │<── 6. MC Protocol Response ───────│    (Status 0x0000 + Binary Data)    │
```

1. **Penerimaan Paket Jaringan**: Listener TCP/UDP menerima frame MC Protocol (misalnya QnA 3E binary command `0x0401` untuk membaca 10 word mulai `D100`).
2. **Parsing Protokol**: Parser membaca header, mendeteksi Device Code (`0xA8` untuk D), nomor alamat awal (`100`), dan jumlah pembacaan (`10` word).
3. **Kalkulasi Offset Memori Hex**:
   - **Pada GX Simulator 3**: `TargetAddress = BaseOffset (0x061A50) + (100 * 2) = 0x061B18`.
   - **Pada GX Simulator 2**: `TargetAddress = pBase + DeviceOffset (0x002000) + (100 * 2)`.
4. **Pembacaan Memori Langsung**:
   - **Pada GX Simulator 3**: Menggunakan `MemoryMappedViewAccessor.ReadArray` langsung dari buffer kernel shared memory (< 0.1 µs).
   - **Pada GX Simulator 2**: Memanggil Win32 `ReadProcessMemory` langsung ke heap `FXSimRun2.exe` via Master Pointer `0x0051DFAC` (~1 µs).
5. **Serialisasi Respons**: Data 20 byte mentah dipasangkan dengan header MC Protocol standar (Subheader `0xD0 0x00`, End Code `0x00 0x00`), kemudian langsung dikirimkan kembali ke socket client. Seluruh alur selesai dalam waktu **< 0.5 milidetik**.

#### C. Penanganan Khusus Format Addressing & Bit Masking
- **Addressing Oktal (X & Y)**: Perangkat X dan Y di Mitsubishi menggunakan penomoran oktal (`0..7`, `10..17`, `20..27`). Software secara otomatis memetakan nomor oktal ke indeks bit absolut:
  ```csharp
  int bitIndex = Convert.ToInt32(addressStr, 8); // Base-8 parsing
  int bytePos = bitIndex / 8;
  int bitOffset = bitIndex % 8;
  ```
- **Addressing Heksadesimal (W & B)**: Perangkat link register W dan link relay B diparsing menggunakan format heksadesimal (`Base-16`).
- **Atomic Bit Manipulation**: Saat menulis 1 bit (misal menyalakan `M100`), software membaca 1 byte target, memodifikasi bit yang dituju menggunakan bitwise OR/AND (`byteVal | (1 << bitOffset)` atau `byteVal & ~(1 << bitOffset)`), lalu menulisnya kembali sehingga 7 bit tetangga di dalam byte tersebut tetap aman dan tidak berubah.
- **Support 32-bit DWord Counter**: Untuk counter `C200 - C255`, software membaca 4 byte (2 word) sekaligus agar nilai counter 32-bit tidak terpotong (truncated).

---

## ✨ Features

- 🚀 **Direct GX Simulator 3 Driver Connection (New in v0.5.0)**:
  - Connects directly to `FSimRun3.exe` / `Sim3Dlg.exe` via dedicated direct simulator adapter.
  - Zero-configuration: no manual station creation, no database setup, no complex registry tuning.
  - Intelligent multi-mode auto-probe (FX5 Series, iQ-R Series, Q/L Series).

- ⚡ **Dual MC Protocol Servers (TCP & UDP)**:
  - **Server 1**: Default port 5000 (auto-increments to 5001 if port 5000 is occupied). Started automatically at launch.
  - **Server 2**: Default port 6000. Stopped by default for secondary clients or testing.
  - Independent TCP & UDP socket listeners running simultaneously.

- 🛡️ **Intelligent Port Conflict Resolution**:
  - Automatically detects port occupancy (e.g., Windows `svchost.exe` / IP Helper on port 5000).
  - Automatically steps up to the next free port (`port++`) and updates the UI in real-time.

- 📡 **Comprehensive MC Protocol Support**:
  - **QnA 3E Binary & ASCII**:
    - `0x0101`: CPU Model Type Read (returns `Q02UCPU` / `FX3U` identification for seamless HMI handshakes).
    - `0x0401`: Batch Read (Bit & Word units).
    - `0x0403`: Random Read (Word & DWord multi-address reading).
    - `0x1401`: Batch Write (Bit & Word units).
    - `0x1402`: Random Write (Word/DWord & Bit units).
  - **A-1E Binary & ASCII** (Legacy FX & A Series compatibility):
    - Subcommands `0x00` (Read Bits), `0x01` (Read Words), `0x02` (Write Bits), `0x03` (Write Words).

- 🎛️ **Interactive Hardware Simulation Panel**:
  - **Inputs Rack (X0 - X7)**: 8 interactive toggle switches with blue LED status indicators to force digital inputs directly from the UI.
  - **Outputs Rack (Y0 - Y7)**: 8 glowing green LED indicator lamps reflecting PLC coil states in real-time.

- 🔍 **Direct Register Inspector**:
  - Inspect any PLC device address (`D0`, `M100`, `Y0`, `X0`, etc.) on demand.
  - Write single-word values directly to memory registers.

- 🛑 **GX Simulator 3 Process Manager**:
  - Built-in **"Exit GX Simulator 3"** button to cleanly terminate background simulator processes (`FSim3Dlg.exe`, `FSimRun3.exe`, `Sim3Dlg.exe`, etc.).

- 🔄 **Automated Update Checker**:
  - Background GitHub Releases integration that alerts you when a new version is available.

---

## 📋 System Requirements

| Requirement | Specification |
| :--- | :--- |
| **Operating System** | Windows 7 / 8 / 10 / 11 (32-bit or 64-bit) |
| **Runtime** | .NET Framework 4.7.2 or higher |
| **PLC Software** | Mitsubishi **GX Works 3** (with GX Simulator 3 installed) |
| **Dependencies** | **Standalone** (Zero MX Component or external DLL dependencies required) |

---

## 🚀 Quick Start Guide

### Step 1: Start Simulation in GX Works 3
1. Open your project in **GX Works 3**.
2. Start simulation: navigate to **Debug** → **Start Simulation**.
3. Ensure the simulation window (`FSim3Dlg.exe` / `Sim3Dlg.exe`) appears and GX Works 3 is in **Monitor Mode**.

### Step 2: Launch NetToGXSim3
1. Run `NetToGXSim3.Wpf.exe` (or use the desktop shortcut after installation).
2. Verify that the status header badge shows **`GX Sim 3: Connected`** (green).
3. Verify that **Server 1** shows **`Running on Port 5001`** (or 5000 if unoccupied).

### Step 3: Connect External HMI / SCADA

#### Example: Weintek EasyBuilder Pro
1. In EasyBuilder Pro, open **System Parameters** → **New Device**.
2. Configure device settings:
   - **PLC Type**: `Mitsubishi Q/QnA (Ethernet)` or `Mitsubishi FX3U/FX5U (Ethernet)`
   - **Interface**: `Ethernet`
   - **IP Address**: `127.0.0.1` (or your host IP)
   - **Port**: `5001` (match the port shown on NetToGXSim3 Server 1)
   - **Protocol**: `TCP/IP, Binary Mode`
3. Launch **On-line Simulation** in EasyBuilder Pro.

#### Example: Python (pymcprotocol)
```python
import pymcprotocol

# Initialize QnA 3E client
mc = pymcprotocol.Type3E()
mc.setaccessopt(commtype="binary")
mc.connect("127.0.0.1", 5001)

# Read 10 words from D0 to D9
values = mc.batchread_wordunits(headdevice="D0", readsize=10)
print(f"D0-D9: {values}")

# Write to D0
mc.batchwrite_wordunits(headdevice="D0", values=[1234])
```

---

## 🛠 Building from Source

```powershell
# Clone the repository
git clone https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3.git
cd Mitsubishi-NetToGXSim3

# Build the solution (x86 platform target)
dotnet build NetToGXSim3.sln -c Release -p:Platform=x86

# Publish binaries
dotnet publish src/NetToGXSim3.Wpf/NetToGXSim3.Wpf.csproj -c Release -o publish

# Create NSIS Installer
.\create_installer.bat
```

---

## 👤 Author & Support

- **Developer**: Ismail Lowkey
- **Repository**: [ismaillowkey/Mitsubishi-NetToGXSim3](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3)
- **Version**: 0.6.3

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
