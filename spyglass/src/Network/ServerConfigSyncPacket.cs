using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace spyglass.src.Network
{
    [ProtoContract]
    public class ServerConfigSyncPacket
    {
        // Preserve the original implicit alphabetical field numbers.
        [ProtoMember(3)]
        public VignetteStyle vignetteStyle;
        [ProtoMember(2)]
        public int edgeSize;
        [ProtoMember(1)]
        public float edgeOpacity;
        [ProtoMember(4)]
        public float lensSize = 0.5f;
    }
}
