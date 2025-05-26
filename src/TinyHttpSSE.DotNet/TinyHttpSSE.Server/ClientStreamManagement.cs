using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TinyHttpSSE.Server
{
    public class ClientStreamManagement
    {
        internal ClientStreamSet InternalAll;
        public readonly ClientStreamSet All;
        public readonly ClientStreamSet Others;
        public readonly ConcurrentDictionary<string, ClientStreamSet> Groups;
        public ClientStreamManagement() {
            InternalAll = new ClientStreamSet();
            All = new ClientStreamSet();
            Others = new ClientStreamSet();
            Groups = new ConcurrentDictionary<string, ClientStreamSet>();
        }

        public void PutInGroup(string groupName, BaseClientStream stream) {
            stream.GroupName = groupName;
            var groupDict = Groups.GetOrAdd(groupName, o => new ClientStreamSet());
            groupDict.Put(stream);
        }

        internal void Delete(BaseClientStream clientStream) {
            InternalAll.Delete(clientStream);
            Others.Delete(clientStream);
            All.Delete(clientStream);
            if (!string.IsNullOrEmpty(clientStream.GroupName) && Groups.TryGetValue(clientStream.GroupName, out ClientStreamSet streamSet)) {
                streamSet.Delete(clientStream);
            }
        }
    }

    public class ClientStreamSet
    {
        ConcurrentDictionary<string, BaseClientStream> _streamSet;
        public ClientStreamSet() {
            _streamSet = new ConcurrentDictionary<string, BaseClientStream>();
        }

        public ICollection<string> Keys => _streamSet.Keys;

        public void Put(BaseClientStream stream) {
            _streamSet.TryAdd(stream.SessionId, stream);
        }

        public void Delete(BaseClientStream stream) {
            _streamSet.TryRemove(stream.SessionId, out _);
        }

        public void PushSseMsg(string dataContent) {
            foreach (var stream in _streamSet) {
                stream.Value.PushSseMsg(dataContent).ConfigureAwait(false);
            }
        }

        public void PushBytes(byte[] byteArr, EnumMessageLevel enumMessageLevel = EnumMessageLevel.Middle) {
            foreach (var stream in _streamSet) {
                stream.Value.PushBytes(byteArr, enumMessageLevel).ConfigureAwait(false);
            }
        }

        public void EndOfStream() {
            foreach (var stream in _streamSet) {
                stream.Value.EndOfStream().ConfigureAwait(false);
            }
            _streamSet.Clear();
        }

        public bool TryGetValue(string sessionId, out BaseClientStream clientStream) {
            return _streamSet.TryGetValue(sessionId, out clientStream);
        }

        public IEnumerator<KeyValuePair<string, BaseClientStream>> GetEnumerator() => _streamSet.GetEnumerator();

    }
}
