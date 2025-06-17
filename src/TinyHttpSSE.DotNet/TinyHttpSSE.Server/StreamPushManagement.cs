using Serilog;
using System;
using System.Collections.Concurrent;
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
        private static StreamPushManagement? instance = null;
        private static object _lockObj=new object();
        public static StreamPushManagement GetSingleton() {
            if (instance != null) {
                return instance; 
            }

            lock (_lockObj) {
                if (instance == null) {
                    instance = new StreamPushManagement();
                    instance.Start();
                }
            }

            return instance;
        }

        readonly ConcurrentDictionary<string, ClientStreamManagement> _clientStreamManageDict;
        private StreamPushManagement() {
            _clientStreamManageDict = new ConcurrentDictionary<string, ClientStreamManagement>();
        }

        public void AddStreamManagement(ClientStreamManagement clientStreamManagement) {
            _clientStreamManageDict.AddOrUpdate(clientStreamManagement.Id,clientStreamManagement,(k,o)=>clientStreamManagement);
        }

        public void RemoveStreamManagement(ClientStreamManagement clientStreamManagement) {
            _clientStreamManageDict.TryRemove(clientStreamManagement.Id,out _);
        }

        public async Task ClearStream(ClientStreamManagement clientStreamManagement) {
            List<string> sessionIdList= clientStreamManagement.InternalAll.Keys.ToList();

            List<Task> taskList=new List<Task>();
            foreach (var sessionId in sessionIdList) {
                if (!clientStreamManagement.InternalAll.TryGetValue(sessionId, out BaseClientStream clientStream)) {
                    continue;
                }

                if (!clientStream.CompeteDispatch()) {
                    continue;
                }
                if (!clientStream.CheckNeedDispatch()) {
                    clientStream.ReleaseDispatch();
                    continue;
                }

                var task= push(clientStreamManagement,clientStream);
                taskList.Add(task);
            }

            var timeoutTask = Task.Delay(1000*5);
            var waitTask= Task.WhenAll(taskList.ToArray());

            await Task.WhenAny(waitTask,timeoutTask);
        }

        bool _isStarted = false;
        private void Start() {
            if (_isStarted) {
                return;
            }

            for (int i = 0; i < DispatchPushThreadNumer; i++) {
                Thread t = new Thread(cyclePush);
                if (i == 0) {
                    t.Priority = ThreadPriority.Highest;
                } else {
                    t.Priority = ThreadPriority.AboveNormal;
                }
                t.IsBackground = true;
                t.Start();
            }

            Thread _heartThread = new Thread(cycleHeart);
            _heartThread.IsBackground = true;
            _heartThread.Start();

            _isStarted = true;
        }

        void cycleHeart(object obj) {
            Thread.Sleep(1000);

            Dictionary<string,DateTime> lastSyncClientsTimeDict= new Dictionary<string,DateTime>();
            Dictionary<string,List<string>> sessionIdsDict= new Dictionary<string,List<string>>();

            TimeSpan syncClientsInterval = TimeSpan.FromSeconds(5);
            List<string> sessionIdList = null;
            while (true) {
               
                try {
                    foreach(var clientManagementPair in _clientStreamManageDict) {
                        string clientManagementKey= clientManagementPair.Key;
                        ClientStreamManagement streamManagement = clientManagementPair.Value;

                        if (!lastSyncClientsTimeDict.ContainsKey(clientManagementKey)) {
                            lastSyncClientsTimeDict.Add(clientManagementKey, DateTime.MinValue);
                        }
                        if (!sessionIdsDict.ContainsKey(clientManagementKey)) {
                            sessionIdsDict.Add(clientManagementKey, new List<string>());
                        }
                        sessionIdList= sessionIdsDict[clientManagementKey];

                        if (DateTime.Now.Subtract(lastSyncClientsTimeDict[clientManagementKey]) > syncClientsInterval) {
                            sessionIdList.Clear();
                            sessionIdList.AddRange(streamManagement.InternalAll.Keys);
                            lastSyncClientsTimeDict[clientManagementKey] = DateTime.Now;
                        }

                        foreach (var sessionId in sessionIdList) {
                            if (!streamManagement.InternalAll.TryGetValue(sessionId, out BaseClientStream clientStream)) {
                                continue;
                            }

                            clientStream.TriggerHeart();
                        }
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "cycleHeart raise error");
                }

                Thread.Sleep(10);
            }
        }

        int pushWorkIndex = 0;

        void cyclePush(object obj) {
            Thread.Sleep(1000);

            int myPushWorkIndex = Interlocked.Increment(ref pushWorkIndex);

            Dictionary<string, DateTime> lastSyncClientsTimeDict = new Dictionary<string, DateTime>();
            Dictionary<string, List<string>> sessionIdsDict = new Dictionary<string, List<string>>();

            TimeSpan syncClientsInterval = TimeSpan.FromMilliseconds(500);
            List<string> sessionIdList = null;
            while (true) {
                
                try {
                    foreach (var clientManagementPair in _clientStreamManageDict) {
                        string clientManagementKey = clientManagementPair.Key;
                        ClientStreamManagement streamManagement = clientManagementPair.Value;

                        if (!lastSyncClientsTimeDict.ContainsKey(clientManagementKey)) {
                            lastSyncClientsTimeDict.Add(clientManagementKey, DateTime.MinValue);
                        }
                        if (!sessionIdsDict.ContainsKey(clientManagementKey)) {
                            sessionIdsDict.Add(clientManagementKey, new List<string>());
                        }
                        sessionIdList = sessionIdsDict[clientManagementKey];

                        if (DateTime.Now.Subtract(lastSyncClientsTimeDict[clientManagementKey]) > syncClientsInterval) {
                            sessionIdList.Clear();
                            sessionIdList.AddRange(streamManagement.InternalAll.Keys);
                            lastSyncClientsTimeDict[clientManagementKey] = DateTime.Now;
                        }

                        foreach (var sessionId in sessionIdList) {
                            if (!streamManagement.InternalAll.TryGetValue(sessionId, out BaseClientStream clientStream)) {
                                continue;
                            }

                            if (!clientStream.CompeteDispatch()) {
                                continue;
                            }
                            if (!clientStream.CheckNeedDispatch()) {
                                clientStream.ReleaseDispatch();
                                continue;
                            }

                            push(streamManagement,clientStream).ConfigureAwait(false);
                        }
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "cyclePush raise error");
                }

                Thread.Sleep(1);
            }
        }

        async Task push(ClientStreamManagement clientStreamManagement, BaseClientStream stream) {
            await Task.Run( () => {
                bool success = false;
                try {
                    success = stream.DispatchPushBytes();

                    if (success) {
                        stream.TriggerPushDispatched();
                    } else {
                        clientStreamManagement.Delete(stream);
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
