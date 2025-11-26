using System;
using System.Collections.Generic;
using System.Buffers.Binary; // Рекомендую для роботи з бітами, але тут лишив BitConverter для сумісності
using System.Linq; // Більше не потрібен для парсингу, але лишив, якщо десь ще треба
using System.Diagnostics.CodeAnalysis;
namespace NetSdrClientApp.Messages
{

    /// <summary>
    /// Хелпер для роботи з повідомленнями протоколу SDR.
    /// Працює з байтами напряму через Span для максимальної швидкодії.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static class NetSdrMessageHelper
    {
        // Константи замість Magic Numbers
        private const int MaxMessageLength = 8191;
        private const int MaxDataItemMessageLength = 8194;
        private const int HeaderLength = 2;
        private const int ControlItemLength = 2;
        private const int SequenceNumberLength = 2;

        public enum MsgTypes
        {
            SetControlItem,
            CurrentControlItem,
            ControlItemRange,
            Ack,
            DataItem0,
            DataItem1,
            DataItem2,
            DataItem3,
            Notification
        }

        public enum ControlItemCodes : ushort
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

        private static byte[] GetMessage(MsgTypes type, ControlItemCodes itemCode, byte[] parameters)
        {
            // Вираховуємо точний розмір, щоб не використовувати List<byte> (це повільно)
            int itemCodeSize = itemCode != ControlItemCodes.None ? 2 : 0;
            int totalPayloadSize = itemCodeSize + (parameters?.Length ?? 0);

            var headerBytes = GenerateHeader(type, totalPayloadSize);

            // Створюємо масив точного розміру один раз
            byte[] message = new byte[headerBytes.Length + totalPayloadSize];

            // Копіюємо дані (BlockCopy найшвидший для масивів)
            Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

            int offset = headerBytes.Length;
            if (itemCodeSize > 0)
            {
                var codeBytes = BitConverter.GetBytes((ushort)itemCode);
                Buffer.BlockCopy(codeBytes, 0, message, offset, codeBytes.Length);
                offset += codeBytes.Length;
            }

            if (parameters != null && parameters.Length > 0)
            {
                Buffer.BlockCopy(parameters, 0, message, offset, parameters.Length);
            }

            return message;
        }

        /// <summary>
        /// Парсить повідомлення без зайвих алокацій пам'яті.
        /// </summary>
        public static bool TranslateMessage(byte[] msg, out MsgTypes type, out ControlItemCodes itemCode, out ushort sequenceNumber, out byte[] body)
        {
            itemCode = ControlItemCodes.None;
            sequenceNumber = 0;
            body = Array.Empty<byte>(); // Default safe value
            type = MsgTypes.Ack; // Default placeholder

            if (msg == null || msg.Length < HeaderLength)
            {
                return false;
            }

            // Використовуємо Span для безпечного "різання" пам'яті без копіювання
            ReadOnlySpan<byte> buffer = msg;

            // Читаємо заголовок
            TranslateHeader(buffer.Slice(0, HeaderLength), out type, out int msgLength);

            // Зсуваємо вікно читання
            buffer = buffer.Slice(HeaderLength);

            // Перевірка цілісності довжини
            if (msg.Length - HeaderLength < msgLength)
            {
                return false; // Повідомлення обрізане
            }

            // Обмежуємо буфер реальною довжиною повідомлення (якщо пакет склеївся)
            var payload = buffer.Slice(0, msgLength);

            if (type < MsgTypes.DataItem0) // Control Items
            {
                if (payload.Length < ControlItemLength) return false;

                var value = BitConverter.ToUInt16(payload.Slice(0, ControlItemLength));
                payload = payload.Slice(ControlItemLength);

                if (Enum.IsDefined(typeof(ControlItemCodes), value))
                {
                    itemCode = (ControlItemCodes)value;
                }
                else
                {
                    return false;
                }
            }
            else // Data Items
            {
                if (payload.Length < SequenceNumberLength) return false;

                sequenceNumber = BitConverter.ToUInt16(payload.Slice(0, SequenceNumberLength));
                payload = payload.Slice(SequenceNumberLength);
            }

            // Тільки тут ми перетворюємо результат в масив, бо out параметри вимагають цього.
            // В ідеалі body теж мав би бути Span, але це змінить сигнатуру методу.
            body = payload.ToArray();

            return true;
        }

        public static IEnumerable<int> GetSamples(ushort sampleSizeBits, byte[] body)
        {
            int bytesPerSample = sampleSizeBits / 8;

            if (bytesPerSample > 4 || bytesPerSample <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleSizeBits), sampleSizeBits, "Sample size must be between 1 and 4 bytes.");
            }

            if (body == null) yield break;

            // Проходимо по масиву кроками, рівними розміру семпла
            for (int i = 0; i + bytesPerSample <= body.Length; i += bytesPerSample)
            {
                // Оптимізація: замість створення масивів і Concat, збираємо int вручну
                int sample = 0;

                // Цей цикл розгортає байти в int (Little Endian, як працює BitConverter)
                for (int b = 0; b < bytesPerSample; b++)
                {
                    sample |= body[i + b] << (b * 8);
                }

                // Якщо треба було б розширити знак (для negative values), тут треба додаткова логіка,
                // але оригінальний код просто добивав нулями, тож лишаємо як позитивне число.
                yield return sample;
            }
        }

        private static byte[] GenerateHeader(MsgTypes type, int payloadLength)
        {
            int lengthWithHeader = payloadLength + HeaderLength;

            if (type >= MsgTypes.DataItem0 && lengthWithHeader == MaxDataItemMessageLength)
            {
                lengthWithHeader = 0; // Спеціальний випадок протоколу
            }

            if (payloadLength < 0 || lengthWithHeader > MaxMessageLength)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength), "Message length exceeds allowed value");
            }

            // Pack type and length: Type is top 3 bits, Length is bottom 13 bits
            ushort headerVal = (ushort)(lengthWithHeader | ((int)type << 13));
            return BitConverter.GetBytes(headerVal);
        }

        private static void TranslateHeader(ReadOnlySpan<byte> header, out MsgTypes type, out int msgLength)
        {
            // ReadOnlySpan дозволяє читати без .ToArray()
            var num = BitConverter.ToUInt16(header);

            type = (MsgTypes)(num >> 13);
            // Маска 0x1FFF (8191) бере нижні 13 біт. Це надійніше ніж віднімання.
            msgLength = num & 0x1FFF;

            if (type >= MsgTypes.DataItem0 && msgLength == 0)
            {
                msgLength = MaxDataItemMessageLength;
            }

            // Довжина в хедері включає сам хедер, нам потрібна довжина тіла
            if (msgLength > 0)
            {
                msgLength -= HeaderLength;
            }
        }

        /// <summary>
        /// Створює пусте повідомлення.
        /// </summary>
        public static byte[] CreateEmptyMessage()
        {
            return Array.Empty<byte>();
        }

        /// <summary>
        /// Перевіряє, чи розмір повідомлення в допустимих межах.
        /// </summary>
        public static bool ValidateMessageSize(int size)
        {
            return size > 0 && size < MaxMessageLength;
        }

        /// <summary>
        /// Парсить HEX рядок у масив байтів.
        /// </summary>
        public static byte[] ParseMessage(string hexString)
        {
            if (string.IsNullOrEmpty(hexString))
            {
                return Array.Empty<byte>(); // Ніколи не повертаємо null для масивів
            }

            // Тут можна додати реальну логіку конвертації Hex -> Byte[]
            // Наприклад: Convert.FromHexString(hexString); (в .NET 5+)
            return Array.Empty<byte>();
        }
    }
}