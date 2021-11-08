using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Lockstep.Collision2D;
using Lockstep.Logic;
using Lockstep.PathFinding;
using Lockstep.Game;
using Lockstep.Math;
using Lockstep.Serialization;
using Lockstep.Util;
using UnityEngine;
using Debug = Lockstep.Logging.Debug;
using Profiler = Lockstep.Util.Profiler;

namespace LockstepTutorial {
    public class GameManager : UnityBaseManager {
        public static GameManager Instance { get; private set; }
        
        //当前玩家的输入数据，可能为空帧也要上传
        public static PlayerInput CurGameInput = new PlayerInput();  
        
        //客户端模式 纯单机版  by add
        [Header("ClientMode")] public bool IsClientMode;
        public PlayerServerInfo ClientModeInfo = new PlayerServerInfo();

        [Header("Recorder")] public bool IsReplay = false;
        public string recordFilePath;

        private static int _maxServerFrameIdx;
        [Header("FrameData")] public int mapId;
        private bool _hasStart = false;
        [HideInInspector] public int predictTickCount = 3;
        [HideInInspector] public int inputTick;
        [HideInInspector] public int localPlayerId = 0;
        [HideInInspector] public int playerCount = 1;
        [HideInInspector] public int curMapId = 0;
        public int curFrameIdx = 0;
        [HideInInspector] public FrameInput curFrameInput;
        [HideInInspector] public PlayerServerInfo[] playerServerInfos;
        
        //所有帧操作数据
        [HideInInspector] public List<FrameInput> frames = new List<FrameInput>();

        [Header("Ping")] public static int PingVal;
        public static List<float> Delays = new List<float>();
        public Dictionary<int, float> tick2SendTimer = new Dictionary<int, float>();

        [Header("GameData")] public static List<Player> allPlayers = new List<Player>();
        public static Player MyPlayer;
        public static Transform MyPlayerTrans;
        [HideInInspector] public float remainTime; // remain time to update
        private NetClient netClient;
        private List<UnityBaseManager> _mgrs = new List<UnityBaseManager>();

        //日志存储日志路径
        private static string _traceLogPath {
            get {
#if UNITY_STANDALONE_OSX
                return $"/tmp/LPDemo/Dump_{Instance.localPlayerId}.txt";
#else
                return $"c:/tmp/LPDemo/Dump_{Instance.localPlayerId}.txt";
#endif
            }
        }


        public void RegisterManagers(UnityBaseManager mgr){
            _mgrs.Add(mgr);
        }

        private void Awake(){
            Screen.SetResolution(1024, 768, false);
            
            //网络通信ping值计算 by add
            gameObject.AddComponent<PingMono>();
            
            //输入脚本 by add
            gameObject.AddComponent<InputMono>();

            _Awake();
        }

        private void Start(){
            _Start();
        }

        private void Update(){
            _DoUpdate();
        }

        private void _Awake(){
#if !UNITY_EDITOR
            IsReplay = false;
#endif
            //管理所有的mgr
            DoAwake();
            foreach (var mgr in _mgrs) {
                //分别调用每个mgr的DoAwake方法
                mgr.DoAwake();
            }
        }


        private void _Start(){
            //判断回放模式和纯客户端模式  by add
            DoStart();
            
            //调用每个mgr的DoStart方法 by add
            foreach (var mgr in _mgrs) {
                mgr.DoStart();
            }

            Debug.Trace("Before StartGame _IdCounter " + BaseEntity.IdCounter, true);
            if (!IsReplay && !IsClientMode) {
                //正常模式 by add
                netClient = new NetClient();
                netClient.Start();
                netClient.Send(new Msg_JoinRoom() {name = Application.dataPath});
            }
            else {
                //回放模式和客户端模式都走这
                //playerServerInfos 会在DoStart里从文件里反序列化生成帧数据
                StartGame(0, playerServerInfos, localPlayerId);
            }
        }


        private void _DoUpdate(){
            if (!_hasStart) return;
            remainTime += Time.deltaTime;
            //每30毫秒刷新一次，其实可以不用，
            while (remainTime >= 0.03f) 
            {
                remainTime -= 0.03f;
                //send input
                if (!IsReplay) {
                    //非回放模式，发送客户端的操作帧数据，
                    SendInput();
                }

                //检测当前帧是否有效
                if (GetFrame(curFrameIdx) == null) {
                    return;
                }
                //更新当前帧所有玩家的输入
                //利用哈希校验每一帧每个客户端是否出现异常，检测不同步的现象
                //记录每一帧数据到本地作为日志文件 debug使用
                Step();
            }
        }

        public static void StartGame(Msg_StartGame msg){
            UnityEngine.Debug.Log("StartGame");
            Instance.StartGame(msg.mapId, msg.playerInfos, msg.localPlayerId);
        }

