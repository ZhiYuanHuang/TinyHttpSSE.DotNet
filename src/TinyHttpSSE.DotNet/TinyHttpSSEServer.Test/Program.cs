using System.IO;
using System.Text;
using TinyHttpSSE.Server;

namespace TinyHttpSSEServer.Test
{
    internal class Program
    {
        static void Main(string[] args) {
            Console.WriteLine("I'm SSE Server!");
            Thread.Sleep(1000 * 1);
           
            HttpSseServer server = new HttpSseServer("http://+:9111/msg/");

            server.StreamCreatedAction = (stream) =>{
                pushInfo(stream);
                Thread.Sleep(1000 );
            };
            server.Start();

            Thread.Sleep(1000 * 5);

            Random random = new Random();
            Dictionary<string, object> dict = new Dictionary<string, object>() { 
                { "Now",DateTime.MinValue},
                { "Serial",string.Empty}
            };
            Task.Run(() => {
                while (true) {
                    int count= random.Next(100,1000)%3+1;
                    DateTime curTime = DateTime.Now;
                    dict["Now"] = curTime;
                    dict["Serial"] = Guid.NewGuid().ToString("N");

                    //server.StreamManagement.Others.PushBytes(Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(dict)));
                    
                    server.StreamManagement.All.PushSseMsg("\r\nhello\r\n");
                    if (count >= 1) {
                        server.StreamManagement.All.PushSseMsg(curTime.ToString("yyyy-MM-dd\r\n"));
                    }
                    if (count >= 2) {
                        server.StreamManagement.All.PushSseMsg(curTime.ToString("HH:mm\r\n"));
                    }
                    if (count >= 3) {
                        server.StreamManagement.All.PushSseMsg(curTime.ToString("ss\r\n"));
                    }

                    Thread.Sleep((random.Next(100,200)%5+1)*1000);
                }
            });

            Console.ReadKey();

            server.StreamManagement.All.EndOfStream();

            Task.Delay(1000*5).Wait();

            server.Stopping().Wait();
        }

        static void pushInfo(BaseClientStream stream) {
            Random random = new Random(Guid.NewGuid().GetHashCode());
            int number = 0;
            string tmpStr = string.Empty;
            int randomLength =  random.Next(1, 4);
            while (!string.IsNullOrEmpty(tmpStr = _info.Substring(number, randomLength))) {
                
                stream.PushSseMsg(tmpStr).Wait();
                number += tmpStr.Length;

                if (number == _info.Length) {
                    randomLength = 0;
                }
                else if (number + 5 >_info.Length) {
                    randomLength = 1;
                } else {

                    randomLength = random.Next(1, 4);
                }
                
                Thread.Sleep(random.Next(100, 120));
            }

            stream.PushSseMsg("\r\n\r\n\r\n开始报时\r\n").Wait();
        }

        

        static string _info = @"
Server-Sent Events 概述
Server-Sent Events（SSE）是一种基于 HTTP 协议的服务器推送技术，它允许服务器以流的方式向客户端实时推送数据。与 WebSocket 等双向通信技术不同，SSE 专注于单向通信（从服务器到客户端），特别适合需要服务器主动推送数据的场景，如：

社交媒体的实时通知（评论、点赞、关注提醒）
股票价格、体育比分的实时更新
日志和事件流的实时监控
聊天应用中的消息提醒";
    }
}
