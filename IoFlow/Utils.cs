//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace IoFlowNamespace
{
    public static class Utils
    {
        //
        // Routines for byte-ordering ops on byte[] buffers.
        //
        // The convention between driver and API is that data which are not sent over net are in host
        // byte order, but anything that goes over net is in network byte order. We optimize for 
        // little-endian (x86, low-value byte first) hosts because that is the majority case. We then
        // special case for big-endian (network byte order, RISC) hosts. 
        //
        internal static bool HostIsLitteEndian = (IPAddress.HostToNetworkOrder(1) != 1);
        const int sizeofInt16 = 2;
        const int sizeofInt32 = 4;
        const int sizeofInt64 = 8;
        const int MAX_NET_PREFIX = 32;

        public static short Int16FromHostBytes(byte[] b, int offset)
        {
            // Host byte order may be big endian (RISC) or little endian (x86).
            // Optimize for host little-endian (x86), correcting on return.
            short i16 = b[offset + 1];
            i16 <<= 8;
            i16 += b[offset];
            return (HostIsLitteEndian ? i16 : ByteSwap(i16));
        }

        public static short Int16FromNetBytes(byte[] b, int offset)
        {
            // Network byte order is big endian, by definition.
            short i16 = b[offset];
            i16 <<= 8;
            i16 += b[offset + 1];
            return i16;
        }

        public static int Int32FromHostBytes(byte[] b, int offset)
        {
            // Host byte order may be big endian (RISC) or little endian (x86).
            // Optimize for host little-endian (x86), correcting on return.
            int i32 = b[offset];
            i32 += ((int)(b[offset + 1]) << 8);
            i32 += ((int)(b[offset + 2]) << 16);
            i32 += ((int)(b[offset + 3]) << 24);

            return (HostIsLitteEndian ? i32 : ByteSwap(i32));
        }

        public static int Int32FromNetBytes(byte[] b, int offset)
        {
            // Network byte order is big endian, by definition.
            Int32 i32 = ((int)(b[offset]) << 24);
            i32 += ((int)(b[offset + 1]) << 16);
            i32 += ((int)(b[offset + 2]) << 8);
            i32 += ((int)(b[offset + 3]));

            return i32;
        }

        public static long Int64FromHostBytes(byte[] b, int offset)
        {
            // Host byte order may be big endian (RISC) or little endian (x86).
            // Optimize for host little-endian (x86), correcting on return.

            long i64 = b[offset];
            i64 += ((long)(b[offset + 1]) << 8);
            i64 += ((long)(b[offset + 2]) << 16);
            i64 += ((long)(b[offset + 3]) << 24);
            i64 += ((long)(b[offset + 4]) << 32);
            i64 += ((long)(b[offset + 5]) << 40);
            i64 += ((long)(b[offset + 6]) << 48);
            i64 += ((long)(b[offset + 7]) << 56);

            return (HostIsLitteEndian ? i64 : ByteSwap(i64));
        }

        public static long Int64FromNetBytes(byte[] b, int offset)
        {
            // Network byte order is big endian, by definition.
            long i64 = ((long)(b[offset]) << 56);
            i64 += ((long)(b[offset + 1]) << 48);
            i64 += ((long)(b[offset + 2]) << 40);
            i64 += ((long)(b[offset + 3]) << 32);
            i64 += ((long)(b[offset + 4]) << 24);
            i64 += ((long)(b[offset + 5]) << 16);
            i64 += ((long)(b[offset + 6]) << 8);
            i64 += ((long)(b[offset + 7]));

            return i64;
        }

        public static short ByteSwap(short i16)
        {
            short swapped = 0;
            swapped |= (short)((i16 & (short)0x00ff) << 8);
            swapped |= (short)(i16 >> 8);
            return swapped;
        }

        public static int ByteSwap(int i32)
        {
            int swapped = 0;
            swapped |= ((i32 >> 24) & 0x000000ff);
            swapped |= ((i32 & 0x00ff0000) >> 8);
            swapped |= ((i32 & 0x0000ff00) << 8);
            swapped |= ((i32 & 0x000000ff) << 24);
            return swapped;
        }

        public static long ByteSwap(long i64)
        {
            long swapped = 0;
            swapped |= ((i64 >> 56) & (long)0x00000000000000ff);
            swapped |= ((i64 & 0x00ff000000000000) >> 40);
            swapped |= ((i64 & 0x0000ff0000000000) >> 24);
            swapped |= ((i64 & 0x000000ff00000000) >> 8);
            swapped |= ((i64 & 0x00000000ff000000) << 8);
            swapped |= ((i64 & 0x0000000000ff0000) << 24);
            swapped |= ((i64 & 0x000000000000ff00) << 40);
            swapped |= ((i64 & 0x00000000000000ff) << 56);

            return swapped;
        }

        public static int Int16ToNetBytes(short i16, byte[] buffer, int offset)
        {
            // Network byte order is big endian, by definition.
            i16 = (HostIsLitteEndian ? i16 : ByteSwap(i16));
            buffer[offset + 0] = (Byte)(i16 >> 8);
            buffer[offset + 1] = (Byte)(i16 & 0x000000ff);
            return 2;
        }

        public static int Int32ToNetBytes(int i32, byte[] buffer, int offset)
        {
            // Network byte order is big endian, by definition.
            i32 = (HostIsLitteEndian ? i32 : ByteSwap(i32));
            buffer[offset + 0] = (Byte)((i32 >> 24) & 0x000000ff);
            buffer[offset + 1] = (Byte)((i32 >> 16) & 0x000000ff);
            buffer[offset + 2] = (Byte)((i32 >> 8) & 0x000000ff);
            buffer[offset + 3] = (Byte)((i32 >> 0) & 0x000000ff);
            return 4;
        }

        public static int Int32ToHostBytes(int i32, byte[] buffer, int offset)
        {
            // Network byte order is big endian, by definition.
            i32 = (HostIsLitteEndian ? i32 : ByteSwap(i32));
            buffer[offset + 0] = (Byte)(i32 & 0x000000ff);
            buffer[offset + 1] = (Byte)((i32 >> 8) & 0x000000ff);
            buffer[offset + 2] = (Byte)((i32 >> 16) & 0x000000ff);
            buffer[offset + 3] = (Byte)((i32 >> 24) & 0x000000ff);
            return 4;
        }

        public static int Int64ToNetBytes(long i64, byte[] buffer, int offset)
        {
            // Network byte order is big endian, by definition.
            i64 = (HostIsLitteEndian ? i64 : ByteSwap(i64));
            buffer[offset + 0] = (Byte)((i64 >> 56) & 0x00000000000000ff);
            buffer[offset + 1] = (Byte)((i64 >> 48) & 0x00000000000000ff);
            buffer[offset + 2] = (Byte)((i64 >> 40) & 0x00000000000000ff);
            buffer[offset + 3] = (Byte)((i64 >> 32) & 0x00000000000000ff);
            buffer[offset + 4] = (Byte)((i64 >> 24) & 0x00000000000000ff);
            buffer[offset + 5] = (Byte)((i64 >> 16) & 0x00000000000000ff);
            buffer[offset + 6] = (Byte)((i64 >> 8) & 0x00000000000000ff);
            buffer[offset + 7] = (Byte)((i64 >> 0) & 0x00000000000000ff);
            return 8;
        }

        public static int Int64ToHostBytes(long i64, byte[] buffer, int offset)
        {
            // Network byte order is big endian, by definition.
            i64 = (HostIsLitteEndian ? i64 : ByteSwap(i64));
            buffer[offset + 0] = (Byte)((i64 >> 0) & 0x00000000000000ff);
            buffer[offset + 1] = (Byte)((i64 >> 8) & 0x00000000000000ff);
            buffer[offset + 2] = (Byte)((i64 >> 16) & 0x00000000000000ff);
            buffer[offset + 3] = (Byte)((i64 >> 24) & 0x00000000000000ff);
            buffer[offset + 4] = (Byte)((i64 >> 32) & 0x00000000000000ff);
            buffer[offset + 5] = (Byte)((i64 >> 40) & 0x00000000000000ff);
            buffer[offset + 6] = (Byte)((i64 >> 48) & 0x00000000000000ff);
            buffer[offset + 7] = (Byte)((i64 >> 56) & 0x00000000000000ff);
            return 8;
        }

        public static int DoubleToNetBytes(double dbl, byte[] buffer, int offset)
        {
            long i64 = BitConverter.DoubleToInt64Bits(dbl);
            Int64ToNetBytes(i64, buffer, offset);
            return 8;
        }

        public static double DoubleFromNetBytes(byte[] buffer, int offset)
        {
            long i64 = Int64FromNetBytes(buffer, offset);
            return BitConverter.Int64BitsToDouble(i64);
        }

        static bool IsIPv4Address(string addr)
        {
            const int byteCount = 4;
            IPAddress ipAddr;
            string[] separators = { @"." };
            string[] toks = addr.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length != byteCount)
                return false;
            int[] octets = new int[byteCount];
            for (int i = 0; i < byteCount; i++)
                if (!Int32.TryParse(toks[i], out octets[i]))
                    return false;
            if (octets[0] <= 0 || octets[0] > 255)
                return false;
            else if (octets[1] < 0 || octets[1] > 255)
                return false;
            else if (octets[2] < 0 || octets[2] > 255)
                return false;
            else if (octets[3] < 0 || octets[3] > 255)
                return false;
            else if (IPAddress.TryParse(addr, out ipAddr) == false ||
                    ipAddr.AddressFamily != AddressFamily.InterNetwork)
                return false;
            else
                return true;
        }

        static bool IsPrefix(string prefix)
        {
            int slashIndex = prefix.LastIndexOf(@"/");
            int iSlash;
            if (slashIndex == -1)
                return false;
            else if (prefix.Length == slashIndex + 1)
                return false;
            else if (!IsIPv4Address(prefix.Substring(0, slashIndex)) &&
                GetDnsIPv4HostName(prefix.Substring(0, slashIndex)) == null)
                return false;
            else if (String.IsNullOrEmpty(prefix.Substring(slashIndex + 1)))
                return false;
            else if (!Int32.TryParse(prefix.Substring(slashIndex + 1), out iSlash))
                return false;
            else if (0 <= iSlash && iSlash <= MAX_NET_PREFIX)
                return true;
            return false;
        }

        static bool IsEthAddress(string addr)
        {
            string[] separators = { @"-" };
            string[] tokens = addr.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 6)
                return false;
            int[] octets = new int[6];
            for (int i = 0; i < 6; i++)
            {
                if (!Int32.TryParse(tokens[i], System.Globalization.NumberStyles.HexNumber, new System.Globalization.CultureInfo("en-US"), out octets[i]))
                    return false;
                if (octets[i] < 0 || octets[i] > 255)
                    return false;
            }
            if (octets[0] == 0 && octets[1] == 0 && octets[2] == 0 && octets[3] == 0 && octets[4] == 0 && octets[5] == 0)
                return false;
            else if (PhysicalAddress.None.Equals(PhysicalAddress.Parse(addr)))
                return false;
            else
                return true;
        }

        static int GetPrefixLength(string prefix)
        {
            if (IsPrefix(prefix) == false)
                throw new ApplicationException("Invalid network prefix string.");
            int slashIndex = prefix.LastIndexOf(@"/");
            return Int32.Parse(prefix.Substring(slashIndex + 1));
        }

        static IPAddress GetDnsIPv4HostName(string straddr)
        {
            Console.WriteLine("GetDnsIPv4HostName parsing {0}", straddr);
            IPHostEntry hostEntry = Dns.GetHostEntry(straddr);
            IPAddress remIPv4Address = null;
            foreach (IPAddress ipAddr in hostEntry.AddressList)
            {
                if (ipAddr.AddressFamily == AddressFamily.InterNetwork)
                {
                    remIPv4Address = ipAddr;
                    break;
                }
            }
            return remIPv4Address;
        }

        public static bool ParseAddress(string straddr, out UInt64 u64, out int prefixLength)
        {
            if (straddr.Contains('.') && straddr.Contains('-'))
                throw new ArgumentException("Net addr string cannot contain both '.' and '-'");
            if (straddr.Contains('.') == false && straddr.Contains('-') == false)
                throw new ArgumentException("Net addr string must contain '.' or '-'");

            u64 = 0;
            prefixLength = 0;
            IPAddress DnsIPv4Address = null;
            if (IsIPv4Address(straddr))
            {
                IPAddress ipAddr = IPAddress.Parse(straddr);
                byte[] addrBytes = ipAddr.GetAddressBytes();
                for (int i = 0; i < 4; i++)
                {
                    u64 <<= 8;
                    u64 += addrBytes[i];
                }
                return true;
            }
            else if (IsEthAddress(straddr))
            {
                PhysicalAddress MAC = PhysicalAddress.Parse(straddr);
                if (PhysicalAddress.None.Equals(MAC))
                {
                    string msg = String.Format("Invalid MAC address {0}", straddr);
                    throw new ArgumentException(msg);
                }
                byte[] addrBytes = MAC.GetAddressBytes();
                for (int i = 0; i < addrBytes.Length; i++)
                {
                    u64 <<= 8;
                    u64 += addrBytes[i];
                }
                return true;
            }
            else if (IsPrefix(straddr))
            {
                string addrOnly = straddr.Substring(0, straddr.IndexOf(@"/"));
                prefixLength = GetPrefixLength(straddr);
                IPAddress ipAddr = null;
                if (IsIPv4Address(addrOnly))
                    ipAddr = IPAddress.Parse(addrOnly);
                else
                    ipAddr = GetDnsIPv4HostName(addrOnly);
                byte[] addrBytes = ipAddr.GetAddressBytes();
                for (int i = 0; i < 4; i++)
                {
                    u64 <<= 8;
                    u64 += addrBytes[i];
                }
                // Set low order bits of u64 (those outside prefix) to zeros.
                const int IPv4AddressBitLength = sizeofInt32 * 8;
                int PrefixShift = IPv4AddressBitLength - ParsePrefix(straddr);
                u64 >>= PrefixShift;
                u64 <<= PrefixShift;
                Console.WriteLine(@"{0} => {1}/{2}", straddr, ipAddr, prefixLength);
                return true;
            }
            else if ((DnsIPv4Address = GetDnsIPv4HostName(straddr)) != null)
            {
                byte[] addrBytes = DnsIPv4Address.GetAddressBytes();
                Console.WriteLine("host {0} on IPv4 address {1}", straddr, DnsIPv4Address);
                for (int i = 0; i < 4; i++)
                {
                    u64 <<= 8;
                    u64 += addrBytes[i];
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        // Note: takes "/0" as default when no explicit prefix specified.


        /// <summary>
        /// Returns net prefix from given address string.
        /// </summary>
        /// <param name="straddr">Address with trailing slash length.</param>
        /// <returns>Net prefix length iff in range [0,32] else zero (e.g. invalid or unspecified).</returns>
        public static byte ParsePrefix(string straddr)
        {
            if (!straddr.Contains('.'))
                return 0;

            if (!straddr.Contains(@"/"))
                return 0;

            string strPrefix = straddr.Substring(straddr.IndexOf(@"/") + 1);
            int iPrefix = int.Parse(strPrefix);
            if (iPrefix > 0 && iPrefix <= MAX_NET_PREFIX)
                return (byte)iPrefix;
            return 0;
        }

    }

    
}
