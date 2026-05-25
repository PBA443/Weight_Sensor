# Weight Sensor SDK Integration & Interop

A high-performance prototype demonstrating a C++ Shared Library (DLL) integrated with a .NET C# console application using Platform Invoke (P/Invoke). This simulates a real-time hardware data pipeline for load cell weight processing.

## 🛠️ Project Architecture

- **`cpp_src/`**: Contains the core logic written in C++ that manages low-level operations (simulated bitwise hardware initialization and ADC-to-weight translation). Compiled into a 64-bit DLL.
- **`csharp_app/`**: A .NET Core console application that serves as the UI layer, consuming the C++ DLL using native interoperability to display real-time sensor updates.

---

## 🚀 Getting Started

### Prerequisites
- [CMake](https://cmake.org/download/) (v3.20 or higher)
- [MinGW-w64](https://www.winlibs.com/) (64-bit GCC/G++)
- [.NET SDK](https://dotnet.microsoft.com/en-us/download) (v6.0/7.0/8.0)

### 1. Build the C++ Shared Library (DLL)
Navigate to the C++ source directory and run the following commands to generate the 64-bit DLL via CMake:

```bash
cd cpp_src
# Clean old cache and build
Remove-Item CMakeCache.txt -ErrorAction SilentlyContinue
cmake -G "MinGW Makefiles" .
cmake --build .