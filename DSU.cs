using System;
using System.Runtime.InteropServices;
using DamienG.Security.Cryptography;

namespace DSUMultiplayerProxy
{
    public class DSU
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Header
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public char[] magic; // DSUS or DSUC
            public ushort protocol;
            public ushort packetsize; // without header
            public uint crc32;
            public uint id;
            public uint messageType; // not technically part of header
        }

        public static int HeaderLength = Marshal.SizeOf(new Header());

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct InfoRequest
        {
            public int controllerCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] controllerIndices;
        }

        public static int InfoRequestLength = Marshal.SizeOf(new InfoRequest());

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct InfoResponse
        {
            public byte slot;
            public byte state;
            public byte model;
            public byte connectionType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] address;
            public byte batteryStatus;
        }

        public static int InfoResponseLength = Marshal.SizeOf(new InfoResponse());

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DataRequest
        {
            public byte flags;
            public byte slot;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] address;
        }

        public static int DataRequestLength = Marshal.SizeOf(new DataRequest());

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DataResponse
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 69)]
            public byte[] data;
        }

        public static int DataResponseLength = Marshal.SizeOf(new DataResponse());

        public enum MessageType
        {
            ProtocolInfo = 0x100000,
            ControllerInfo = 0x100001,
            ControllerData = 0x100002,
        }

        // realistically i should have constructors and methods instead of this hacky method, but this is a lot easier
        // i guess maybe a TODO would be to make this more like C# code and not C code?

        public static byte[] StructToByteArray(object s)
        {
            // this is really dumb but probably the easiest way to
            // take the arbitrary struct data and stuff it into a byte array
            int size = Marshal.SizeOf(s);
            byte[] b = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size); // allocate unmanaged memory
            Marshal.StructureToPtr(s, ptr, true); // move struct to unmanaged memory
            Marshal.Copy(ptr, b, 0, size); // copy unmanaged memory to byte array
            Marshal.FreeHGlobal(ptr); // free allocated memory

            return b;
        }

        public static T ByteArrayToStruct<T>(byte[] b, int offset = 0)
        {
            T s = default;
            int size = Marshal.SizeOf(s);

            IntPtr ptr = Marshal.AllocHGlobal(size); 
            Marshal.Copy(b, offset, ptr, size); // copy byte array to unmanaged memory
            s = (T)Marshal.PtrToStructure(ptr, s.GetType()); // move unmanaged memory to struct
            Marshal.FreeHGlobal(ptr);

            return s;
        }

        private static byte[] FormPacketGeneric(Header header, object message)
        {
            byte[] packet = new byte[HeaderLength + Marshal.SizeOf(message)];
            header.crc32 = 0;

            byte[] headerBytes = StructToByteArray(header);
            byte[] messageBytes = StructToByteArray(message);

            Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
            Buffer.BlockCopy(messageBytes, 0, packet, headerBytes.Length, messageBytes.Length);

            header.crc32 = Crc32.Compute(packet);
            headerBytes = StructToByteArray(header);
            Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);

            return packet;
        }

        public static byte[] FormPacket(Header header, InfoRequest infoRequest)
        {
            return FormPacketGeneric(header, infoRequest);
        }

        public static byte[] FormPacket(Header header, InfoResponse infoResponse)
        {
            return FormPacketGeneric(header, infoResponse);
        }

        public static byte[] FormPacket(Header header, DataRequest dataRequest)
        {
            return FormPacketGeneric(header, dataRequest);
        }

        public static byte[] FormPacket(Header header, InfoResponse infoResponse, DataResponse dataResponse)
        {
            byte[] packet = new byte[HeaderLength + InfoResponseLength + DataResponseLength];
            header.crc32 = 0;

            byte[] headerBytes = StructToByteArray(header);
            byte[] infoResponseBytes = StructToByteArray(infoResponse);
            byte[] dataResponseBytes = StructToByteArray(dataResponse);

            Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
            Buffer.BlockCopy(infoResponseBytes, 0, packet, headerBytes.Length, infoResponseBytes.Length);
            Buffer.BlockCopy(dataResponseBytes, 0, packet, headerBytes.Length + infoResponseBytes.Length, dataResponseBytes.Length);

            header.crc32 = Crc32.Compute(packet);
            headerBytes = StructToByteArray(header);
            Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);

            return packet;
        }

    }
}
