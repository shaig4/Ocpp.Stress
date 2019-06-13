using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;

namespace stresser
{
    class Program
    {
        static void Main(string[] args)
        {
            NLog.LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration(
                                                                       System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                                                         "nlog.config"), false);

            var logger = NLog.LogManager.GetLogger("l");

            for (var t = 0; t < 300; t++)
            {
                Task.Run(() =>
                {
                   var p = new Program();
                   while (true)
                   {
                       try { p.Fllow(logger); } catch(Exception ex) { logger.Error(ex.ToString());  }
                   }
                 });
            }

            Console.WriteLine("Running.. ");
            Console.ReadKey(true);
        }
        void Fllow(NLog.Logger logger)
        { 
            var signal = new ManualResetEventSlim(false);
            string responseId=null;
            long? transactionId = null;

            using (var ws = new WebSocket(@"ws://127.0.0.1:8080/steve/websocket/CentralSystemService/stressy", "ocpp1.6"))
            {
                ws.OnMessage += (sender, e) =>
                {
                    Console.WriteLine("receive <- " + e.Data);
                    var arr = Newtonsoft.Json.JsonConvert.DeserializeObject<object[]>(e.Data);
                    if (e.Data.Contains("Error"))
                        responseId = null;
                    else
                        responseId = arr[1]?.ToString();

                    try
                    {
                        if (((dynamic)arr[2]).transactionId!=null)
                            transactionId = ((dynamic)arr[2]).transactionId;
                    }
                    catch { }
                    signal.Set();
                };
                ws.OnError += (sender, e) =>
                {
                    signal.Set();
                    Console.WriteLine("error <- " + e.Message);
                };

                ws.Connect();
             //   ws.SetCredentials("cp0001", "xxx",false);

                int num = 0;
                Send(num++, "BootNotification", new
                {
                    chargePointModel = "EV - X",
                    chargePointVendor = "XX"
                }, ws, signal, ref responseId, logger);

                Send(num++, "Authorize", new
                {
                    idTag = "abcd",
                }, ws, signal, ref responseId, logger);

                Send(num++, "StatusNotification", new
                {
                    connectorId = 0,
                    errorCode = 1,
                    info = "nonontify",
                    status = "Charging",
                    timestamp = DateTime.UtcNow,
                    vendorErrorCode = "no error",
                    vendorId = "no vendor id",
            }, ws, signal, ref responseId, logger);

                transactionId = null;
                Send(num++, "StartTransaction", new
                {
                    idTag = "abcd",
                    meterStart = 120,
                    reservationId = 0,
                    timestamp = DateTime.UtcNow,
                }, ws, signal, ref responseId, logger);

                Send(num++, "MeterValues", new
                {
                    transactionId = transactionId,
                    connectorId = 0,
                    meterValue =new object[] {
                                    new {  sampledValue = new object[] { new { value = "ok" } },
                                            timestamp = DateTime.UtcNow } },
                }, ws, signal, ref responseId, logger);

                Send(num++, "StopTransaction", new
                {
                    transactionId = transactionId,
                    timestamp = DateTime.UtcNow,
                    meterStop = 12
                }, ws, signal, ref responseId, logger);

                Send(num++, "Heartbeat", new
                {
                }, ws, signal, ref responseId, logger);




                Send(num++, "RemoteStartTransaction", new
                {
                    connectorId = 7,
                    idTag = "abcd",
                }, ws, signal, ref responseId, logger);

                Send(num++, "RemoteStopTransaction", new
                {
                    transactionId = transactionId
                }, ws, signal, ref responseId, logger);


            }
        }

        void WaitRandom()
        {
            var ms=new Random().Next(10, 10000);
            Thread.Sleep(ms);
        }
        private enum MsgType
        {
            req = 2,
            res = 3,
            err = 4
        }
         void Send(int num, string action, object msg, WebSocket ws, ManualResetEventSlim signal, ref string resId, NLog.Logger logger)
        {
            WaitRandom();

            signal.Reset();
            resId = null;

            var arr = new object[]{
                            (int)MsgType.req,
                            num.ToString(),
                            action,
                            msg };
            var data = Newtonsoft.Json.JsonConvert.SerializeObject(arr);
            Console.WriteLine("send -> " + data);
            ws.Send(data);
            var watch = Stopwatch.StartNew();
            var ok = signal.Wait(TimeSpan.FromSeconds(60)) && num.ToString() == resId;

            logger.Info($"{ok}|{action}|{watch.ElapsedMilliseconds}");

            if (ok)
                Console.WriteLine("OK!");
            else
                Console.WriteLine("Failed!");
        }
    }
}
