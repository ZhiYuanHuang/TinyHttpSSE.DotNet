using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TinyHttpSSE.Server
{
    public abstract class BaseClientStream : IDisposable
    {
        public readonly string SessionId;
        public readonly HttpListenerRequest Request;
        public event EventHandler PushDispatchedEvent;
        public event EventHandler HeartEvent;
        public TimeSpan HeartInterval=TimeSpan.FromSeconds(30);

        public string GroupName { get; internal set; }

        public bool WriteRaiseError {
            get => _writeRaiseError;
        }

        private HttpListenerResponse _response;
        private DateTime _lastHeartInvokeTime = DateTime.MinValue;

        private volatile bool _writeRaiseError = false;
        private int _dispatchStatus = 0;
        private readonly Semaphore _semaphore=null;

        public BaseClientStream(HttpListenerContext httpContext) {
            _semaphore = new Semaphore(1,1);
            Request=httpContext.Request;
            _response = httpContext.Response;
            
            _response.StatusCode = (int)HttpStatusCode.OK;
            _response.ContentType = "text/event-stream;charset=UTF-8";
            _response.AddHeader("Cache-Control", "no-cache");
            _response.AddHeader("Connection", "keep-alive");
            _response.AddHeader("Access-Control-Allow-Origin", "*");
            _response.AddHeader("Transfer-Encoding", "chunked");
            _response.ContentEncoding = Encoding.UTF8;

            SessionId = Guid.NewGuid().ToString();
            Console.WriteLine($"sse stream {SessionId} create");
        }

        internal bool CompeteDispatch() {
            if (Interlocked.CompareExchange(ref _dispatchStatus, 1, 0) != 0) {
                return false;
            }
            return true;
        }

        internal void ReleaseDispatch() {
            Volatile.Write(ref _dispatchStatus, 0);
        }

        public virtual bool CheckNeedDispatch() {
            return false;
        }

        public virtual bool DispatchPushBytes() {
            return true;
        }

        internal void TriggerHeart() {
            if (HeartEvent == null ) {
                return;
            }

            if (DateTime.Now.Subtract(_lastHeartInvokeTime) < HeartInterval) {
                return;
            }

            Task.Run(() => {
                try {
                    HeartEvent.Invoke(this, EventArgs.Empty);
                } finally {
                    _lastHeartInvokeTime = DateTime.Now;
                }
            });
        }

        internal void TriggerPushDispatched() {
            if (PushDispatchedEvent == null) {
                return;
            }

            try {
                PushDispatchedEvent.Invoke(this, EventArgs.Empty);
            } catch {

            }
        }

        const string DataPrefix = "data:";

        public virtual async Task<bool> PushSseMsg(string dataContent) {
            string msgStr = string.Concat(DataPrefix, dataContent, "\n\n");
            
            return await PushBytes(Encoding.UTF8.GetBytes(msgStr));
        }

        public virtual async Task<bool> PushBytes(byte[] byteArr, EnumMessageLevel enumMessageLevel = EnumMessageLevel.Middle) {
            if (_writeRaiseError) {
                return false;
            }

            return await InternalPushBytes(byteArr);
        }

        public async Task EndOfStream() {
            if (_writeRaiseError) {
                return;
            }

            await InternalPushBytes(Array.Empty<byte>());
            Task.Run(() => {

                Dispose();
            }).ConfigureAwait(false);
        }

        internal async Task<bool> InternalPushBytes(byte[] byteArr) {

            _semaphore.WaitOne();

            bool result = false;
            try {
                string chunkSizeStr = byteArr.Length.ToString("X");
                byte[] chunkSizeByteArr= Encoding.ASCII.GetBytes(chunkSizeStr);
#if NET472
                await _response.OutputStream.WriteAsync(chunkSizeByteArr,0,chunkSizeByteArr.Length);
#else
                await _response.OutputStream.WriteAsync(chunkSizeByteArr);
#endif

                writeLine();
                if (byteArr.Length > 0) {
#if NET472
                    await _response.OutputStream.WriteAsync(byteArr,0,byteArr.Length);
#else
                    await _response.OutputStream.WriteAsync(byteArr);
#endif

                }
                writeLine();
                result = true;
            } catch {
                result = false;
                _writeRaiseError = true;
            } finally {
                _semaphore.Release();
            }

            return result;
        }

        private void writeLine() {
            _response.OutputStream.WriteByte(0x0d);
            _response.OutputStream.WriteByte(0x0a);
        }

        private void close() {
            try {
                _response?.Close();
            } finally {
                _response = null;
            }
        }

       
        private bool disposedValue;

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: 释放托管状态(托管对象)
                    if (HeartEvent != null) {
                        Delegate[] arr = HeartEvent.GetInvocationList();
                        for (int i = 0; i < arr.Length; i++) {
                            HeartEvent -= arr[i] as EventHandler;
                        }
                    }

                    if (PushDispatchedEvent != null) {
                        Delegate[] arr = PushDispatchedEvent.GetInvocationList();
                        for (int i = 0; i < arr.Length; i++) {
                            PushDispatchedEvent -= arr[i] as EventHandler;
                        }
                    }

                }

                close();
                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue = true;

                Console.WriteLine($"sse stream {SessionId} dispose");
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~BaseClientStream()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
