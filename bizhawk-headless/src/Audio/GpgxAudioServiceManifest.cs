using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    internal static class GpgxAudioServiceManifest
    {
        internal static CompleteRunAudioObserver Load(string path, string game, IGpgxAudioTraceApi api)
        {
            return LoadCore(path, game, api, false);
        }

        /// <summary>
        /// Candidate-only closed construction of the approved S2 action-7
        /// hook. Callers cannot select its token, PC, opcode, or topology.
        /// </summary>
        internal static CompleteRunAudioObserver LoadS2RequestCandidate(
            string path, IGpgxAudioTraceApi api)
        {
            return LoadCore(path, "s2", api, true);
        }

        /// <summary>Loads the fixed candidate from caller-verified immutable
        /// manifest bytes so identity and parsing cannot cross path opens.</summary>
        internal static CompleteRunAudioObserver LoadS2RequestCandidate(
            byte[] manifestBytes, IGpgxAudioTraceApi api)
        {
            if(manifestBytes==null)throw new ArgumentNullException(
                "manifestBytes");
            string json;
            try { json=new UTF8Encoding(false,true).GetString(manifestBytes); }
            catch(DecoderFallbackException error)
            { throw new InvalidDataException("Service manifest is not UTF-8.",error); }
            return LoadCore(JObject.Parse(json),"s2",api,true);
        }

        /// <summary>
        /// Closed construction of the approved Sonic 3&amp;K Sonic/Tails
        /// pre-consumption music-mailbox manifest. Callers cannot select the
        /// game, tokens, PCs, opcodes, or active-kind topology.
        /// </summary>
        internal static CompleteRunAudioObserver LoadS3kRequest(
            string path, IGpgxAudioTraceApi api)
        {
            return LoadCore(path, "s3k", api, false);
        }

        private static CompleteRunAudioObserver LoadCore(string path,
            string game, IGpgxAudioTraceApi api, bool addS2RequestCandidate)
        {
            if (!Path.IsPathRooted(path)) throw new ArgumentException("Manifest path must be absolute.", "path");
            return LoadCore(JObject.Parse(File.ReadAllText(path)),game,api,
                addS2RequestCandidate);
        }

        private static CompleteRunAudioObserver LoadCore(JObject root,
            string game, IGpgxAudioTraceApi api, bool addS2RequestCandidate)
        {
            if ((string)root["schema"] != "openggf.gpgx-audio-service-manifests.v1")
                throw new InvalidDataException("Unsupported service-manifest schema.");
            JObject value = root["games"]?[game] as JObject;
            if (value == null) throw new InvalidDataException("Missing game manifest: " + game);
            JArray rj = (JArray)value["ranges"];
            var ranges = new GpgxAudioObserverAdapter.SnapshotRange[rj.Count];
            var indices = new Dictionary<ushort, ushort>();
            for (int i = 0; i < rj.Count; i++) {
                ushort id = (ushort)(int)rj[i]["id"], start = (ushort)(int)rj[i]["start"];
                int end = (int)rj[i]["exclusive_end"];
                if (id == 0 || end <= start || end > 8192 || indices.ContainsKey(id)) throw new InvalidDataException("Invalid range.");
                indices.Add(id, (ushort)i); ranges[i] = new GpgxAudioObserverAdapter.SnapshotRange { RangeId=id, Start=start, Length=(ushort)(end-start) };
            }
            JArray kj = (JArray)value["kinds"];
            var kinds = new GpgxAudioObserverAdapter.ServiceKind[kj.Count]; uint snapshots=0;
            for (int i=0;i<kj.Count;i++) { ushort first,count; Slice((JArray)kj[i]["canonical_ranges"],indices,out first,out count); byte flags=KindFlags((JArray)kj[i]["flags"]); kinds[i]=new GpgxAudioObserverAdapter.ServiceKind { KindId=(byte)(int)kj[i]["id"], Flags=flags, CancellationRangeFirst=first, CancellationRangeCount=count, ContinuationFrameLimit=(byte)((flags&2)==0?0:4) }; snapshots+=Length(ranges,first,count); }
            JArray hj=(JArray)value["hooks"]; var hooks=new GpgxAudioObserverAdapter.ServiceHook[hj.Count]; var mask=new byte[8192]; var union=new HashSet<uint>();
            for(int i=0;i<hj.Count;i++){ JToken h=hj[i]; ushort first,count; Slice((JArray)h["ranges"],indices,out first,out count); byte cpu=Cpu((string)h["cpu"]); uint pc=(uint)h["pc"]; byte[] op=Hex((string)h["opcode"]); hooks[i]=new GpgxAudioObserverAdapter.ServiceHook { HookToken=(ushort)(int)h["token"], Action=Action((string)h["action"]), Cpu=cpu, Pc=pc, ServiceKindId=(byte)(int)h["kind"], ExpectedActiveKind=(byte)(int)h["expected_kind"], Flags=HookFlags((JArray)h["flags"]), OpcodeLength=(byte)op.Length, RangeFirst=first, RangeCount=count, Opcode=Pack(op) }; snapshots+=Length(ranges,first,count); if(cpu==1){ if(pc>=65536)throw new InvalidDataException("Z80 PC out of range."); mask[pc>>3]|=(byte)(1<<((int)pc&7)); union.Add(pc); } }
            var declared=new HashSet<uint>(); foreach(JToken pc in (JArray)value["z80_watch_pc_union"])declared.Add((uint)pc); if(!union.SetEquals(declared))throw new InvalidDataException("Watch mask is not the exact Z80 hook union.");
            if(addS2RequestCandidate)
            {
                for(int i=0;i<hooks.Length;i++)
                    if(hooks[i].HookToken==S2PreconsumptionRequestObserver.MarkerToken
                        ||hooks[i].HookToken==S2PreconsumptionRequestObserver.Kind3MarkerToken
                        ||(hooks[i].Cpu==S2PreconsumptionRequestObserver.MarkerSourceCpu
                            &&hooks[i].Pc==S2PreconsumptionRequestObserver.Pc)
                        ||hooks[i].Action==7)
                        throw new InvalidDataException("The authenticated S2 profile collides with the fixed request hook.");
                var rootMarker=new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken=S2PreconsumptionRequestObserver.MarkerToken,
                    Action=7,Cpu=S2PreconsumptionRequestObserver.MarkerSourceCpu,
                    Pc=S2PreconsumptionRequestObserver.Pc,
                    ServiceKindId=S2PreconsumptionRequestObserver.MarkerServiceKind,
                    ExpectedActiveKind=S2PreconsumptionRequestObserver.MarkerServiceKind,
                    Flags=0,OpcodeLength=4,RangeFirst=0,RangeCount=0,
                    Opcode=0x09108013UL,Reserved=0
                };
                var kind3Marker=new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken=S2PreconsumptionRequestObserver.Kind3MarkerToken,
                    Action=7,Cpu=S2PreconsumptionRequestObserver.MarkerSourceCpu,
                    Pc=S2PreconsumptionRequestObserver.Pc,
                    ServiceKindId=S2PreconsumptionRequestObserver.MarkerServiceKind,
                    ExpectedActiveKind=S2PreconsumptionRequestObserver.Kind3MarkerServiceKind,
                    Flags=0,OpcodeLength=4,RangeFirst=0,RangeCount=0,
                    Opcode=0x09108013UL,Reserved=0
                };
                // The core requires hooks ordered by CPU, then PC, then token,
                // so the pair is inserted at its ordered position rather than
                // appended past the authenticated M68K hooks.
                int insertion=0;
                while(insertion<hooks.Length
                    &&(hooks[insertion].Cpu<rootMarker.Cpu
                        ||(hooks[insertion].Cpu==rootMarker.Cpu
                            &&hooks[insertion].Pc<rootMarker.Pc)
                        ||(hooks[insertion].Cpu==rootMarker.Cpu
                            &&hooks[insertion].Pc==rootMarker.Pc
                            &&hooks[insertion].HookToken<rootMarker.HookToken)))
                    insertion++;
                var extended=new GpgxAudioObserverAdapter.ServiceHook[hooks.Length+2];
                Array.Copy(hooks,0,extended,0,insertion);
                extended[insertion]=rootMarker;
                extended[insertion+1]=kind3Marker;
                Array.Copy(hooks,insertion,extended,insertion+2,
                    hooks.Length-insertion);
                hooks=extended;
            }
            var config=new GpgxAudioObserverAdapter.Config { Magic=0x31544147,AbiVersion=1,StructSize=64,HookSize=32,RangeSize=16,EventSize=32,MaxDepth=8,MaxOpcodeBytes=8,ResetServiceKind=1,MaxContinuationFrames=4,WatchMaskBytes=8192,HookCount=(uint)hooks.Length,RangeCount=(uint)ranges.Length,SnapshotBytesTotal=snapshots,EventCapacity=65536,MaxServiceTokensPerFrame=65535,KindSize=16,KindCount=(ushort)kinds.Length };
            return new CompleteRunAudioObserver(api,config,mask,kinds,hooks,ranges);
        }
        private static void Slice(JArray ids,Dictionary<ushort,ushort> map,out ushort first,out ushort count){if(ids.Count==0){first=count=0;return;}if(!map.TryGetValue((ushort)(int)ids[0],out first))throw new InvalidDataException("Unknown range.");count=(ushort)ids.Count;for(int i=1;i<ids.Count;i++){ushort n;if(!map.TryGetValue((ushort)(int)ids[i],out n)||n!=first+i)throw new InvalidDataException("Noncontiguous range slice.");}}
        private static uint Length(GpgxAudioObserverAdapter.SnapshotRange[] r,ushort f,ushort c){uint n=0;for(int i=0;i<c;i++)n+=r[f+i].Length;return n;}
        private static byte Cpu(string v){if(v=="Z80")return 1;if(v=="M68K")return 2;throw new InvalidDataException("Unknown CPU.");}
        private static byte Action(string v){if(v=="PUSH_BEGIN")return 1;if(v=="POP_END_AT_PC")return 2;if(v=="POP_END_FALLTHROUGH")return 3;if(v=="TAIL_POP_PUSH")return 4;if(v=="OBSERVATION_MARKER")return 7;if(v=="SNAPSHOT_AT_PC")return 13;throw new InvalidDataException("Unknown action.");}
        private static byte KindFlags(JArray a){byte f=0;foreach(JToken x in a){string v=(string)x;if(v=="TYPED_ASYNC")f|=1;else if(v=="ALLOW_CONTINUATION")f|=2;else if(v=="ALLOW_CHILDREN")f|=4;else throw new InvalidDataException("Unknown kind flag.");}return f;}
        private static byte HookFlags(JArray a){byte f=0;foreach(JToken x in a){if((string)x!="ARM_Z80_PROOFS_ON_COMPLETION")throw new InvalidDataException("Unknown hook flag.");f|=1;}return f;}
        private static byte[] Hex(string s){if(string.IsNullOrEmpty(s)||(s.Length&1)!=0||s.Length>16)throw new InvalidDataException("Invalid opcode.");var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(s.Substring(i*2,2),16);return b;}
        private static ulong Pack(byte[] b){ulong v=0;for(int i=0;i<b.Length;i++)v|=(ulong)b[i]<<(8*i);return v;}
    }
}
