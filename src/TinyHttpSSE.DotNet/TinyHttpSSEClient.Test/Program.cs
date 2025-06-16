using System.Text;
using TinyHttpSSE.Client;

namespace TinyHttpSSEClient.Test
{
    internal class Program
    {
        static void Main(string[] args) {
            Console.WriteLine("I'm SSE Client!");

            Console.WriteLine();
            Console.WriteLine();

            string url = "http://127.0.0.1:9111/msg/";
            if (args.Length >= 1) {
                url= args[0];
            }

            HttpSseClient httpSseClient = new HttpSseClient(url,true);
            httpSseClient.EndOfStreamEvent += HttpSseClient_EndOfStreamEvent;
            httpSseClient.ReceiveSseMsgEvent += HttpSseClient_ReceiveSseMsgEvent;
            do {

                try {
                    bool result= httpSseClient.Connect();
                    if (result) {
                        break;
                    }
                } catch {

                }

                Thread.Sleep(100);
                
            } while (true);

            Console.ReadKey();
        }

        private static void HttpSseClient_ReceiveByteEvent(object? sender, byte[] e) {
            string jsonStr= Encoding.UTF8.GetString(e);
            Console.WriteLine(jsonStr);
        }

        private static void HttpSseClient_EndOfStreamEvent(object? sender, EventArgs e) {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("----- 流接收结束 -----");
        }

        private static void HttpSseClient_ReceiveSseMsgEvent(object? sender, string e) {
            Console.Write(e) ;
        }
    }
}
