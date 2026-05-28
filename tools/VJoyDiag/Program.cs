using System.Reflection;
using System.Runtime.InteropServices;

const string dllName = "vJoyInterface.dll";
var dllPath = args.Length > 0 ? args[0] : @"C:\Program Files\vJoy\x64\vJoyInterface.dll";
NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (_, _, _) =>
    NativeLibrary.TryLoad(dllPath, out var h) ? h : IntPtr.Zero);

Console.WriteLine($"DLL: {dllPath}");
Console.WriteLine($"Exists: {File.Exists(dllPath)}");
Console.WriteLine($"vJoyEnabled: {Safe(() => vJoyEnabled())}");
Console.WriteLine($"isVJDExists(1): {Safe(() => isVJDExists(1))}");
Console.WriteLine($"GetVJDStatus(1): {Safe(() => GetVJDStatus(1))}");
var acquired = Safe(() => AcquireVJD(1));
Console.WriteLine($"AcquireVJD(1): {acquired}");
if (acquired is true)
{
  var setX = Safe(() => SetAxis(0x6000, 1, 0x30));
  var setRz = Safe(() => SetAxis(0x2000, 1, 0x35));
  Console.WriteLine($"SetAxis X: {setX}");
  Console.WriteLine($"SetAxis RZ: {setRz}");
  Safe(() => { RelinquishVJD(1); return true; });
}

static object? Safe(Func<object?> fn)
{
  try { return fn(); }
  catch (Exception ex) { return $"ERR: {ex.GetType().Name}: {ex.Message}"; }
}

[DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
static extern bool vJoyEnabled();

[DllImport(dllName, EntryPoint = "isVJDExists", CallingConvention = CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
static extern bool isVJDExists(uint rId);

[DllImport(dllName, EntryPoint = "GetVJDStatus", CallingConvention = CallingConvention.Cdecl)]
static extern uint GetVJDStatus(uint rId);

[DllImport(dllName, EntryPoint = "AcquireVJD", CallingConvention = CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
static extern bool AcquireVJD(uint rId);

[DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
static extern void RelinquishVJD(uint rId);

[DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
static extern bool SetAxis(int value, uint rId, uint axis);
