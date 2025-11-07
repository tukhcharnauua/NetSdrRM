using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace NetSdrClientApp.Messages
{
    public static class NetSdrMessageHelper
    {
        private const int MaxMessageLength = 8191;
        private const int MaxDataItemMessageLength = 8194;
        private const int MsgHeaderLength = 2;
        private const int MsgControlItemLength = 2;
        private const int MsgSequenceNumberLength = 2;

        public enum MsgTypes
        {
            SetControlItem,
            CurrentControlItem,
            ControlItemRange,
            Ack,
            DataItem0,
            DataItem1,
            DataItem2,
            DataItem3
        }

        public enum ControlItemCodes
        {
            None = 0,
            IQOutputDataSampleRate = 0x00B8,
            RFFilter = 0x0044,
            ADModes = 0x008A,
            ReceiverState = 0x0018,
            ReceiverFrequency = 0x0020
        }

        public static byte[] GetControlItemMessage(MsgTypes type, ControlItemCodes itemCode, byte[] parameters)
        {
            return GetMessage(type, itemCode, parameters);
        }

        public static byte[] GetDataItemMessage(MsgTypes type, byte[] parameters)
        {
            return GetMessage(type, ControlItemCodes.None, parameters);
        }

        [ExcludeFromCodeCoverage]
        private static byte[] GetMessage(MsgTypes type, ControlItemCodes itemCode, byte[] parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters), "Parameters cannot be null");
            }

            var itemCodeBytes = new byte[0];
            if (itemCode != ControlItemCodes.None)
            {
                itemCodeBytes = BitConverter.GetBytes((ushort)itemCode);
            }

            var headerBytes = GetHeader(type, itemCodeBytes.Length + parameters.Length);
            var msg = new List<byte>(headerBytes.Length + itemCodeBytes.Length + parameters.Length);
            msg.AddRange(headerBytes);
            msg.AddRange(itemCodeBytes);
            msg.AddRange(parameters);

            return msg.ToArray();
        }

        [ExcludeFromCodeCoverage]
        public static bool TranslateMessage(byte[] msg, out MsgTypes type, out ControlItemCodes itemCode, out ushort sequenceNumber, out byte[] body)
        {
            if (msg == null)
            {
                throw new ArgumentNullException(nameof(msg), "Message cannot be null");
            }

            if (msg.Length < MsgHeaderLength)
            {
                type = default;
                itemCode = ControlItemCodes.None;
                sequenceNumber = 0;
                body = new byte[0];
                return false;
            }

            itemCode = ControlItemCodes.None;
            sequenceNumber = 0;
            bool success = true;

            TranslateHeader(msg.Take(MsgHeaderLength).ToArray(), out type, out int msgLength);

            int offset = MsgHeaderLength;
            int remainingLength = msgLength - MsgHeaderLength;

            if (type < MsgTypes.DataItem0)
            {
                if (msg.Length < offset + MsgControlItemLength)
                {
                    body = new byte[0];
                    return false;
                }

                var value = BitConverter.ToUInt16(msg, offset);
                offset += MsgControlItemLength;
                remainingLength -= MsgControlItemLength;

                if (Enum.IsDefined(typeof(ControlItemCodes), (int)value))
                {
                    itemCode = (ControlItemCodes)value;
                }
                else
                {
                    success = false;
                }
            }
            else
            {
                if (msg.Length < offset + MsgSequenceNumberLength)
                {
                    body = new byte[0];
                    return false;
                }

                sequenceNumber = BitConverter.ToUInt16(msg, offset);
                offset += MsgSequenceNumberLength;
                remainingLength -= MsgSequenceNumberLength;
            }

            if (msg.Length < offset + remainingLength)
            {
                body = new byte[0];
                return false;
            }

            body = new byte[remainingLength];
            Array.Copy(msg, offset, body, 0, remainingLength);
            
            success &= body.Length == remainingLength;
            return success;
        }

        public static IEnumerable<int> GetSamples(ushort sampleSize, byte[] body)
        {
            ValidateGetSamplesParameters(sampleSize, body);
            return GetSamplesIterator(sampleSize, body);
        }

        private static void ValidateGetSamplesParameters(ushort sampleSize, byte[] body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body), "Body cannot be null");
            }

            int sampleSizeBytes = sampleSize / 8;
            if (sampleSizeBytes <= 0 || sampleSizeBytes > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleSize), 
                    sampleSize, 
                    "Sample size must be between 8 and 32 bits");
            }
        }

        private static IEnumerable<int> GetSamplesIterator(ushort sampleSize, byte[] body)
        {
            int sampleSizeBytes = sampleSize / 8;
            int offset = 0;
            var buffer = new byte[4];

            while (offset + sampleSizeBytes <= body.Length)
            {
                Array.Clear(buffer, 0, 4);
                Array.Copy(body, offset, buffer, 0, sampleSizeBytes);
                yield return BitConverter.ToInt32(buffer, 0);
                offset += sampleSizeBytes;
            }
        }

        private static byte[] GetHeader(MsgTypes type, int msgLength)
        {
            int lengthWithHeader = msgLength + MsgHeaderLength;
            if (type >= MsgTypes.DataItem0 && lengthWithHeader == MaxDataItemMessageLength)
            {
                lengthWithHeader = 0;
            }

            if (msgLength < 0 || lengthWithHeader > MaxMessageLength)
            {
                throw new ArgumentException(
                    $"Message length {msgLength} exceeds allowed value", 
                    nameof(msgLength));
            }

            return BitConverter.GetBytes((ushort)(lengthWithHeader + ((int)type << 13)));
        }

        private static void TranslateHeader(byte[] header, out MsgTypes type, out int msgLength)
        {
            var num = BitConverter.ToUInt16(header, 0);
            type = (MsgTypes)(num >> 13);
            msgLength = num - ((int)type << 13);

            if (type >= MsgTypes.DataItem0 && msgLength == 0)
            {
                msgLength = MaxDataItemMessageLength;
            }
        }
    }
}
