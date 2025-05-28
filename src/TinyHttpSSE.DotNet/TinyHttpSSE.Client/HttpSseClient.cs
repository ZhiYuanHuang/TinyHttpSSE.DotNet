using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace TinyHttpSSE.Client
{
    public class HttpSseClient
    {
        public readonly string SseServerUrl;
        private readonly HttpClient _httpClient;

        public event EventHandler<byte[]> ReceiveByteEvent;
        public event EventHandler<string> ReceiveSseMsgEvent;
        public event EventHandler ConnectBrokenEvent;
        public event EventHandler EndOfStreamEvent;

        public TimeSpan? ReceiveTimeout=null;

        private Thread _backgroundReceiveThread = null;
        private CancellationTokenSource _backgroudReceiveCts = null;

        public HttpSseClient(string sseServerUrl,bool verifyCert=false) {
            SseServerUrl = sseServerUrl;
            if (verifyCert) {
                _httpClient = new HttpClient();
            } else {
                var handler = new HttpClientHandler { 
                    ServerCertificateCustomValidationCallback= (message, cert, chain, errors) => { return true; }
                };

                _httpClient = new HttpClient(handler);
            }
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public bool Connected { get; private set; }

        public bool Connect() {
            bool connected = false;
            try {
                var request = new HttpRequestMessage(HttpMethod.Get, SseServerUrl);
                request.Headers.Add("Accept", "text/event-stream");
                var response = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                connected = true;

                _backgroudReceiveCts?.Cancel();
                _backgroudReceiveCts?.Dispose();
                _backgroudReceiveCts = null;
                _backgroundReceiveThread?.Join();
                _backgroundReceiveThread = null;

                _backgroudReceiveCts = new CancellationTokenSource();
                _backgroundReceiveThread = new Thread(backgroundReceive);
                _backgroundReceiveThread.Priority = ThreadPriority.AboveNormal; 
                _backgroundReceiveThread.IsBackground = true;
                _backgroundReceiveThread.Start(Tuple.Create(_backgroudReceiveCts,response));
            } catch (Exception ex) {
                connected = false;
                throw ex;
            } finally {
                Connected = connected;
            }

            return connected;
        }

        private void backgroundReceive(object obj) {
            Tuple<CancellationTokenSource, HttpResponseMessage> tuple= obj as Tuple<CancellationTokenSource, HttpResponseMessage>;
            CancellationTokenSource cts = tuple.Item1;
            HttpResponseMessage response = tuple.Item2;

            try {
                receiveFromRespStream(cts,response);
            } finally {
                Connected = false;
            }
        }

        private void receiveFromRespStream(CancellationTokenSource cts,HttpResponseMessage response) {
            Stream inputStream = null;
            try {
                inputStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                while (true) {
                    if (cts.IsCancellationRequested) {
                        break;
                    }

                    byte[] buffer = null;
                    var readChunkTask = Task.Run(() =>{
                        return readChunk(inputStream,out buffer);
                    });

                    if (ReceiveTimeout.HasValue) {
                        var tmpCts = new CancellationTokenSource();

                        var timeoutTask = Task.Delay(ReceiveTimeout.Value, tmpCts.Token);

                        int index = Task.WaitAny(readChunkTask, timeoutTask);
                        if (index == 1) {
                            triggerBrokenEvent();
                            break;
                        }
                        tmpCts.Cancel();
                    }
                    
                    bool result = readChunkTask.Result;

                    if (!result) {
                        triggerBrokenEvent();
                        break;
                    }
                    if (buffer.Length == 0) {
                        triggerEndOfStreamEvent();
                        break;
                    }

                    if (ReceiveByteEvent != null) {
                        try {
                            ReceiveByteEvent.Invoke(this, buffer);
                        }
                        catch(Exception ex) {
                            Log.Error(ex, "ReceiveByteEvent raise error");
                        }
                    } else if (ReceiveSseMsgEvent != null) {
                        resolveSseMsg(buffer, out string sseMsgStr);
                        if (!string.IsNullOrEmpty(sseMsgStr)) {
                            try {
                                ReceiveSseMsgEvent.Invoke(this, sseMsgStr);
                            } catch (Exception ex) {
                                Log.Error(ex, "ReceiveSseMsgEvent raise error");
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Log.Error(ex, "receiveFromRespStream raise error");
            } finally {
                inputStream?.Close();
                inputStream?.Dispose();
                response?.Dispose();
            }
        }

        private void resolveSseMsg(byte[] buffer, out string rawSseMsgStr) {
            rawSseMsgStr = string.Empty;
            try {
                rawSseMsgStr = Encoding.UTF8.GetString(buffer);
                if (rawSseMsgStr.StartsWith("data:") && rawSseMsgStr.EndsWith("\n\n")) {
                    rawSseMsgStr = rawSseMsgStr.Substring(5,rawSseMsgStr.Length-7);
                }
            }catch(Exception ex) {
                Log.Error(ex, "resolveSseMsg raise error");
            }
        }

        private void triggerBrokenEvent() {
            if (ConnectBrokenEvent != null) {
                Task.Run(() => {

                    ConnectBrokenEvent.Invoke(this, EventArgs.Empty);
                });
            }
        }

        private void triggerEndOfStreamEvent() {
            if (EndOfStreamEvent != null) {
                Task.Run(() => {

                    EndOfStreamEvent.Invoke(this, EventArgs.Empty);
                });
            }
        }

        private bool readChunk(Stream stream,out byte[] byteArr) {
            byteArr=null;

            try {
                bool result = readChunkSize(stream, out int chunkSize);
                if (!result) {
                    return false;
                }

                if (chunkSize == 0) {
                    byteArr = Array.Empty<byte>();
                    return true;
                }

                byteArr = new byte[chunkSize];
                stream.ReadAtLeast(byteArr, chunkSize);
                stream.ReadByte();
                stream.ReadByte();

                return true;
            } catch(Exception ex) {
                Log.Error(ex, "readChunk raise error");
                return false;
            }
        }

        private bool readChunkSize(Stream stream, out int chunkSize) {
            chunkSize = 0;
            int tmpIndex = 0;
            byte[] buffer = new byte[100];
        
            do {
                int byteInt= stream.ReadByte();
                if (byteInt == -1) {
                    return false;
                }else if (byteInt == 0x0d) {
                    stream.ReadByte();
                    break;
                }
                else {
                    buffer[tmpIndex++] =Convert.ToByte( byteInt);
                }
        
            } while (true);
        
            string chunkSizeStr= System.Text.Encoding.ASCII.GetString(buffer,0,tmpIndex);

            chunkSize = Convert.ToInt32(chunkSizeStr,16);
            if (chunkSize == 0) {
                stream.ReadByte();
                stream.ReadByte();
            }
        
            return true;
        }
    }
}
