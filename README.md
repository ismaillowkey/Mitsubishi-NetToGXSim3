# NetToGXSim3

**Mitsubishi GX Works 3 Simulator Network Protocol Bridge**  
*Developed by Ismail Lowkey*

[![Download Latest Release](https://img.shields.io/badge/Download-Latest%20Release%20(Installer)-brightgreen?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/ismaillowkey/Mitsubishi-NetToGX3Sim/releases/latest)
[![GitHub Releases](https://img.shields.io/github/v/release/ismaillowkey/Mitsubishi-NetToGX3Sim?style=for-the-badge&logo=github&color=blue)](https://github.com/ismaillowkey/Mitsubishi-NetToGX3Sim/releases/latest)

> ### 📥 **[Click Here to Download Latest Setup Installer (v0.5.0)](https://github.com/ismaillowkey/Mitsubishi-NetToGX3Sim/releases/latest)**
> Get the ready-to-run Windows installer (`Setup_NetToGXSim3_v0.5.0.exe`) from GitHub Releases.

[![Platform](https://img.shields.io/badge/Platform-Windows%20x86%20%7C%20x64-blue.svg)]()
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple.svg)]()
[![Target](https://img.shields.io/badge/Target-MELSOFT%20GX%20Simulator%203-red.svg)]()
[![Protocol](https://img.shields.io/badge/Protocol-MC%20Protocol%20(3E%20%26%201E)-green.svg)]()
[![License](https://img.shields.io/badge/License-MIT-lightgrey.svg)]()

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
                               │ IPC / Virtual Bus
┌──────────────────────────────▼──────────────────────────────┐
│                    GX Simulator 3 Engine                    │
│       (FSimRun3.exe / Sim3Dlg.exe / Virtual FX5 / iQ-R)     │
└──────────────────────────────┬──────────────────────────────┘
                               │ Direct Driver COM Engine (STA Thread)
┌──────────────────────────────▼──────────────────────────────┐
│                        NetToGXSim3                          │
│  ┌─────────────────────────┐     ┌────────────────────────┐ │
│  │   MC Server 1 (TCP/UDP) │     │   MC Server 2 (TCP/UDP)│ │
│  │   (Default: Port 5001)  │     │   (Default: Port 6000) │ │
│  └────────────┬────────────┘     └───────────┬────────────┘ │
│               │ In-Memory Mirror Cache       │              │
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
| **COM Components** | MELSOFT Communication Library (`ActProgType` / `ActUtlType`) |

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
git clone https://github.com/ismaillowkey/Mitsubishi-NetToGX3Sim.git
cd Mitsubishi-NetToGX3Sim

# Build the solution (x86 platform target)
dotnet build NetToGXSim3.sln -c Release -p:Platform=x86

# Publish binaries
dotnet publish src/NetToGXSim3.Wpf/NetToGXSim3.Wpf.csproj -c Release -o publish

# Create NSIS Installer (optional)
create_installer.bat
```

---

## 👤 Author & Support

- **Developer**: Ismail Lowkey
- **Repository**: [ismaillowkey/Mitsubishi-NetToGX3Sim](https://github.com/ismaillowkey/Mitsubishi-NetToGX3Sim)
- **Version**: 0.5.0

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
