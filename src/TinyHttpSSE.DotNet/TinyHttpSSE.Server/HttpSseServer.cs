using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TinyHttpSSE.Server
{
    public class HttpSseServer : IDisposable
    {
        public Func<HttpListenerContext, bool> VerifyClientFunc;
        public Func<HttpListenerContext, BaseClientStream> StreamCreateFunc;
        public Action<BaseClientStream> StreamCreatedAction;

        public readonly ClientStreamManagement StreamManagement;

        private readonly StreamPushManagement _streamPushManagement;
        private readonly CancellationTokenSource _cts;
        private HttpListener _httpListener;

        public HttpSseServer(string listenUrl) {
            StreamManagement = new ClientStreamManagement();
            _streamPushManagement = new StreamPushManagement(StreamManagement);
            _cts = new CancellationTokenSource();
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(listenUrl);
        }

        public bool Start() {
            try {
                _httpListener.Start();

                _httpListener.BeginGetContext(new AsyncCallback(getContext), _httpListener);

                _streamPushManagement.Start();
            } catch (Exception ex) {
                Log.Error(ex, "HttpSseServer start raise error");
                return false;
            }

            return true;
        }

        public async Task Stopping() {
            _cts.Cancel();

            await _streamPushManagement.Stopping();
        }

        void getContext(IAsyncResult ar) {
            HttpListener httpListener = ar.AsyncState as HttpListener;
            try {
                HttpListenerContext context = null;
                try {
                    context = httpListener.EndGetContext(ar);  //接收到的请求context（一个环境封装体）
                } catch {

                }
                
                if (!_cts.IsCancellationRequested) {
                    httpListener.BeginGetContext(new AsyncCallback(getContext), httpListener);  //开始 第二次 异步接收request请求
                }

                if (context == null) {
                    return;
                }

                if (VerifyClientFunc != null) {
                    if (!VerifyClientFunc.Invoke(context)) {
                        return;
                    }
                }

                BaseClientStream stream = null;
                if (StreamCreateFunc != null) {
                    stream = StreamCreateFunc(context);
                } else {
                    stream = new DefaultClientStream(context);
                }

                if (stream == null) {
                    return;
                }

                StreamManagement.InternalAll.Put(stream);

                if (StreamCreatedAction != null) {
                    StreamCreatedAction.Invoke(stream);
                }

                StreamManagement.All.Put(stream);

            } catch (Exception ex) {
                Log.Error(ex, "getContext raise error");
            } 
        }


        private void close() {
            try {
                _httpListener?.Stop();
                _httpListener?.Close();
            } finally {
                _httpListener = null;
            }
        }

        private bool disposedValue;

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: 释放托管状态(托管对象)
                }

                close();

                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue = true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~StreamabelHttpServer()
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
