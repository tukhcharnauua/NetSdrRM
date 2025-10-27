using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static NetSdrClientApp.Messages.NetSdrMessageHelper;
using System.IO; // Додано для FileStream та BinaryWriter

namespace NetSdrClientApp
{
    public class NetSdrClient
    {
        // Рішення S2933: Поля readonly
        private readonly ITcpClient _tcpClient;
        private readonly IUdpClient _udpClient;
        
        public bool IQStarted { get; set; }

        // Рішення CS8618: Поле зроблене nullable (додано '?'),
        // оскільки воно ініціалізується лише в SendTcpRequest, а не в конструкторі.
        // Зверніть увагу: ми залишаємо його nullable, щоб полегшити скидання після отримання відповіді.
        private TaskCompletionSource<byte[]>? responseTaskSource; 

        public NetSdrClient(ITcpClient tcpClient, IUdpClient udpClient)
        {
            _tcpClient = tcpClient;
            _udpClient = udpClient;

            _tcpClient.MessageReceived += _tcpClient_MessageReceived;
            _udpClient.MessageReceived += _udpClient_MessageReceived;
        }

        public async Task ConnectAsync()
        {
            if (!_tcpClient.Connected)
            {
                _tcpClient.Connect();

                var sampleRate = BitConverter.GetBytes((long)100000).Take(5).ToArray();
                var automaticFilterMode = BitConverter.GetBytes((ushort)0).ToArray();
                var adMode = new byte[] { 0x00, 0x03 };

                //Host pre setup
                var msgs = new List<byte[]>
                {
                    NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.IQOutputDataSampleRate, sampleRate),
                    NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.RFFilter, automaticFilterMode),
                    NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ADModes, adMode),
                };

                foreach (var msg in msgs)
                {
                    await SendTcpRequest(msg);
                }
            }
        }

        public void Disconect()
        {
            _tcpClient.Disconnect();
        }

        public async Task StartIQAsync()
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return;
            }

            var iqDataMode = (byte)0x80;
            var start = (byte)0x02;
            var fifo16bitCaptureMode = (byte)0x01;
            var n = (byte)1;

            var args = new[] { iqDataMode, start, fifo16bitCaptureMode, n };

            var msg = NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverState, args);
            
            await SendTcpRequest(msg);

            IQStarted = true;

            _ = _udpClient.StartListeningAsync();
        }

        public async Task StopIQAsync()
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return;
            }

            var stop = (byte)0x01;

            var args = new byte[] { 0, stop, 0, 0 };

            var msg = NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverState, args);

            await SendTcpRequest(msg);

            IQStarted = false;

            _udpClient.StopListening();
        }

        public async Task ChangeFrequencyAsync(long hz, int channel)
        {
            var channelArg = (byte)channel;
            var frequencyArg = BitConverter.GetBytes(hz).Take(5);
            var args = new[] { channelArg }.Concat(frequencyArg).ToArray();

            var msg = NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverFrequency, args);

            await SendTcpRequest(msg);
        }

        // Залишаємо метод екземпляра
        private void _udpClient_MessageReceived(object? sender, byte[] e)
        {
            // Рішення S1481: Замінено невикористані out параметри (type, code, sequenceNum) на discard (_).
            NetSdrMessageHelper.TranslateMessage(e, out _, out _, out _, out byte[] body);
            var samples = NetSdrMessageHelper.GetSamples(16, body);

            // Цей Console.WriteLine може уповільнювати прийом UDP пакетів!
            Console.WriteLine($"Samples recieved: " + body.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));

            // Припускаючи, що FileStream та BinaryWriter доступні (наприклад, це консольний або десктопний додаток)
            // Додано перевірку на наявність System.IO для коректної компіляції
            using (FileStream fs = new FileStream("samples.bin", FileMode.Append, FileAccess.Write, FileShare.Read))
            using (BinaryWriter sw = new BinaryWriter(fs))
            {
                foreach (var sample in samples)
                {
                    sw.Write((short)sample); //write 16 bit per sample as configured 
                }
            }
        }

        // Виправлено: метод тепер викидає виняток замість повернення null.
        private async Task<byte[]> SendTcpRequest(byte[] msg)
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                // Виправлення помилки "Possible null reference return."
                throw new InvalidOperationException("TCP client is not connected."); 
            }

            responseTaskSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var responseTask = responseTaskSource.Task;

            await _tcpClient.SendMessageAsync(msg);

            var resp = await responseTask;

            // resp тепер гарантовано не null, якщо TaskCompletionSource успішно завершився
            return resp;
        }

        private void _tcpClient_MessageReceived(object? sender, byte[] e)
        {
            // Виправлення TODO: add Unsolicited messages handling here
            // Спочатку перекладаємо повідомлення, щоб визначити його тип
            NetSdrMessageHelper.TranslateMessage(e, out MsgTypes type, out ControlItemCodes code, out _, out _);
            
            if (responseTaskSource != null)
            {
                // Припускаємо, що будь-яке повідомлення, отримане під час очікування, є відповіддю
                responseTaskSource.SetResult(e);
                responseTaskSource = null; // Скидаємо очікування
                Console.WriteLine("Response recieved: " + e.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));
            }
            else
            {
                // Логіка для обробки Unsolicited messages (небажаних повідомлень)
                // Наприклад, якщо MsgTypes.Notification є небажаним типом
                if (type == MsgTypes.Notification) 
                {
                    Console.WriteLine($"Unsolicited NOTIFICATION received: Code={code}.");
                    // Тут може бути додаткова логіка обробки цього типу повідомлення
                }
                else
                {
                    // Логування інших несподіваних повідомлень
                    Console.WriteLine($"Unexpected TCP message (not response, not notification) recieved: Type={type}, Code={code}. Data: " 
                                      + e.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));
                }
            }
        }
    }
}
