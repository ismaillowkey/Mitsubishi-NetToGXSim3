# NetToGXSim3

**Mitsubishi GX Works 3 Simulator Network Protocol Bridge, OPC DA, OPC UA & Modbus TCP Server**  
*Developed by Ismail Lowkey*

[![Platform](https://img.shields.io/badge/Platform-Windows%20x86%20%7C%20x64-007acc.svg)]()
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple.svg)]()
[![Target](https://img.shields.io/badge/Target-MELSOFT%20GX%20Simulator%203-red.svg)]()
[![Protocol](https://img.shields.io/badge/Protocol-MC%20Protocol%20(3E%20%26%201E%20TCP%2FUDP)-green.svg)]()
[![OPC DA](https://img.shields.io/badge/OPC%20DA-Classic%20COM%202.05a-blue.svg)]()
[![OPC UA](https://img.shields.io/badge/OPC%20UA-opc.tcp%2048400-teal.svg)]()
[![Modbus TCP](https://img.shields.io/badge/Modbus%20TCP-Port%205020-amber.svg)]()
[![Version](https://img.shields.io/badge/Version-v0.8.1-orange.svg)]()
[![License](https://img.shields.io/badge/License-MIT-gray.svg)]()

---

## 📥 Download Installer Terbaru

Unduh installer setup versi terbaru pada halaman [GitHub Releases](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3/releases/latest):

| Berkas | Platform | Tautan Unduhan |
| :--- | :--- | :--- |
| **NetToGXSim3 (Setup Installer)** | Windows 7 / 8 / 10 / 11 (32-bit / 64-bit) | [Download Setup (.exe)](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3/releases/latest) |

---

## 🌐 Ismail Lowkey Ecosystem Apps ("My Apps")

1. **Mitsubishi Net To GXsim2**: [https://github.com/ismaillowkey/Mitsubishi-NetToGXSim2](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim2) — Protocol Bridge & OPC DA Server for Mitsubishi GX Simulator 2 (GX Works 2 / FX3U).
2. **Mitsubishi Net To GXsim3**: [https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3) — Protocol Bridge & OPC DA Server for Mitsubishi GX Simulator 3 (GX Works 3 / FX5U / iQ-R).
3. **Omron Net To Cx Sim**: [https://github.com/ismaillowkey/OmronNetToCXSim](https://github.com/ismaillowkey/OmronNetToCXSim) — FINS Network Bridge & OPC DA Server for Omron CX-Simulator.
4. **MPS Pneumatic Simulator**: [https://github.com/ismaillowkey/MPSPneumaticSimulator](https://github.com/ismaillowkey/MPSPneumaticSimulator) — Interactive Mechatronics & Pneumatic MPS Station Simulator.

---

## 📖 Overview

**NetToGXSim3** is a high-performance network protocol bridge, **OPC DA 2.05a Classic COM Server**, **OPC UA Server (`opc.tcp`)**, and **Modbus TCP Server** designed to connect **Mitsubishi GX Simulator 3** (the built-in simulation engine for **GX Works 3**, supporting FX5U, FX5UJ, FX5S, iQ-R, L-series) to external Ethernet networks, HMIs, and SCADA systems.

Traditionally, GX Simulator 3 runs as isolated desktop processes (`FSim3Dlg.exe`, `FSimRun3.exe`, `Sim3Dlg.exe`, `RSimRun3.exe`), making it impossible for external HMIs, SCADA packages, or IoT gateways to communicate with the simulated PLC without physical hardware. **NetToGXSim3** eliminates this limitation by connecting directly to the running GX Simulator 3 runtime via Windows Shared Memory (Memory-Mapped Files / MMF) with sub-microsecond latency, hosting dual Ethernet MC Protocol servers (TCP & UDP), an OPC UA Server, an OPC DA Classic Server, and a Modbus TCP Server without requiring any external drivers or third-party middleware.

> [!TIP]
> **Zero-Configuration (Direct Simulator Driver Engine)**: NetToGXSim3 communicates directly with GX Simulator 3 shared memory. No manual station setup, no external communication drivers, and zero complex middleware required!

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
│  │   (Default: Port 5001)  │     │   (Default: Port 5002) │ │
│  └─────────────────────────┘     └────────────────────────┘ │
│  ┌─────────────────────────┐     ┌────────────────────────┐ │
│  │  OPC DA 2.05a Classic   │     │  OPC UA Server         │ │
│  │  (COM: NetToGXSim3.DA)  │     │  (opc.tcp://:48400)    │ │
│  └─────────────────────────┘     └────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Modbus TCP Server (Port 5020, FC 01/02/03/04/05/06/15)│ │
│  └────────────────────────────────────────────────────────┘ │
│               Pure On-Demand Direct Passthrough             │
└──────────────────────────────┬──────────────────────────────┘
                               │ Ethernet & Local COM
    ┌──────────────────────────┼───────────────────────────┐
    │                          │                           │
┌───▼─────────────┐  ┌─────────▼───────────┐     ┌─────────▼───────────┐
│  Weintek HMI /  │  │  Python / Node-RED  │     │  SCADA / OPC Client │
│  Modbus Clients │  │  pymc / pymodbus    │     │  UaExpert / Kepware │
└─────────────────┘  └─────────────────────┘     └─────────────────────┘
```

---

## 🔬 GX Simulator 3 Shared Memory Architecture & Hex Offsets

### 1. Dual Shared Memory MMF Sections
When simulation starts in GX Works 3 (**Debug** → **Start Simulation**), the simulation engine dynamically creates two kernel-level shared memory mapped files in Windows:

1. **`GXS3.SYS01.CPU01.RX600.RAM`**:
   - Emulates the physical RAM of the **Renesas RX600 CPU** (the core embedded microcontroller powering the FX5U hardware).
   - Houses all operational PLC memory: Data Registers (`D`), Internal Relays (`M`), Latch Relays (`L`), Extension / File Registers (`R` / `ZR`), Link Registers (`W`), Base Input Relays (`X`), Base Output Relays (`Y`), and Force Latch Tables.
2. **`GXS3.SYS01.QXBUS.PHS00.ASIC`**:
   - Emulates the **ASIC Physical I/O Hardware Bus** (the physical ASIC bus interfacing terminal screw blocks for digital inputs and outputs).

### 2. Detailed Memory Offset Map (`RX600.RAM` & `QXBUS.ASIC`)
| Device | Location / MMF Section | Offset (Hex) | Offset (Decimal) | Size / Capacity | Description |
|:---|:---|:---|:---|:---|:---|
| **M** | `RX600.RAM` | **`0x060000`** | 393,216 | 960 bytes (7,680 bits) | Internal Relays (M0 to M7679) |
| **L** | `RX600.RAM` | **`0x0603C0`** | 394,176 | 960 bytes (7,680 bits) | Latch Relays (L0 to L7679) |
| **W** | `RX600.RAM` | **`0x061450`** | 398,416 | 1,024 bytes (512 words) | Link Registers (W0 to W511) |
| **D** | `RX600.RAM` | **`0x061A50`** | 399,952 | 16,000 bytes (8,000 words)| Data Registers (D0 to D7999) |
| **R / ZR**| `RX600.RAM` | **`0x00BE80`** | 48,768 | 65,534 bytes (32,767 words)| Extension / File Registers (R0 to R32767) |
| **X (RAM)** | `RX600.RAM` | **`0x066000`** | 417,792 | Octal bit buffer | Base Input Relays in CPU RAM |
| **X (Force)** | `RX600.RAM` | **`0x066184`** | 418,180 | Octal bit buffer | GX Works 3 Simulation / Force Latch Table |
| **X (ASIC)** | `QXBUS.PHS00.ASIC` | **`0x001510`** | 5,392 | Octal bit buffer | Physical Input Image in ASIC Hardware Bus |
| **Y (RAM)** | `RX600.RAM` | **`0x066204`** | 418,308 | Octal bit buffer | Base Output Relays in CPU RAM |
| **Y (Force)** | `RX600.RAM` | **`0x066388`** | 418,696 | Octal bit buffer | GX Works 3 Force Output Latch Table |

### 3. The "Triple-Write" Mechanism for Physical Inputs (X)
In the FX5U simulation engine (`FSimRun3`), the virtual CPU executes a real-time hardware scan loop identical to physical PLC hardware:
1. Scan physical input pins from ASIC bus
2. Execute ladder program logic
3. Write physical output pins to ASIC bus

**The Problem**:
If an external tool only writes to `OFFSET_X_RAM` (`0x066000`), the virtual CPU's physical scan loop overwrites the bit back to OFF on the very next millisecond cycle because the emulated ASIC bus still reads 0.

**The Solution implemented in NetToGXSim3**:
NetToGXSim3 executes a synchronized **Triple-Write**:
```csharp
// 1. Write to CPU Base RAM Table
_ram.Write(OFFSET_X_RAM + bytePos, bRam);

// 2. Write to GX Works 3 Force Latch Table (simulates ladder force table)
_ram.Write(OFFSET_X_FORCE + bytePos, bForce);

// 3. Write directly to ASIC Hardware Emulation Image
if (_asic != null)
{
    _asic.Write(OFFSET_X_ASIC + bytePos, bAsic);
}
```
This guarantees that the virtual ASIC hardware, GX Works 3 ladder force table, and CPU RAM remain 100% in sync, preventing input state bounce or unwanted resets.

---

## ⚡ Pure On-Demand Passthrough (Zero CPU Overhead)

NetToGXSim3 operates with **Zero CPU Overhead**:
- **No Background Polling**: The application does NOT poll or mirror the entire memory buffer continuously.
- **Zero Idle CPU**: CPU utilization drops to 0% when no client requests are active.
- **Sub-Microsecond Latency**: Data is read or written instantly through pointer accessors precisely upon packet arrival.

---

## ✨ Features

- 🔌 **Native OPC DA 2.05a Classic COM Server**:
  - **ProgID**: `NetToGXSim3.OPCServer.DA`
  - **CLSID**: `{4A7E0D2C-3F5B-4C91-95E8-8F2B1A6D7C90}`
  - **Interactive Tag Workstation**: Add, edit, and delete tags with live values, status quality lamps, and timestamps.
  - **Default Presets**: Pre-configured tags for `X0..X17`, `Y0..Y17`, `M0..M7`, `D0..D3`, `R0`, `W0`.
  - **Persistence & Portability**: Automatic real-time persistence in `opc_tags.json`, plus CSV Export/Import.
  - **Test Controls**: In-app test value write and bit toggle directly to virtual PLC registers.
  - **Broad Compatibility**: Direct integration with OPC Expert, Kepware KEPServerEX, Matrikon, Wonderware InTouch, Siemens WinCC, etc.

- 🌐 **Built-in OPC UA Server (`opc.tcp`)**:
  - **Endpoint**: `opc.tcp://localhost:48400/nettogxsim3` (and network hostname / IP).
  - **Port Configuration**: Fully customizable port with up/down spinner and automatic collision detection.
  - **Zero-Friction Authentication**: Anonymous access enabled by default.
  - **Automatic Tag Mirroring**: Seamlessly reflects 100% of tags created in the centralized tag workstation into the OPC UA namespace (`ns=2;s=NetToGXSim3.<Tag>`).
  - **Broad Client Compatibility**: Direct connectivity with UaExpert, Ignition SCADA, Pro-face, Node-RED, and cloud IoT gateways.

- ⚡ **Built-in Modbus TCP Server (Port 5020)**:
  - **High-Speed On-Demand Passthrough**: Serves real-time Modbus TCP requests without background polling overhead.
  - **Full Function Code Support**:
    - `FC 01`: Read Coils (Coil `0..1999` mapped to `Y0..Y17` Octal, Coil `2000+` mapped to `M0..M7679`).
    - `FC 02`: Read Discrete Inputs (`10001+` mapped to `X0..X17` Octal inputs).
    - `FC 03`: Read Holding Registers (`40001+` mapped to `D0..D7999`).
    - `FC 04`: Read Input Registers (`30001+` mapped to `D0..D7999`).
    - `FC 05`: Write Single Coil (writes to `Y` or `M` with triple-write latch sync).
    - `FC 06`: Write Single Holding Register (writes to `D`).
    - `FC 15`: Write Multiple Coils (batch write to `Y` or `M`).
    - `FC 16`: Write Multiple Registers (batch write to `D`).
  - **Zero-Based Addressing Toggle**: Switch between standard Modbus 1-based offset and 0-based address indexing on the fly.
  - **Lightweight 1-Second Viewport Sync & Event-Driven Write**: UI monitoring polls virtual PLC memory only once per second for visible DataGrid rows, accompanied by instant zero-latency feedback on write events.
  - **Real-Time Request & Client Diagnostics**: Atomic thread-safe tracking of total requests processed and active TCP clients.

- ⚡ **Dual MC Protocol Servers (TCP & UDP)**:
  - **Server 1 (TCP Binary)**: Default port `5001`. Automatically starts on launch.
  - **Server 2 (UDP Binary)**: Default port `5002`. Can be started independently.
  - Simultaneous independent TCP and UDP socket listeners.

- 🛡️ **Intelligent Port Conflict Resolution**:
  - Proactively checks port availability (e.g. avoiding port conflicts with Windows system services).
  - Automatically binds to the next available free port without crashing.

- 📡 **Comprehensive MC Protocol Support**:
  - **QnA 3E Binary & ASCII**:
    - `0x0101`: CPU Model Type Read (returns `Q02UCPU` / `FX5U` identification).
    - `0x0401`: Batch Read (Bit & Word units).
    - `0x0403`: Random Read (Word & DWord multi-address reading).
    - `0x1401`: Batch Write (Bit & Word units).
    - `0x1402`: Random Write (Word/DWord & Bit units).
  - **A-1E Binary & ASCII** (Legacy FX & A Series compatibility):
    - Subcommands `0x00` (Read Bits), `0x01` (Read Words), `0x02` (Write Bits), `0x03` (Write Words).

- 🎛️ **Interactive Hardware Simulation Rack**:
  - **Inputs Rack (X0 - X17 / Octal)**: 16 interactive toggle switches with blue LED status indicators to simulate digital inputs.
  - **Outputs Rack (Y0 - Y17 / Octal)**: 16 glowing green LED lamps reflecting PLC coil states in real time.

- 🔍 **Register Inspector & Traffic Log**:
  - Real-time packet inspector for any PLC device address (`D0`, `M0`, `X0`, `Y0`, `R0`, `W0`).
  - High-performance 500-line rolling traffic log buffer with sub-millisecond truncation.

- 🛑 **GX Simulator 3 Process Manager**:
  - Built-in **"Exit GX Simulator 3"** button to gracefully terminate background simulator processes (`FSim3Dlg.exe`, `FSimRun3.exe`, `Sim3Dlg.exe`, `RSimRun3.exe`).

- 🔄 **Automated Update Checker**:
  - Background GitHub Releases integration that alerts you when a new version is available.

---

## 📋 System Requirements

| Requirement | Specification |
| :--- | :--- |
| **Operating System** | Windows 7 / 8 / 10 / 11 (32-bit or 64-bit) |
| **Runtime** | .NET Framework 4.7.2 or higher |
| **PLC Software** | Mitsubishi **GX Works 3** (with GX Simulator 3 installed) |
| **Dependencies** | **Standalone** (Zero third-party middleware or external driver dependencies required) |

---

## 🚀 Quick Start Guide

### Step 1: Start Simulation in GX Works 3
1. Open your project in **GX Works 3** (FX5U / FX5UJ / FX5S / iQ-R).
2. Start simulation: navigate to **Debug** → **Start Simulation**.
3. Ensure the simulator window (`FSim3Dlg.exe` / `Sim3Dlg.exe`) appears and GX Works 3 is in **Monitor Mode**.

### Step 2: Launch NetToGXSim3
1. Run `NetToGXSim3.Wpf.exe` (or use the desktop shortcut).
2. Verify that the status header badge shows **`GX Sim 3: Connected`** (green).
3. The **MC TCP Binary Server** automatically starts on port **`5001`**, and the **OPC DA Server** starts automatically.

### Step 3: Connect External HMI, SCADA, or OPC Clients

#### Example A: Weintek EasyBuilder Pro
1. In EasyBuilder Pro, open **System Parameters** → **New Device**.
2. Configure device settings:
   - **PLC Type**: `Mitsubishi FX5U (Ethernet)` or `Mitsubishi Q/QnA (Ethernet)`
   - **Interface**: `Ethernet`
   - **IP Address**: `127.0.0.1` (or your host IP)
   - **Port**: `5001` (matching NetToGXSim3 Server 1)
   - **Protocol**: `TCP/IP, Binary Mode`
3. Launch **On-line Simulation** in EasyBuilder Pro.

#### Example B: OPC DA Client (OPC Expert / Kepware / SCADA)
1. In your OPC DA client, browse local servers or enter ProgID: **`NetToGXSim3.OPCServer.DA`**.
2. Connect to the server. Pre-configured tags (`D0`, `M0`, `X0`, `Y0`, `R0`, etc.) will immediately populate with live values.

#### Example C: Python (`pymcprotocol`)
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

# Toggle bit M0
mc.batchwrite_bitunits(headdevice="M0", values=[1])
```

#### Example D: OPC UA Client (UaExpert / Ignition)
1. In UaExpert, click **Server** → **Add** and enter endpoint: `opc.tcp://127.0.0.1:48400/nettogxsim3`.
2. Connect using Security Policy: **None** (Anonymous).
3. Browse the node tree under `Root` → `Objects` → `NetToGXSim3` to read and write live PLC tags.

#### Example E: Modbus TCP Client (Modbus Poll / SCADA)
1. In Modbus Poll (or any Modbus client):
   - **Connection**: `Modbus TCP/IP`, IP Address: `127.0.0.1`, Port: `5020`.
   - **Read Coils (FC 01)**: Address `0` (or `1`) to read `Y0`, Address `2000` to read `M0`.
   - **Read Holding Registers (FC 03)**: Address `0` (or `40001`) to read `D0`.
   - **Write Single Coil (FC 05)**: Address `2000` to set `M0` ON (`1`) or OFF (`0`).

---

## 🛠 Building from Source

```powershell
# 1. Clone the repository
git clone https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3.git
cd Mitsubishi-NetToGXSim3

# 2. Build the solution (x86 platform target)
dotnet build NetToGXSim3.sln -c Release -p:Platform=x86

# 3. Publish binaries
dotnet publish src/NetToGXSim3.Wpf/NetToGXSim3.Wpf.csproj -c Release -o publish

# 4. Build NSIS Installer Setup Package
.\run_or_createInstaller.bat
```

---

## 👤 Author & Support

- **Developer**: Ismail Lowkey
- **Repository**: [ismaillowkey/Mitsubishi-NetToGXSim3](https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3)
- **Version**: v0.8.1

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
