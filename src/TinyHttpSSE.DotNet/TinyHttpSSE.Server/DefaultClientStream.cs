using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TinyHttpSSE.Server
{
    public sealed class DefaultClientStream: BaseClientStream
    {
        private readonly ConcurrentQueue<byte[]> _importantQueue;
        private readonly ConcurrentQueue<byte[]> _highQueue;
        private readonly ConcurrentQueue<byte[]> _middleQueue;
        private readonly ConcurrentQueue<byte[]> _lowQueue;
        private readonly ConcurrentStack<byte[]> _nomatterStack;
        private readonly List<byte[]> _nomatterBatchList;

        const int LowQueueMaxCount = 50;

        const int HighQueueBatchCount = 6;
        const int MiddleQueueBatchCount = 3;
        const int LowQueueBatchCount = 1;
        const int AllBatchCount = HighQueueBatchCount + MiddleQueueBatchCount + LowQueueBatchCount;

        const int NomatterBatchCount = 2;

        public DefaultClientStream(HttpListenerContext httpContext) : base(httpContext) {

            _importantQueue = new ConcurrentQueue<byte[]>();
            _highQueue = new ConcurrentQueue<byte[]>();
            _middleQueue = new ConcurrentQueue<byte[]>();
            _lowQueue = new ConcurrentQueue<byte[]>();
            _nomatterStack = new ConcurrentStack<byte[]>();
            _nomatterBatchList = new List<byte[]>();
        }

        public override Task<bool> PushBytes(byte[] byteArr, EnumMessageLevel enumMessageLevel = EnumMessageLevel.Middle) {
            if (WriteRaiseError) {
                return Task.FromResult(false);
            }

            bool writeQueueSuccess = false;
            switch (enumMessageLevel) {
                case EnumMessageLevel.Important:
                    _importantQueue.Enqueue(byteArr);
                    writeQueueSuccess = true;
                    break;
                case EnumMessageLevel.High:
                    _highQueue.Enqueue(byteArr);
                    writeQueueSuccess = true;
                    break;
                case EnumMessageLevel.Middle:
                    _middleQueue.Enqueue(byteArr);
                    writeQueueSuccess = true;
                    break;
                case EnumMessageLevel.Low:
                    if (_lowQueue.Count < LowQueueMaxCount) {
                        _lowQueue.Enqueue(byteArr);
                        writeQueueSuccess = true;
                    }
                    writeQueueSuccess = false;
                    break;
                case EnumMessageLevel.Nomatter:
                    _nomatterStack.Push(byteArr);
                    writeQueueSuccess = true;
                    break;
            }

            return Task.FromResult(writeQueueSuccess);
        }

        public override bool CheckNeedDispatch() {
            return _importantQueue.Count>0 || _highQueue.Count>0 || _middleQueue.Count>0 ||_lowQueue.Count>0||_nomatterStack.Count>0;
        }

        public override async Task<bool> DispatchPushBytes() {
            if (WriteRaiseError) {
                return false;
            }
            if (!await pushImportantMsg()) {
                return false;
            }

            int curBatchCount = 0;

            int tmpBatchCount = HighQueueBatchCount;
            int tmpCount = 0;
            byte[] tmpBuff = null;
            while (tmpCount++ < tmpBatchCount && _highQueue.TryDequeue(out tmpBuff)) {
                if (!await InternalPushBytes(tmpBuff)) {
                    return false;
                }
                curBatchCount++;
            }

            tmpBatchCount = AllBatchCount - LowQueueBatchCount - tmpCount;
            tmpCount = 0;
            tmpBuff = null;
            while (tmpCount++ < tmpBatchCount && _middleQueue.TryDequeue(out tmpBuff)) {
                if (!await InternalPushBytes(tmpBuff)) {
                    return false;
                }
                curBatchCount++;
            }

            tmpBatchCount = AllBatchCount - curBatchCount;
            tmpCount = 0;
            tmpBuff = null;
            while (tmpCount++ < tmpBatchCount && _lowQueue.TryDequeue(out tmpBuff)) {
                if (!await InternalPushBytes(tmpBuff)) {
                    return false;
                }
                curBatchCount++;
            }

            if (curBatchCount > 0) {
                _nomatterStack.Clear();
            } else {
                tmpCount = 0;
                tmpBuff = null;
                _nomatterBatchList.Clear();
                while (tmpCount++ < NomatterBatchCount && _nomatterStack.TryPop(out tmpBuff)) {
                    _nomatterBatchList.Insert(0, tmpBuff);
                }
                _nomatterStack.Clear();
                foreach (var nomatterBuff in _nomatterBatchList) {
                    if (!await InternalPushBytes(nomatterBuff)) {
                        return false;
                    }
                }
            }

            return true;
        }

        private async Task<bool> pushImportantMsg() {
            while (_importantQueue.TryDequeue(out byte[] byteArr)) {
                bool writeResult = await InternalPushBytes(byteArr);
                if (!writeResult) {
                    return false;
                }
            }
            return true;
        }

        protected override void Dispose(bool disposing) {
            _importantQueue.Clear();
            _highQueue.Clear();
            _middleQueue.Clear();
            _lowQueue.Clear();
            _nomatterStack.Clear();
            base.Dispose(disposing);
        }
    }
}
