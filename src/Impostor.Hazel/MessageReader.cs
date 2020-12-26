﻿using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Impostor.Api;
using Impostor.Api.Net.Messages;
using Microsoft.Extensions.ObjectPool;

namespace Impostor.Hazel
{
    public class MessageReader : IMessageReader
    {
        private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

        private readonly ObjectPool<MessageReader> _pool;
        private bool _inUse;

        internal MessageReader(ObjectPool<MessageReader> pool)
        {
            _pool = pool;
        }

        public byte[] Buffer { get; private set; }

        public int Offset { get; internal set; }

        public int Position { get; internal set; }

        public int Length { get; internal set; }

        public byte Tag { get; private set; }

        public MessageReader Parent { get; private set; }

        private int ReadPosition => Offset + Position;

        public void Update(byte[] buffer, int offset = 0, int position = 0, int? length = null, byte tag = byte.MaxValue, MessageReader parent = null)
        {
            _inUse = true;

            Buffer = buffer;
            Offset = offset;
            Position = position;
            Length = length ?? buffer.Length;
            Tag = tag;
            Parent = parent;
        }

        internal void Reset()
        {
            _inUse = false;

            Tag = byte.MaxValue;
            Buffer = null;
            Offset = 0;
            Position = 0;
            Length = 0;
            Parent = null;
        }

        public IMessageReader ReadMessage()
        {
            var length = ReadUInt16();
            var tag = FastByte();
            var pos = ReadPosition;

            Position += length;

            var reader = _pool.Get();
            reader.Update(Buffer, pos, 0, length, tag, this);
            return reader;
        }

        public bool ReadBoolean()
        {
            byte val = FastByte();
            return val != 0;
        }

        public sbyte ReadSByte()
        {
            return (sbyte)FastByte();
        }

        public byte ReadByte()
        {
            return FastByte();
        }

        public ushort ReadUInt16()
        {
            var output = BinaryPrimitives.ReadUInt16LittleEndian(Buffer.AsSpan(ReadPosition));
            Position += sizeof(ushort);
            return output;
        }

        public short ReadInt16()
        {
            var output = BinaryPrimitives.ReadInt16LittleEndian(Buffer.AsSpan(ReadPosition));
            Position += sizeof(short);
            return output;
        }

        public uint ReadUInt32()
        {
            var output = BinaryPrimitives.ReadUInt32LittleEndian(Buffer.AsSpan(ReadPosition));
            Position += sizeof(uint);
            return output;
        }

        public int ReadInt32()
        {
            var output = BinaryPrimitives.ReadInt32LittleEndian(Buffer.AsSpan(ReadPosition));
            Position += sizeof(int);
            return output;
        }

        public unsafe float ReadSingle()
        {
            var output = BinaryPrimitives.ReadSingleLittleEndian(Buffer.AsSpan(ReadPosition));
            Position += sizeof(float);
            return output;
        }

        public string ReadString()
        {
            var len = ReadPackedInt32();
            var output = Encoding.UTF8.GetString(Buffer.AsSpan(ReadPosition, len));
            Position += len;
            return output;
        }

        public ReadOnlyMemory<byte> ReadBytesAndSize()
        {
            var len = ReadPackedInt32();
            return ReadBytes(len);
        }

        public ReadOnlyMemory<byte> ReadBytes(int length)
        {
            var output = Buffer.AsMemory(ReadPosition, length);
            Position += length;
            return output;
        }

        public int ReadPackedInt32()
        {
            return (int)ReadPackedUInt32();
        }

        public uint ReadPackedUInt32()
        {
            bool readMore = true;
            int shift = 0;
            uint output = 0;

            while (readMore)
            {
                byte b = FastByte();
                if (b >= 0x80)
                {
                    readMore = true;
                    b ^= 0x80;
                }
                else
                {
                    readMore = false;
                }

                output |= (uint)(b << shift);
                shift += 7;
            }

            return output;
        }

        public void CopyTo(IMessageWriter writer)
        {
            writer.Write((ushort) Length);
            writer.Write((byte) Tag);
            writer.Write(Buffer.AsMemory(Offset, Length));
        }

        public void Seek(int position)
        {
            Position = position;
        }

