using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TinyHttpSSE.Server
{
    public class StreamPushManagement
    {
        public static int DispatchPushThreadNumer = 4;

        readonly ClientStreamManagement _clientStreamManagement;
        bool _isExited_1 = false;
        public StreamPushManagement(ClientStreamManagement streamManagement) {
            _clientStreamManagement = streamManagement;
        }

        CancellationTokenSource _cts;
        bool _isStarted = false;
        internal void Start() {
            if (_isStarted) {
                return;
            }

            _cts = new CancellationTokenSource();

            for (int i = 0; i < DispatchPushThreadNumer; i++) {
                Thread t = new Thread(cyclePush);
                if (i == 0) {
                    t.Priority = ThreadPriority.Highest;
                } else {
                    t.Priority = ThreadPriority.AboveNormal;
                }
                t.IsBackground = true;
                t.Start(_cts);
            }

            Thread _heartThread = new Thread(cycleHeart);
            _heartThread.IsBackground = true;
            _heartThread.Start(_cts);

            _isStarted = true;
        }

        internal async Task Stopping() {
            _cts.Cancel();

            await Task.Delay(100);

            SpinWait.SpinUntil(() => _isExited_1);
        }

        void cycleHeart(object obj) {
            CancellationTokenSource cts = obj as CancellationTokenSource;
            Thread.Sleep(1000);

            DateTime lastUpdateClientsTime = DateTime.MinValue;
            TimeSpan syncClientsInterval = TimeSpan.FromSeconds(5);
            List<string> sessionIdList = new List<string>();
            while (true) {
                if (cts.IsCancellationRequested) {
                    break;
                }

                try {
                    if (DateTime.Now.Subtract(lastUpdateClientsTime) > syncClientsInterval) {
                        sessionIdList.Clear();
                        sessionIdList.AddRange(_clientStreamManagement.InternalAll.Keys);
                        lastUpdateClientsTime = DateTime.Now;
                    }

                    foreach (var sessionId in sessionIdList) {
                        if (!_clientStreamManagement.InternalAll.TryGetValue(sessionId, out BaseClientStream clientStream)) {
                            continue;
                        }

                        clientStream.TriggerHeart();
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "cycleHeart raise error");
                }

                Thread.Sleep(10);
            }
        }

        int pushWorkIndex = 0;

        void cyclePush(object obj) {
            CancellationTokenSource cts = obj as CancellationTokenSource;
            Thread.Sleep(1000);

            int myPushWorkIndex = Interlocked.Increment(ref pushWorkIndex);

            DateTime lastUpdateClientsTime = DateTime.MinValue;
            TimeSpan syncClientsInterval = TimeSpan.FromMilliseconds(500);
            List<string> sessionIdList = new List<string>();
            while (true) {
                if (cts.IsCancellationRequested) {
                    break;
                }

                try {
                    if (DateTime.Now.Subtract(lastUpdateClientsTime) > syncClientsInterval) {
                        sessionIdList.Clear();
                        sessionIdList.AddRange(_clientStreamManagement.InternalAll.Keys);
                        lastUpdateClientsTime = DateTime.Now;
                    }

                    foreach (var sessionId in sessionIdList) {
                        if (!_clientStreamManagement.InternalAll.TryGetValue(sessionId, out BaseClientStream clientStream)) {
                            continue;
                        }

                        if (!clientStream.CompeteDispatch()) {
                            continue;
                        }
                        if (!clientStream.CheckNeedDispatch()) {
                            clientStream.ReleaseDispatch();
                            continue;
                        }

                        push(clientStream).ConfigureAwait(false);
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "cyclePush raise error");
                }

                Thread.Sleep(1);
            }

            if (myPushWorkIndex == 1) {
                sessionIdList.Clear();
                sessionIdList.AddRange(_clientStreamManagement.InternalAll.Keys);

                foreach (var sessionId in sessionIdList) {
                    if (!_clientStreamManagement.InternalAll.TryGetValue(sessionId, out BaseClientStream clientStream)) {
                        continue;
                    }

                    if (!clientStream.CompeteDispatch()) {
                        continue;
                    }
                    if (!clientStream.CheckNeedDispatch()) {
                        clientStream.ReleaseDispatch();
                        continue;
                    }

                    push(clientStream).ConfigureAwait(false);
                }

                Task.Delay(1000 * 3).Wait();

                _isExited_1 = true;
            }
        }

        async Task push(BaseClientStream stream) {
            await Task.Run(async () => {
                bool success = false;
                try {
                    success = await stream.DispatchPushBytes();

                    if (!success) {
                        _clientStreamManagement.Delete(stream);
                        stream.Dispose();
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "push rasie error");
                } finally {
                    stream.ReleaseDispatch();
                }

            });

        }
    }
}
