using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MidiBard.Util;

public static class PingHelper
{
    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_CONNECTIONS = 4;
    private const int MIB_TCP_STATE_LISTEN = 2;

    [DllImport("Iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved = 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public readonly uint dwState;
        public readonly uint dwLocalAddr;
        public readonly uint dwLocalPort;
        public readonly uint dwRemoteAddr;
        public readonly uint dwRemotePort;
        public readonly uint dwOwningPid;
    }

    private static bool InXIVPortRange(ushort port)
    {
        return (port >= 54992 && port <= 54994) ||
               (port >= 55006 && port <= 55007) ||
               (port >= 55021 && port <= 55040) ||
               (port >= 55296 && port <= 55551);
    }

    public static async Task<float?> GetFfxivPingOffsetSecondsAsync()
    {
        var address = IPAddress.Loopback;
        var bufferLength = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferLength, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS);
        var pTcpTable = Marshal.AllocHGlobal(bufferLength);

        try
        {
            var error = GetExtendedTcpTable(pTcpTable, ref bufferLength, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS);
            if (error != 0) return null;

            var table = new List<TcpRow>();
            var rowSize = Marshal.SizeOf<TcpRow>();
            var dwNumEntries = Marshal.ReadInt32(pTcpTable);
            var pRows = pTcpTable + 4;

            for (var i = 0; i < dwNumEntries && bufferLength - (4 + i * rowSize) >= rowSize; i++)
            {
                table.Add(Marshal.PtrToStructure<TcpRow>(pRows + i * rowSize));
            }

            var pid = Environment.ProcessId;
            foreach (var row in table)
            {
                var state = row.dwState;
                var tcpRemoteAddr = new IPAddress(row.dwRemoteAddr);
                var trpBytes = BitConverter.GetBytes((ushort)row.dwRemotePort).Reverse().ToArray();
                var tcpRemotePort = BitConverter.ToUInt16(trpBytes, 0);

                if (state == MIB_TCP_STATE_LISTEN || Equals(tcpRemoteAddr, IPAddress.Loopback)) continue;

                if (row.dwOwningPid == pid && InXIVPortRange(tcpRemotePort))
                {
                    address = tcpRemoteAddr;
                    break;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pTcpTable);
        }

        if (Equals(address, IPAddress.Loopback)) return null;

        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, 2000); // 2 sec timeout
            if (reply.Status == IPStatus.Success)
            {
                return (float)(reply.RoundtripTime / 2.0 / 1000.0);
            }
        }
        catch { }

        return null;
    }
}
