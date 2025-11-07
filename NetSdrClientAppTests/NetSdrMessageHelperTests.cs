using NetSdrClientApp.Messages;

namespace NetSdrClientAppTests
{
    public class NetSdrMessageHelperTests
    {
        [SetUp]
        public void Setup()
        {
        }
 
        [Test]
        public void GetControlItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.Ack;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            int parametersLength = 7500;
            //Act
            byte[] msg = NetSdrMessageHelper.GetControlItemMessage(type, code, new byte[parametersLength]);
            var headerBytes = msg.Take(2).ToArray();
            var codeBytes = msg.Skip(2).Take(2).ToArray();
            var parametersBytes = msg.Skip(4).ToArray();
            var num = BitConverter.ToUInt16(headerBytes);
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);
            var actualCode = BitConverter.ToInt16(codeBytes);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(headerBytes, Has.Length.EqualTo(2));
                Assert.That(msg, Has.Length.EqualTo(actualLength));
                Assert.That(type, Is.EqualTo(actualType));
                Assert.That(actualCode, Is.EqualTo((short)code));
                Assert.That(parametersBytes, Has.Length.EqualTo(parametersLength));
            });
        }

        [Test]
        public void GetDataItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            int parametersLength = 7500;
            //Act
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, new byte[parametersLength]);
            var headerBytes = msg.Take(2).ToArray();
            var parametersBytes = msg.Skip(2).ToArray();
            var num = BitConverter.ToUInt16(headerBytes);
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(headerBytes, Has.Length.EqualTo(2));
                Assert.That(msg, Has.Length.EqualTo(actualLength));
                Assert.That(type, Is.EqualTo(actualType));
                Assert.That(parametersBytes, Has.Length.EqualTo(parametersLength));
            });
        }

        [Test]
        public void GetControlItemMessage_NullParameters_ThrowsArgumentNullException()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverFrequency;
            //Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                NetSdrMessageHelper.GetControlItemMessage(type, code, null));
            Assert.That(exception.ParamName, Is.EqualTo("parameters"));
        }

        [Test]
        public void GetDataItemMessage_NullParameters_ThrowsArgumentNullException()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;
            //Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                NetSdrMessageHelper.GetDataItemMessage(type, null));
            Assert.That(exception.ParamName, Is.EqualTo("parameters"));
        }

        [Test]
        public void GetControlItemMessage_ExceedsMaxLength_ThrowsArgumentException()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.RFFilter;
            var parameters = new byte[8200]; // Перевищує MaxMessageLength
            //Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                NetSdrMessageHelper.GetControlItemMessage(type, code, parameters));
            Assert.That(exception.ParamName, Is.EqualTo("msgLength"));
        }

        [Test]
        public void GetDataItemMessage_MaxDataItemLength_CreatesCorrectHeader()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem1;
            var parameters = new byte[8192]; // MaxDataItemMessageLength - 2 (header)
            //Act
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);
            var headerBytes = msg.Take(2).ToArray();
            var num = BitConverter.ToUInt16(headerBytes);
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(actualType, Is.EqualTo(type));
                Assert.That(actualLength, Is.EqualTo(0)); // Should be 0 for max length
            });
        }

        [Test]
        public void TranslateMessage_ValidControlItemMessage_ReturnsTrue()
        {
            //Arrange
            var originalType = NetSdrMessageHelper.MsgTypes.CurrentControlItem;
            var originalCode = NetSdrMessageHelper.ControlItemCodes.IQOutputDataSampleRate;
            var originalParams = new byte[] { 0x01, 0x02, 0x03 };
            var msg = NetSdrMessageHelper.GetControlItemMessage(originalType, originalCode, originalParams);
            //Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var type, out var code, 
                out var seqNum, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.True);
                Assert.That(type, Is.EqualTo(originalType));
                Assert.That(code, Is.EqualTo(originalCode));
                Assert.That(seqNum, Is.EqualTo(0));
                Assert.That(body, Is.EqualTo(originalParams));
            });
        }

        [Test]
        public  void TranslateMessage_ValidDataItemMessage_ReturnsTrue()
        {
            //Arrange
            var originalType = NetSdrMessageHelper.MsgTypes.DataItem3;
            var originalParams = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            var msg = NetSdrMessageHelper.GetDataItemMessage(originalType, originalParams);
            // Додаємо sequence number вручну (перші 2 байти після header)
            Array.Copy(BitConverter.GetBytes((ushort)42), 0, msg, 2, 2);
            //Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var type, out var code, 
                out var seqNum, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.True);
                Assert.That(type, Is.EqualTo(originalType));
                Assert.That(code, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
                Assert.That(seqNum, Is.EqualTo(42));
            });
        }

        [Test]
        public void TranslateMessage_NullMessage_ThrowsArgumentNullException()
        {
            //Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                NetSdrMessageHelper.TranslateMessage(null, out _, out _, out _, out _));
            Assert.That(exception.ParamName, Is.EqualTo("msg"));
        }

        [Test]
        public void TranslateMessage_MessageTooShort_ReturnsFalse()
        {
            //Arrange
            var msg = new byte[] { 0x01 }; // Менше ніж MsgHeaderLength
            //Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var type, out var code, 
                out var seqNum, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.False);
                Assert.That(body, Is.Empty);
            });
        }

        [Test]
        public void TranslateMessage_InvalidControlItemCode_ReturnsFalse()
        {
            //Arrange
            var msg = new byte[6];
            // Встановлюємо header для SetControlItem
            var headerValue = (ushort)(6 + ((int)NetSdrMessageHelper.MsgTypes.SetControlItem << 13));
            Array.Copy(BitConverter.GetBytes(headerValue), 0, msg, 0, 2);
            // Встановлюємо невалідний код (не існує в enum)
            Array.Copy(BitConverter.GetBytes((ushort)9999), 0, msg, 2, 2);
            //Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var type, out var code, 
                out var seqNum, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.False);
                Assert.That(type, Is.EqualTo(NetSdrMessageHelper.MsgTypes.SetControlItem));
            });
        }

        [Test]
        public void TranslateMessage_IncompleteControlItemMessage_ReturnsFalse()
        {
            //Arrange
            var msg = new byte[3]; // Header + 1 байт (замало для control item)
            var headerValue = (ushort)(3 + ((int)NetSdrMessageHelper.MsgTypes.SetControlItem << 13));
            Array.Copy(BitConverter.GetBytes(headerValue), 0, msg, 0, 2);
            //Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out _, out _, out _, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.False);
                Assert.That(body, Is.Empty);
            });
        }

        [Test]
        public void TranslateMessage_IncompleteDataItemMessage_ReturnsFalse()
        {
            //Arrange
            var msg = new byte[3]; // Header + 1 байт (замало для sequence number)
            var headerValue = (ushort)(3 + ((int)NetSdrMessageHelper.MsgTypes.DataItem0 << 13));
            Array.Copy(BitConverter.GetBytes(headerValue), 0, msg, 0, 2);
            //Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out _, out _, out _, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.False);
                Assert.That(body, Is.Empty);
            });
        }

        [Test]
        public void TranslateMessage_MaxDataItemLength_ParsesCorrectly()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            var parameters = new byte[8192]; // Max length
            var msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);
            //Act
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var parsedType, 
                out var code, out var seqNum, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.True);
                Assert.That(parsedType, Is.EqualTo(type));
                Assert.That(body, Has.Length.EqualTo(8190)); // 8192 - 2 (sequence number)
            });
        }

        [Test]
        public void GetSamples_ValidInput8Bit_ReturnsCorrectSamples()
        {
            //Arrange
            ushort sampleSize = 8;
            var body = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            //Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToList();
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(samples, Has.Count.EqualTo(4));
                Assert.That(samples[0], Is.EqualTo(1));
                Assert.That(samples[1], Is.EqualTo(2));
                Assert.That(samples[2], Is.EqualTo(3));
                Assert.That(samples[3], Is.EqualTo(4));
            });
        }

        [Test]
        public void GetSamples_ValidInput16Bit_ReturnsCorrectSamples()
        {
            //Arrange
            ushort sampleSize = 16;
            var body = new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 };
            //Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToList();
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(samples, Has.Count.EqualTo(3));
                Assert.That(samples[0], Is.EqualTo(1));
                Assert.That(samples[1], Is.EqualTo(2));
                Assert.That(samples[2], Is.EqualTo(3));
            });
        }

        [Test]
        public void GetSamples_ValidInput24Bit_ReturnsCorrectSamples()
        {
            //Arrange
            ushort sampleSize = 24;
            var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
            //Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToList();
            //Assert
            Assert.That(samples, Has.Count.EqualTo(2));
        }

        [Test]
        public void GetSamples_ValidInput32Bit_ReturnsCorrectSamples()
        {
            //Arrange
            ushort sampleSize = 32;
            var body = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00 };
            //Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToList();
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(samples, Has.Count.EqualTo(2));
                Assert.That(samples[0], Is.EqualTo(1));
                Assert.That(samples[1], Is.EqualTo(2));
            });
        }

        [Test]
        public void GetSamples_NullBody_ThrowsArgumentNullException()
        {
            //Arrange
            ushort sampleSize = 16;
            //Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                NetSdrMessageHelper.GetSamples(sampleSize, null).ToList());
            Assert.That(exception.ParamName, Is.EqualTo("body"));
        }

        [Test]
        public void GetSamples_InvalidSampleSizeTooSmall_ThrowsArgumentOutOfRangeException()
        {
            //Arrange
            ushort sampleSize = 0;
            var body = new byte[] { 0x01, 0x02 };
            //Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                NetSdrMessageHelper.GetSamples(sampleSize, body).ToList());
            Assert.That(exception.ParamName, Is.EqualTo("sampleSize"));
        }

        [Test]
        public void GetSamples_InvalidSampleSizeTooLarge_ThrowsArgumentOutOfRangeException()
        {
            //Arrange
            ushort sampleSize = 40; // Більше 32 біт
            var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            //Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                NetSdrMessageHelper.GetSamples(sampleSize, body).ToList());
            Assert.That(exception.ParamName, Is.EqualTo("sampleSize"));
        }

        [Test]
        public void GetSamples_IncompleteLastSample_IgnoresIt()
        {
            //Arrange
            ushort sampleSize = 16;
            var body = new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03 }; // Останній семпл неповний
            //Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToList();
            //Assert
            Assert.That(samples, Has.Count.EqualTo(2)); // Тільки 2 повні семпли
        }

        [Test]
        public void GetSamples_EmptyBody_ReturnsEmptyEnumerable()
        {
            //Arrange
            ushort sampleSize = 16;
            var body = new byte[0];
            //Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToList();
            //Assert
            Assert.That(samples, Is.Empty);
        }

        [Test]
        public void GetControlItemMessage_AllControlItemCodes_ProduceValidMessages()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var parameters = new byte[] { 0x01, 0x02 };
            var codes = new[]
            {
                NetSdrMessageHelper.ControlItemCodes.None,
                NetSdrMessageHelper.ControlItemCodes.IQOutputDataSampleRate,
                NetSdrMessageHelper.ControlItemCodes.RFFilter,
                NetSdrMessageHelper.ControlItemCodes.ADModes,
                NetSdrMessageHelper.ControlItemCodes.ReceiverState,
                NetSdrMessageHelper.ControlItemCodes.ReceiverFrequency
            };
            //Act & Assert
            foreach (var code in codes)
            {
                var msg = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);
                Assert.Multiple(() =>
                {
                    Assert.That(msg, Is.Not.Null);
                    Assert.That(msg, Has.Length.GreaterThan(0));
                });
            }
        }

        [Test]
        public void GetDataItemMessage_AllDataItemTypes_ProduceValidMessages()
        {
            //Arrange
            var parameters = new byte[] { 0x01, 0x02, 0x03 };
            var types = new[]
            {
                NetSdrMessageHelper.MsgTypes.DataItem0,
                NetSdrMessageHelper.MsgTypes.DataItem1,
                NetSdrMessageHelper.MsgTypes.DataItem2,
                NetSdrMessageHelper.MsgTypes.DataItem3
            };
            //Act & Assert
            foreach (var type in types)
            {
                var msg = NetSdrMessageHelper.GetDataItemMessage(type, parameters);
                Assert.Multiple(() =>
                {
                    Assert.That(msg, Is.Not.Null);
                    Assert.That(msg, Has.Length.GreaterThan(0));
                });
            }
        }

        [Test]
        public void TranslateMessage_RoundTrip_PreservesData()
        {
            //Arrange
            var originalType = NetSdrMessageHelper.MsgTypes.ControlItemRange;
            var originalCode = NetSdrMessageHelper.ControlItemCodes.ADModes;
            var originalParams = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
            //Act
            var msg = NetSdrMessageHelper.GetControlItemMessage(originalType, originalCode, originalParams);
            bool success = NetSdrMessageHelper.TranslateMessage(msg, out var type, out var code, 
                out _, out var body);
            //Assert
            Assert.Multiple(() =>
            {
                Assert.That(success, Is.True);
                Assert.That(type, Is.EqualTo(originalType));
                Assert.That(code, Is.EqualTo(originalCode));
                Assert.That(body, Is.EqualTo(originalParams));
            });
        }

        [Test]
        public void GetSamples_LargeBody_ProcessesCorrectly()
        {
            //Arrange
            ushort sampleSize = 16;
            var body = new byte[1000]; // 500 семплів по 16 біт
            for (int i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 256);
            }
            //Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSize, body).ToList();
            //Assert
            Assert.That(samples, Has.Count.EqualTo(500));
        }
    }
}
