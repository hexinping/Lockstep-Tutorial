using System.Collections.Generic;
using System.IO;
using Lockstep.Logic;
using Lockstep.Serialization;
using Lockstep.Util;

namespace LockstepTutorial {
    public class RecordHelper {
        private const int RECODER_FILE_VERSION = 0;
        
        //序列化到本地文件，回放模式直接反序列化使用
        public static void Serialize(string recordFilePath, GameManager mgr){
            var writer = new Serializer();
            writer.Write(RECODER_FILE_VERSION);
            writer.Write(mgr.playerCount); //玩家总数
            writer.Write(mgr.localPlayerId); //当前玩家id
            writer.Write(mgr.playerServerInfos); //所有玩家信息

            var count = mgr.frames.Count; //总逻辑帧数
            writer.Write(count);
            for (int i = 0; i < count; i++) {
                mgr.frames[i].Serialize(writer); //每一帧所有的帧数据
            }

            var bytes = writer.CopyData();

            var relPath = PathUtil.GetUnityPath(recordFilePath);
            var dir = Path.GetDirectoryName(relPath);
            if (!Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(relPath, bytes);
        }

        public static void Deserialize(string recordFilePath, GameManager mgr){
#if !UNITY_EDITOR
        return;
#endif
            var relPath = PathUtil.GetUnityPath(recordFilePath);
            var bytes = File.ReadAllBytes(relPath);
            var reader = new Deserializer(bytes);
            var recoderFileVersion = reader.ReadInt32();
            mgr.playerCount = reader.ReadInt32();
            mgr.localPlayerId = reader.ReadInt32();
            mgr.playerServerInfos = reader.ReadArray(mgr.playerServerInfos);

            var count = reader.ReadInt32();
            mgr.frames = new List<FrameInput>();
            for (int i = 0; i < count; i++) {
                var frame = new FrameInput();
                frame.Deserialize(reader);
                frame.tick = i;
                mgr.frames.Add(frame);
            }
        }
    }
}