        //This function really needs to be changed to be more generic
        //Maybe have it take an IMessageReader plus an offset plus a byte payload, and calculate the size changes inside the function
        public void EditMessage(IMessageReader par, IMessageReader message, String edit)
        {
            byte[] payload = System.Text.Encoding.ASCII.GetBytes(edit);
            int payloadLength = payload.Length;

            byte[] intBytes = BitConverter.GetBytes(payloadLength);
            byte payloadSize = intBytes[0];

            byte[] fixedPayload = new byte[payload.Length + 1];
            payload.CopyTo(fixedPayload, 1);
            fixedPayload[0] = payloadSize;

            //This function assumes it is called when an RPC message is detected
            //That means message.Offset points to the beginning of the RPC message info
            //But points after the RPC message length and tag
            //So adding 2 bytes to the Offset puts it right in front of the chat to edit, including the chat length
            int editPosition = message.Offset + 2;

            int extraSize = 0;
            if (Buffer.Length != fixedPayload.Length + editPosition)
            {
                extraSize = fixedPayload.Length + editPosition - Buffer.Length;
                var resizedBuffer = Buffer;
                Array.Resize(ref resizedBuffer , Buffer.Length + extraSize);
                Buffer = resizedBuffer;
            }

            var lengthOffset = message.Offset - 3;
            var curLen = message.Buffer[lengthOffset] |
                        (message.Buffer[lengthOffset + 1] << 8);

            curLen += extraSize;

            Buffer[lengthOffset] = (byte)curLen;
            Buffer[lengthOffset + 1] = (byte)(message.Buffer[lengthOffset + 1] >> 8);

            System.Buffer.BlockCopy(fixedPayload, 0, Buffer, editPosition, fixedPayload.Length);

            Console.WriteLine("Current message length: " + message.Length);
            Console.WriteLine("Current parent message length: " + ((MessageReader) message).Parent.Length);

            ((MessageReader) message).Parent.BadAdjustLength(message.Offset, extraSize);


            Console.WriteLine("Post edit message length: " + message.Length);
            Console.WriteLine("Post edit parent message length: " + ((MessageReader) message).Parent.Length);


        }

        public void RemoveMessage(IMessageReader message)
        {
            if (message.Buffer != Buffer)
            {
                throw new ImpostorProtocolException("Tried to remove message from a message that does not have the same buffer.");
            }

            // Offset of where to start removing.
            var offsetStart = message.Offset - 3;

            // Offset of where to end removing.
            var offsetEnd = message.Offset + message.Length;

            // The amount of bytes to copy over ourselves.
            var lengthToCopy = message.Buffer.Length - offsetEnd;

            System.Buffer.BlockCopy(Buffer, offsetEnd, Buffer, offsetStart, lengthToCopy);

            ((MessageReader) message).Parent.AdjustLength(message.Offset, message.Length + 3);
        }

        private void AdjustLength(int offset, int amount)
        {
            this.Length -= amount;

            if (this.ReadPosition > offset)
            {
                this.Position -= amount;
            }

            if (Parent != null)
            {
                var lengthOffset = this.Offset - 3;
                var curLen = this.Buffer[lengthOffset] |
                             (this.Buffer[lengthOffset + 1] << 8);

                curLen -= amount;

                this.Buffer[lengthOffset] = (byte)curLen;
                this.Buffer[lengthOffset + 1] = (byte)(this.Buffer[lengthOffset + 1] >> 8);

                Parent.AdjustLength(offset, amount);
            }
        }

        private void BadAdjustLength(int offset, int amount)
        {
            Console.WriteLine("Bad adjust length by: " + amount);
            this.Length += amount;

            if (this.ReadPosition > offset)
            {
                this.Position += amount;
            }

            //Figure out if the following block is necessary
            if (Parent != null)
            {
            //  (See among-us-protocol for examples of packets to better understand breakdown)
            //  SendChat example is a good one to follow

            //  Following line calculates the location of the first byte of the length of the current message
                var lengthOffset = this.Offset - 3;

            //  Following line calculates the total length of the current message by:
            //  1) Grabbing the first byte of the length of the message, using the offset calculated earlier
            //  2) Grab the second byte of the length of the message
            //  3) Add the bytes together using binary math
                var curLen = this.Buffer[lengthOffset] |
                             (this.Buffer[lengthOffset + 1] << 8);

            //  The following line re-calculates the current length for the current message
                curLen += amount;

            //  The following line sets the first byte of the length of the current message to the first byte of the re-calculated length
                this.Buffer[lengthOffset] = (byte)curLen;

            //  The following line sets the second buyes of the length of the current message to the second byte of the re-calculated length
                this.Buffer[lengthOffset + 1] = (byte)(this.Buffer[lengthOffset + 1] >> 8);

            //  Recursively adjust length until there are no more parents
                Parent.BadAdjustLength(offset, amount);
            }
        }

        public IMessageReader Copy(int offset = 0)
        {
            var reader = _pool.Get();
            reader.Update(Buffer, Offset + offset, Position, Length - offset, Tag, Parent);
            return reader;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte FastByte()
        {
            return Buffer[Offset + Position++];
        }

        public void Dispose()
        {
            if (_inUse)
            {
                _pool.Return(this);
            }
        }
    }
}
