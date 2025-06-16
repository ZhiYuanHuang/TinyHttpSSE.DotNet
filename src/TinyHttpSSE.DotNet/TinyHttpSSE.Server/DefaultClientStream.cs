using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TinyHttpSSE.Server
{
    public sealed class DefaultClientStream: BaseClientStream
    {
        private readonly ConcurrentQueue<byte[]> _importantQueue;
        private readonly ConcurrentQueue<byte[]> _highQueue;
        private readonly ConcurrentQueue<byte[]> _middleQueue;
        private readonly ConcurrentQueue<byte[]> _lowQueue;
        private readonly ConcurrentQueue<byte[]> _nomatterQueue;

        const int ConstLowQueueMaxCount = 300;
        public int LowQueueMaxCount = ConstLowQueueMaxCount;

        const int HighQueueBatchCount = 6;
        const int MiddleQueueBatchCount = 3;
        const int LowQueueBatchCount = 1;
        const int AllBatchCount = HighQueueBatchCount + MiddleQueueBatchCount + LowQueueBatchCount;

        const int NomatterMaxCount = 2;

        const int MaxDispatchCount = 1000;

        MemoryStream _memoryStream = null;
        readonly bool _enableChunkCompress=false;

        public DefaultClientStream(HttpListenerContext httpContext) : base(httpContext) {

            _importantQueue = new ConcurrentQueue<byte[]>();
            _highQueue = new ConcurrentQueue<byte[]>();
            _middleQueue = new ConcurrentQueue<byte[]>();
            _lowQueue = new ConcurrentQueue<byte[]>();
            _nomatterQueue = new ConcurrentQueue<byte[]>();

            if (httpContext.Request.Headers.AllKeys.Contains("EnableChunkCompress")) {
                bool.TryParse(httpContext.Request.Headers["EnableChunkCompress"], out _enableChunkCompress);
            }

            _memoryStream = new MemoryStream();
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
                    int lowQueueMaxCount = LowQueueMaxCount > 0 ? LowQueueMaxCount : ConstLowQueueMaxCount;
                    if (_lowQueue.Count >= lowQueueMaxCount) {
                        _lowQueue.Clear();
                    }
                    _lowQueue.Enqueue(byteArr);
                    writeQueueSuccess = true;
                    break;
                case EnumMessageLevel.Nomatter:
                    _nomatterQueue.Enqueue(byteArr);
                    writeQueueSuccess = true;
                    if (_nomatterQueue.Count > NomatterMaxCount) {
                        _nomatterQueue.TryDequeue(out _);
                    }
                    break;
            }

            return Task.FromResult(writeQueueSuccess);
        }

        public override bool CheckNeedDispatch() {
            return _importantQueue.Count>0 || _highQueue.Count>0 || _middleQueue.Count>0 ||_lowQueue.Count>0 || _nomatterQueue.Count>0;
        }

        public override bool DispatchPushBytes() {
            if (WriteRaiseError) {
                return false;
            }

            int curBatchSentCount = 0;

            //important msg must be push fastest
            if (!batchPushBytes(_importantQueue,ref curBatchSentCount)) {
                return false;
            }

            int dispatchSumCount = 0;
            
            do {
                curBatchSentCount = 0;

                if(!batchPushBytes(_highQueue,ref curBatchSentCount, HighQueueBatchCount)) {
                    return false;
                }
                if(!batchPushBytes(_middleQueue,ref curBatchSentCount, AllBatchCount - LowQueueBatchCount - curBatchSentCount)) {
                    return false;
                }
                if(!batchPushBytes(_lowQueue,ref curBatchSentCount, AllBatchCount - curBatchSentCount)) {
                    return false;
                }

                dispatchSumCount += curBatchSentCount;
                if (curBatchSentCount == 0) {
                    break;
                }

            } while (dispatchSumCount < MaxDispatchCount);

            if (dispatchSumCount > 0) {
                _nomatterQueue.Clear();
            } else {
                if (!batchPushBytes(_nomatterQueue, ref curBatchSentCount, NomatterMaxCount)) {
                    return false;
                }
            }

            return true;
        }

        private bool batchPushBytes(ConcurrentQueue<byte[]> bufferQueue,ref int allSentCount, int maxCount=-1) {

            int curCount = 0;
            byte[] tmpBuff = null;
            byte[] pushBuff = null;

            while (bufferQueue.TryDequeue(out tmpBuff)) {
                pushBuff = tmpBuff;
                if (_enableChunkCompress) {
                    var tuple = compressAsync(tmpBuff).GetAwaiter().GetResult();
                    if (tuple.Item1) {
                        pushBuff= tuple.Item2;
                    } else {
                        continue;
                    }
                }

                if (!InternalPushBytes(pushBuff).GetAwaiter().GetResult()) {
                    return false;
                }

                allSentCount++;
                curCount++;
                if (maxCount >= 0) {
                    if (curCount >= maxCount) {
                        break;
                    }
                }
            }

            return true;
        }

        private async Task<Tuple<bool, byte[]>> compressAsync(byte[] byteArr) {
            if (_memoryStream.Position != 0) {
                _memoryStream.SetLength(0);
                _memoryStream.Position = 0;
            }

            try {
                using (var gzipStream = new GZipStream(_memoryStream, CompressionMode.Compress, true)) {
                    gzipStream.Write(byteArr, 0, byteArr.Length);
                }
                return Tuple.Create(true, _memoryStream.ToArray());
            } catch {
                return Tuple.Create<bool, byte[]>(false, null);
            }
        }

        protected override void Dispose(bool disposing) {
            _importantQueue.Clear();
            _highQueue.Clear();
            _middleQueue.Clear();
            _lowQueue.Clear();
            _nomatterQueue.Clear();
            base.Dispose(disposing);
        }
    }
}
