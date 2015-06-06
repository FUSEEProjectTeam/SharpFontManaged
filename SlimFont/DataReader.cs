﻿using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SlimFont {
    unsafe sealed class DataReader : IDisposable {
        readonly Stream stream;
        readonly byte[] buffer;
        readonly GCHandle handle;
        readonly byte* start;
        readonly int maxReadLength;
        int readOffset;
        int writeOffset;

        public DataReader (Stream stream, int maxReadLength = 4096) {
            this.stream = stream;
            this.maxReadLength = maxReadLength;

            buffer = new byte[maxReadLength * 2];
            handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            start = (byte*)handle.AddrOfPinnedObject();
        }

        public void Dispose () {
            if (handle.IsAllocated)
                handle.Free();
        }

        public byte ReadByte () => *Read(1);
        public short ReadInt16 () => *(short*)Read(sizeof(short));
        public int ReadInt32 () => *(int*)Read(sizeof(int));
        public ushort ReadUInt16 () => *(ushort*)Read(sizeof(ushort));
        public uint ReadUInt32 () => *(uint*)Read(sizeof(uint));
        public short ReadInt16BE () => (short)htons(ReadUInt16());
        public int ReadInt32BE () => (int)htonl(ReadUInt32());
        public ushort ReadUInt16BE () => htons(ReadUInt16());
        public uint ReadUInt32BE () => htonl(ReadUInt32());

        public void Jump (uint position) {
            // TODO: if the jump is within our buffer we can reuse it
            readOffset = 0;
            writeOffset = 0;
            stream.Position = position;
        }

        public void Skip (int count) {
            readOffset += count;
            if (readOffset < writeOffset)
                CheckWrapAround();
            else {
                // we've skipped everything in our buffer; clear it out
                // and then skip any remaining data by seeking the stream
                // TODO: implement a fallback for streams that can't seek
                var seekCount = readOffset - writeOffset;
                if (seekCount > 0)
                    stream.Position += seekCount;

                readOffset = 0;
                writeOffset = 0;
            }
        }

        byte* Read (int count) {
            // we'll be returning a pointer to a contiguous block of memory
            // at least count bytes large, starting at the current offset
            var result = start + readOffset;
            readOffset += count;

            if (readOffset >= writeOffset) {
                if (count > maxReadLength)
                    throw new InvalidOperationException("Tried to read more data than the max read length.");

                // we need to read at least this many bytes, but we'll try for more (could be zero)
                var need = readOffset - writeOffset;
                while (need > 0) {
                    // try to read in a chunk of maxReadLength bytes (unless that would push past the end of our space)
                    int read = stream.Read(buffer, writeOffset, Math.Min(maxReadLength, buffer.Length - writeOffset));
                    if (read <= 0)
                        throw new EndOfStreamException();

                    writeOffset += read;
                    need -= read;
                }

                CheckWrapAround();
            }

            // most of the time we'll have plenty of data in the buffer
            // so we'll fall through here and get the pointer quickly
            return result;
        }

        void CheckWrapAround () {
            // if we've gone past the max read length, we can no longer ensure
            // that future read calls of maxReadLength size will be able to get a
            // contiguous buffer, so wrap back to the beginning
            if (readOffset >= maxReadLength) {
                // back copy any buffered data so that it doesn't get lost
                var copyCount = writeOffset - readOffset;
                if (copyCount > 0)
                    Buffer.BlockCopy(buffer, readOffset, buffer, 0, copyCount);

                readOffset = 0;
                writeOffset = copyCount;
            }
        }

        static uint htonl (uint value) {
            // this branch is constant at JIT time and will be optimized out
            if (!BitConverter.IsLittleEndian)
                return value;

            var ptr = (byte*)&value;
            return (uint)(ptr[0] << 24 | ptr[1] << 16 | ptr[2] << 8 | ptr[3]);
        }

        static ushort htons (ushort value) {
            // this branch is constant at JIT time and will be optimized out
            if (!BitConverter.IsLittleEndian)
                return value;

            var ptr = (byte*)&value;
            return (ushort)(ptr[0] << 8 | ptr[1]);
        }
    }
}