using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace HayaseBindVPN
{
    // Windows SDK fwpmtypes.h / fwptypes.h, x64 ABI. No driver or shell commands.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayData { public string name; public string description; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Blob { public uint size; public IntPtr data; }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct Value
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public uint number;
        [FieldOffset(8)] public IntPtr pointer;
        public static Value U32(uint n) { return new Value { type = 3, number = n }; }
        public static Value U8(byte n) { return new Value { type = 1, number = n }; }
        public static Value Ptr(uint t, IntPtr p) { return new Value { type = t, pointer = p }; }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Condition { public Guid key; public uint match; public Value value; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Action { public uint type; public Guid key; }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct Context
    {
        [FieldOffset(0)] public ulong raw;
        [FieldOffset(0)] public Guid provider;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Filter
    {
        public Guid key;
        public DisplayData display;
        public uint flags;
        public IntPtr provider;
        public Blob providerData;
        public Guid layer;
        public Guid sublayer;
        public Value weight;
        public uint count;
        public IntPtr conditions;
        public Action action;
        // Native union includes a GUID, not merely a UINT64.
        public Context context;
        public IntPtr reserved;
        public ulong id;
        public Value effectiveWeight;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Sublayer
    {
        public Guid key; public DisplayData display; public uint flags;
        public IntPtr provider; public Blob providerData; public ushort weight;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Session
    {
        public Guid key; public DisplayData display; public uint flags;
        public uint timeout; public uint process; public IntPtr sid;
        public string username; [MarshalAs(UnmanagedType.Bool)] public bool kernel;
    }
    internal static class Native
    {
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        internal static extern uint FwpmEngineOpen0(string server, uint auth, IntPtr identity, IntPtr session, out IntPtr engine);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmEngineClose0(IntPtr engine);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmSubLayerAdd0(IntPtr engine, ref Sublayer sublayer, IntPtr security);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmSubLayerDeleteByKey0(IntPtr engine, ref Guid key);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmFilterAdd0(IntPtr engine, ref Filter filter, IntPtr security, out ulong id);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmFilterDeleteByKey0(IntPtr engine, ref Guid key);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmFilterGetByKey0(IntPtr engine, ref Guid key, out IntPtr filter);
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        internal static extern uint FwpmGetAppIdFromFileName0(string path, out IntPtr blob);
        [DllImport("fwpuclnt.dll")] internal static extern void FwpmFreeMemory0(ref IntPtr memory);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmTransactionBegin0(IntPtr engine, uint flags);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmTransactionCommit0(IntPtr engine);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmTransactionAbort0(IntPtr engine);
        [DllImport("iphlpapi.dll")] internal static extern uint ConvertInterfaceGuidToLuid(ref Guid guid, out ulong luid);
        internal static void Check(uint code, string operation)
        {
            if (code != 0) throw new Win32Exception(unchecked((int)code), operation + " failed (0x" + code.ToString("X8") + ").");
        }
    }
    internal sealed class Memory : IDisposable
    {
        private readonly List<IntPtr> allocations = new List<IntPtr>();
        public IntPtr Allocate(int size) { var p = Marshal.AllocHGlobal(size); allocations.Add(p); return p; }
        public IntPtr Bytes(byte[] bytes) { var p = Allocate(bytes.Length); Marshal.Copy(bytes, 0, p, bytes.Length); return p; }
        public IntPtr UInt64(ulong n) { var p = Allocate(8); Marshal.WriteInt64(p, unchecked((long)n)); return p; }
        public void Dispose() { foreach (var p in allocations) Marshal.FreeHGlobal(p); }
    }
    internal sealed class Engine : IDisposable
    {
        public IntPtr Handle { get; private set; }
        public Engine(bool dynamic)
        {
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("The helper requires 64-bit Windows.");
            IntPtr sessionPtr = IntPtr.Zero;
            try
            {
                if (dynamic)
                {
                    var session = new Session { flags = 1, timeout = 5000 };
                    sessionPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Session)));
                    Marshal.StructureToPtr(session, sessionPtr, false);
                }
                IntPtr handle;
                Native.Check(Native.FwpmEngineOpen0(null, 10, IntPtr.Zero, sessionPtr, out handle), "Open Windows Filtering Platform");
                Handle = handle;
            }
            finally { if (sessionPtr != IntPtr.Zero) Marshal.FreeHGlobal(sessionPtr); }
        }
        public void Transaction(System.Action action)
        {
            Native.Check(Native.FwpmTransactionBegin0(Handle, 0), "Begin filter update");
            try { action(); Native.Check(Native.FwpmTransactionCommit0(Handle), "Commit filter update"); }
            catch { Native.FwpmTransactionAbort0(Handle); throw; }
        }
        public void Delete(Guid key)
        {
            uint result = Native.FwpmFilterDeleteByKey0(Handle, ref key);
            if (result != 0x80320003) Native.Check(result, "Remove owned filter");
        }
        public Filter? Get(Guid key)
        {
            IntPtr p;
            uint result = Native.FwpmFilterGetByKey0(Handle, ref key, out p);
            if (result == 0x80320003) return null;
            Native.Check(result, "Read filter");
            try { return (Filter)Marshal.PtrToStructure(p, typeof(Filter)); }
            finally { Native.FwpmFreeMemory0(ref p); }
        }
        public bool Matches(Guid key, Func<Filter, bool> inspect)
        {
            IntPtr p;
            uint result = Native.FwpmFilterGetByKey0(Handle, ref key, out p);
            if (result == 0x80320003) return false;
            Native.Check(result, "Inspect filter");
            try { return inspect((Filter)Marshal.PtrToStructure(p, typeof(Filter))); }
            finally { Native.FwpmFreeMemory0(ref p); }
        }
        public void Add(Guid key, Guid sublayer, Guid layer, bool persistent, byte weight, bool permit, Condition[] conditions)
        {
            using (var memory = new Memory())
            {
                int size = Marshal.SizeOf(typeof(Condition));
                IntPtr p = memory.Allocate(size * conditions.Length);
                for (int i = 0; i < conditions.Length; i++) Marshal.StructureToPtr(conditions[i], IntPtr.Add(p, i * size), false);
                var filter = new Filter {
                    key = key, display = new DisplayData { name = "Hayase BindVPN: " + (permit ? "selected interface / loopback" : "block other traffic") },
                    flags = persistent ? 1u : 0u, layer = layer, sublayer = sublayer,
                    weight = Value.U8(weight), count = (uint)conditions.Length, conditions = p,
                    action = new Action { type = permit ? 0x1002u : 0x1001u }
                };
                ulong id;
                Native.Check(Native.FwpmFilterAdd0(Handle, ref filter, IntPtr.Zero, out id), "Add interface filter");
            }
        }
        public void Dispose() { if (Handle != IntPtr.Zero) { Native.FwpmEngineClose0(Handle); Handle = IntPtr.Zero; } }
    }
    internal sealed class Adapter
    {
        public string id; public string name; public string description; public bool up;
        public bool suggested; public string[] addresses;
        public static Adapter[] List()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && IsGuid(n.Id))
                .Select(n => new Adapter {
                    id = new Guid(n.Id).ToString(), name = n.Name, description = n.Description,
                    up = n.OperationalStatus == OperationalStatus.Up,
                    suggested = (n.Name + " " + n.Description).IndexOf("mullvad", StringComparison.OrdinalIgnoreCase) >= 0,
                    addresses = n.GetIPProperties().UnicastAddresses.Select(a => a.Address).Where(Usable).Select(a => a.ToString()).ToArray()
                }).OrderByDescending(n => n.suggested).ThenBy(n => n.name).ToArray();
        }
        private static bool IsGuid(string id) { Guid result; return Guid.TryParse(id, out result); }
        internal static bool Usable(IPAddress a)
        {
            var bytes = a.GetAddressBytes();
            return !IPAddress.IsLoopback(a) && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)
                && !a.IsIPv6LinkLocal && !(bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254);
        }
    }
    internal interface BindingPolicy : IDisposable
    {
        int PermitCount { get; }
        bool HasBlock();
        bool HasBlockForExecutable(string executable);
        bool PermitsHealthy();
        void InstallBlock(string executable);
        void RestrictTo(string executable, Adapter adapter);
        void ClearPermits();
        void RemoveBlock();
    }
    internal sealed class Policy : BindingPolicy
    {
        internal static readonly Guid ProductionSublayer = new Guid("f923a5b6-3a13-4c7e-b8f5-588946a65180");
        private static readonly Guid AppId = new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971");
        private static readonly Guid Flags = new Guid("632ce23b-5167-435c-86d7-e903684aa80c");
        private static readonly Guid LocalInterface = new Guid("4cd62a49-59c3-4969-b7f3-bda5d32890a4");
        private static readonly Guid NextInterface = new Guid("93ae8f5b-7f6f-4719-98c8-14e97429ef04");
        private static readonly Guid ArrivalInterface = new Guid("618a9b6d-386b-4136-ad6e-b51587cfb1cd");
        private static readonly Guid LocalAddress = new Guid("d9ee00de-c1ef-4617-bfe3-ffd8f5a08957");
        internal static readonly Guid[] Layers = {
            new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82"),
            new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4"),
            new Guid("e1cd9fe7-f4b5-4273-96c0-592e487b8650"),
            new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f")
        };
        private readonly Engine permanent;
        private readonly Engine temporary;
        private readonly Guid sublayer;
        private readonly bool persistent;
        private readonly List<Guid> permits = new List<Guid>();
        public int PermitCount { get { return permits.Count; } }
        public Policy() : this(ProductionSublayer, true) { }
        internal Policy(Guid key, bool persist, bool splitSessions = false)
        {
            sublayer = key; persistent = persist;
            permanent = new Engine(!persist && !splitSessions);
            try
            {
                temporary = persist || splitSessions ? new Engine(true) : permanent;
                var sub = new Sublayer { key = sublayer, flags = persist ? 1u : 0u, weight = 0xFFFF,
                    display = new DisplayData { name = "Hayase BindVPN" } };
                uint result = Native.FwpmSubLayerAdd0(permanent.Handle, ref sub, IntPtr.Zero);
                if (result != 0x80320009) Native.Check(result, "Create owned filter sublayer");
            }
            catch { if (temporary != null && temporary != permanent) temporary.Dispose(); permanent.Dispose(); throw; }
        }
        internal Guid StaticKey(int index)
        {
            byte[] bytes = sublayer.ToByteArray();
            bytes[0] ^= (byte)(index + 1);
            return new Guid(bytes);
        }
        private static Condition Equal(Guid key, Value value) { return new Condition { key = key, value = value }; }
        public void InstallBlock(string executable)
        {
            IntPtr blob;
            Native.Check(Native.FwpmGetAppIdFromFileName0(executable, out blob), "Identify Hayase executable");
            try
            {
                var app = Equal(AppId, Value.Ptr(12, blob));
                permanent.Transaction(delegate {
                    for (int i = 0; i < 8; i++) permanent.Delete(StaticKey(i));
                    for (int i = 0; i < 4; i++)
                    {
                        permanent.Add(StaticKey(i * 2), sublayer, Layers[i], persistent, 1, false, new[] { app });
                        permanent.Add(StaticKey(i * 2 + 1), sublayer, Layers[i], persistent, 3, true,
                            new[] { app, new Condition { key = Flags, match = 6, value = Value.U32(1) } });
                    }
                });
            }
            finally { Native.FwpmFreeMemory0(ref blob); }
        }
        public bool HasBlock()
        {
            for (int i = 0; i < 4; i++)
            {
                var filter = permanent.Get(StaticKey(i * 2));
                if (!filter.HasValue || filter.Value.sublayer != sublayer || filter.Value.layer != Layers[i]
                    || filter.Value.action.type != 0x1001 || (filter.Value.flags & 0x20) != 0) return false;
            }
            return true;
        }
        public bool HasBlockForExecutable(string executable)
        {
            IntPtr blob;
            Native.Check(Native.FwpmGetAppIdFromFileName0(executable, out blob), "Identify protected application");
            try
            {
                byte[] expected = BlobBytes(blob);
                for (int i = 0; i < 4; i++)
                {
                    int layerIndex = i;
                    for (int kind = 0; kind < 2; kind++)
                    {
                        bool loopback = kind == 1;
                        if (!permanent.Matches(StaticKey(i * 2 + kind), delegate(Filter filter) {
                            if (filter.sublayer != sublayer || filter.layer != Layers[layerIndex] || (filter.flags & 0x20) != 0
                                || (persistent && (filter.flags & 1) == 0) || filter.action.type != (loopback ? 0x1002u : 0x1001u)
                                || filter.count != (loopback ? 2u : 1u)) return false;
                            var conditions = Enumerable.Range(0, (int)filter.count).Select(index => (Condition)Marshal.PtrToStructure(
                                IntPtr.Add(filter.conditions, index * Marshal.SizeOf(typeof(Condition))), typeof(Condition))).ToArray();
                            var app = conditions.FirstOrDefault(condition => condition.key == AppId);
                            if (app.key != AppId || app.match != 0 || app.value.type != 12 || !BlobBytes(app.value.pointer).SequenceEqual(expected)) return false;
                            if (!loopback) return true;
                            var flag = conditions.FirstOrDefault(condition => condition.key == Flags);
                            return flag.key == Flags && flag.match == 6 && flag.value.type == 3 && flag.value.number == 1;
                        })) return false;
                    }
                }
                return true;
            }
            finally { Native.FwpmFreeMemory0(ref blob); }
        }
        private static byte[] BlobBytes(IntPtr pointer)
        {
            var blob = (Blob)Marshal.PtrToStructure(pointer, typeof(Blob));
            byte[] bytes = new byte[blob.size]; Marshal.Copy(blob.data, bytes, 0, bytes.Length); return bytes;
        }
        public bool PermitsHealthy()
        {
            return permits.Count > 0 && permits.All(key => temporary.Matches(key, filter => filter.sublayer == sublayer
                && filter.action.type == 0x1002 && filter.count == 4 && (filter.flags & 0x20) == 0));
        }
        public void RestrictTo(string executable, Adapter adapter)
        {
            Guid guid; ulong luid = 0;
            bool live = adapter != null && adapter.up && Guid.TryParse(adapter.id, out guid);
            if (live) { guid = new Guid(adapter.id); live = Native.ConvertInterfaceGuidToLuid(ref guid, out luid) == 0; }
            var nextPermits = new List<Guid>();
            IntPtr blob;
            Native.Check(Native.FwpmGetAppIdFromFileName0(executable, out blob), "Identify Hayase executable");
            try
            {
                using (var memory = new Memory())
                {
                    var app = Equal(AppId, Value.Ptr(12, blob));
                    var iface = Value.Ptr(4, memory.UInt64(luid));
                    temporary.Transaction(delegate {
                        foreach (Guid key in permits) temporary.Delete(key);
                        if (!live) return;
                        foreach (var text in adapter.addresses)
                        {
                            IPAddress address = IPAddress.Parse(text);
                            if (!Adapter.Usable(address)) continue;
                            byte[] bytes = address.GetAddressBytes();
                            bool v6 = bytes.Length == 16;
                            Value source = v6 ? Value.Ptr(11, memory.Bytes(bytes)) : Value.U32(
                                ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3]);
                            for (int direction = 0; direction < 2; direction++)
                            {
                                Guid key = Guid.NewGuid();
                                temporary.Add(key, sublayer, Layers[direction * 2 + (v6 ? 1 : 0)], false, 2, true,
                                    new[] { app, Equal(LocalInterface, iface), Equal(direction == 0 ? NextInterface : ArrivalInterface, iface), Equal(LocalAddress, source) });
                                nextPermits.Add(key);
                            }
                        }
                    });
                }
                permits.Clear(); permits.AddRange(nextPermits);
            }
            finally { Native.FwpmFreeMemory0(ref blob); }
        }
        public void ClearPermits()
        {
            if (permits.Count == 0) return;
            temporary.Transaction(delegate { foreach (Guid key in permits) temporary.Delete(key); });
            permits.Clear();
        }
        public void RemoveBlock()
        {
            ClearPermits();
            permanent.Transaction(delegate { for (int i = 0; i < 8; i++) permanent.Delete(StaticKey(i)); });
        }
        public void Dispose()
        {
            // Dynamic allows disappear on process death too. Persistent blocks remain.
            if (temporary != permanent) temporary.Dispose();
            if (!persistent && temporary != permanent)
            {
                permanent.Transaction(delegate { for (int i = 0; i < 8; i++) permanent.Delete(StaticKey(i)); });
                Guid key = sublayer;
                Native.Check(Native.FwpmSubLayerDeleteByKey0(permanent.Handle, ref key), "Clean up test sublayer");
            }
            permanent.Dispose();
        }
        internal void CloseAllowSessionForTest()
        {
            if (temporary == permanent) throw new InvalidOperationException("The test requires separate sessions.");
            temporary.Dispose(); permits.Clear();
        }
        internal static void VerifyAbi()
        {
            if (IntPtr.Size != 8 || Marshal.SizeOf(typeof(Value)) != 16 || Marshal.SizeOf(typeof(Condition)) != 40
                || Marshal.SizeOf(typeof(Filter)) != 200 || Marshal.SizeOf(typeof(Sublayer)) != 72)
                throw new InvalidOperationException("Unexpected Windows filtering ABI.");
        }
    }
}