        public void StartGame(int mapId, PlayerServerInfo[] playerInfos, int localPlayerId){
            _hasStart = true;
            curMapId = mapId;

            this.playerCount = playerInfos.Length;
            this.playerServerInfos = playerInfos;
            this.localPlayerId = localPlayerId;
            //帧记录存储文件路径
            Debug.TraceSavePath = _traceLogPath;
            allPlayers.Clear();
            for (int i = 0; i < playerCount; i++) {
                allPlayers.Add(new Player() {localId = i});
            }

            //create Players 
            for (int i = 0; i < playerCount; i++) {
                var playerInfo = playerInfos[i];
                var go = HeroManager.InstantiateEntity(allPlayers[i], playerInfo.PrefabId, playerInfo.initPos);
                //init mover
                if (allPlayers[i].localId == localPlayerId) {
                    //初始化本地客户端的Transform
                    MyPlayerTrans = go.transform;
                }
            }
            
            //当前玩家
            MyPlayer = allPlayers[localPlayerId];
        }

        
        //30毫秒发送一次
        public void SendInput(){
            if (IsClientMode) {
                //客户端模式直接存储帧数据
                PushFrameInput(new FrameInput() {
                    tick = curFrameIdx,
                    inputs = new PlayerInput[] {CurGameInput} //InputMono脚本里会每帧都会赋值
                });
                return;
            }
        
            predictTickCount = 2; //Mathf.Clamp(Mathf.CeilToInt(pingVal / 30), 1, 20);
            if (inputTick > predictTickCount + _maxServerFrameIdx) {
                return;
            }

            var playerInput = CurGameInput;
            //每一帧操作数据发送给服务器
            netClient?.Send(new Msg_PlayerInput() {
                input = playerInput,
                tick = inputTick
            });
            //UnityEngine.Debug.Log("" + playerInput.inputUV);
            tick2SendTimer[inputTick] = Time.realtimeSinceStartup;
            //UnityEngine.Debug.Log("SendInput " + inputTick);
            inputTick++;
        }


        private void Step(){
            //更新当前帧所有玩家的输入
            UpdateFrameInput();
            if (IsReplay) {
                //回放模式
                if (curFrameIdx < frames.Count) {
                    Replay(curFrameIdx);
                    curFrameIdx++;
                }
            }
            else {
                // TODO??
                Recoder();
                //send hash 利用哈希校验每一帧每个客户端是否出现异常，检测不同步的现象
                netClient?.Send(new Msg_HashCode() {
                    tick = curFrameIdx,
                    hash = GetHash()
                });
                
                //每一帧记录数据
                TraceHelper.TraceFrameState();
                curFrameIdx++;
            }
        }

        private void Recoder(){
            _Update();
        }


        private void Replay(int frameIdx){
            _Update();
        }

        private void _Update(){
            var deltaTime = new LFloat(true, 30);
            DoUpdate(deltaTime);
            foreach (var mgr in _mgrs) {
                //英雄 敌人mgr DoUpdate
                mgr.DoUpdate(deltaTime);
            }
        }


        private void OnDestroy(){
            //关闭客户端，发送离开房间消息
            netClient?.Send(new Msg_QuitRoom());
            foreach (var mgr in _mgrs) {
                mgr.DoDestroy();
            }

            if (!IsReplay) {
                //正常模式下把帧数据记录到本地文件，回放模式里使用
                RecordHelper.Serialize(recordFilePath, this);
            }

            Debug.FlushTrace();
            DoDestroy();
        }

        public override void DoAwake(){
            Instance = this;
            
            //记录一些管理器对象 by add
            var mgrs = GetComponents<UnityBaseManager>();
            foreach (var mgr in mgrs) {
                if (mgr != this) {
                    RegisterManagers(mgr);
                }
            }
        }


        public override void DoStart(){
            if (IsReplay) {
                //回放模式，反序列化文件
                RecordHelper.Deserialize(recordFilePath, this);
            }

            if (IsClientMode) {
                //纯客户端模式
                playerCount = 1;
                localPlayerId = 0;
                playerServerInfos = new PlayerServerInfo[] {ClientModeInfo};
                frames = new List<FrameInput>();
            }
        }


        public override void DoUpdate(LFloat deltaTime){ }

        public override void DoDestroy(){
            //DumpPathFindReqs();
        }


        public static void PushFrameInput(FrameInput input){
            var frames = Instance.frames;
            for (int i = frames.Count; i <= input.tick; i++) {
                frames.Add(new FrameInput());
            }

            if (frames.Count == 0) {
                Instance.remainTime = 0;
            }

            _maxServerFrameIdx = Math.Max(_maxServerFrameIdx, input.tick);
            if (Instance.tick2SendTimer.TryGetValue(input.tick, out var val)) {
                Delays.Add(Time.realtimeSinceStartup - val);
            }
            //存储帧消息数据
            frames[input.tick] = input;
        }


        public FrameInput GetFrame(int tick){
            if (frames.Count > tick) {
                var frame = frames[tick];
                if (frame != null && frame.tick == tick) {
                    return frame;
                }
            }

            return null;
        }

        private void UpdateFrameInput(){
            curFrameInput = GetFrame(curFrameIdx);
            var frame = curFrameInput;
            for (int i = 0; i < playerCount; i++) {
                //所有玩家的帧输入数据
                allPlayers[i].InputAgent = frame.inputs[i];
            }
        }


        //{string.Format("{0:yyyyMMddHHmmss}", DateTime.Now)}_
        public int GetHash(){
            int hash = 1;
            int idx = 0;
            foreach (var entity in allPlayers) {
                hash += entity.currentHealth.GetHash() * PrimerLUT.GetPrimer(idx++);
                hash += entity.transform.GetHash() * PrimerLUT.GetPrimer(idx++);
            }

            foreach (var entity in EnemyManager.Instance.allEnemy) {
                hash += entity.currentHealth.GetHash() * PrimerLUT.GetPrimer(idx++);
                hash += entity.transform.GetHash() * PrimerLUT.GetPrimer(idx++);
            }

            return hash;
        }
    }
}