#define DEBUG_FRAME_DELAY
using System;
using System.Collections.Generic;
using System.Linq;
using Lockstep.Math;
using Lockstep.Serialization;
using Lockstep.Util;
using NetMsg.Common;
using Debug = Lockstep.Logging.Debug;

namespace Lockstep.Game {
    public interface IFrameBuffer {
        void PushLocalFrame(ServerFrame frame);
        void PushServerFrames(ServerFrame[] frames, bool isNeedDebugCheck = true);
        void PushMissServerFrames(ServerFrame[] frames, bool isNeedDebugCheck = true);
        ServerFrame GetFrame(int tick);
        ServerFrame GetServerFrame(int tick);
        ServerFrame GetLocalFrame(int tick);
        void SetClientTick(int tick);
        void SendInput(Msg_PlayerInput input);

        void DoUpdate(float deltaTime,int worldTick);
        int NextTickToCheck { get; }
        int MaxServerTickInBuffer { get; }
        bool IsNeedRollback { get; }
        int MaxContinueServerTick { get; }
        int CurTickInServer { get; }
        int PingVal { get; }
    }

    public class FrameBuffer : IFrameBuffer {
        /// for debug
        public static byte __debugMainActorID;

        //buffers
        private int _maxClientPredictFrameCount;
        private int _bufferSize;
        private int _spaceRollbackNeed;
        private int _maxServerOverFrameCount;

        private ServerFrame[] _serverBuffer;
        private ServerFrame[] _clientBuffer;

        //ping 
        public int PingVal { get; private set; }
        private float _pingTimer;
        private List<long> _delays = new List<long>();
        Dictionary<int, long> _tick2SendTimestamp = new Dictionary<int, long>();

        /// the tick client need run in next update
        private int _nextClientTick;

        public int CurTickInServer { get; private set; }
        public int NextTickToCheck { get; private set; }
        public int MaxServerTickInBuffer { get; private set; } = -1;
        public bool IsNeedRollback { get; private set; }
        public int MaxContinueServerTick { get; private set; }


        public INetworkService _networkService;

        public FrameBuffer(INetworkService networkService, int bufferSize, int snapshotFrameInterval,
            int maxClientPredictFrameCount){
            this._bufferSize = bufferSize;
            this._networkService = networkService;
            this._maxClientPredictFrameCount = maxClientPredictFrameCount;
            _spaceRollbackNeed = snapshotFrameInterval * 2;
            _maxServerOverFrameCount = bufferSize - _spaceRollbackNeed;
            _serverBuffer = new ServerFrame[bufferSize];
            _clientBuffer = new ServerFrame[bufferSize];
        }

        public void SetClientTick(int tick){
            _nextClientTick = tick + 1;
        }

        public void PushLocalFrame(ServerFrame frame){
            var sIdx = frame.tick % _bufferSize;
            Debug.Assert(_clientBuffer[sIdx] == null || _clientBuffer[sIdx].tick <= frame.tick,
                "Push local frame error!");
            _clientBuffer[sIdx] = frame;
        }

        public void PushMissServerFrames(ServerFrame[] frames, bool isNeedDebugCheck = true){
            PushServerFrames(frames, isNeedDebugCheck);
            _networkService.SendMissFrameRepAck(MaxContinueServerTick + 1);
        }

        public void PushServerFrames(ServerFrame[] frames, bool isNeedDebugCheck = true){
            var count = frames.Length;
            for (int i = 0; i < count; i++) {
                var data = frames[i];
                //Debug.Log("PushServerFrames" + data.tick);
                if (_tick2SendTimestamp.TryGetValue(data.tick, out var sendTick)) {
                    var delay = LTime.realtimeSinceStartupMS - sendTick;
                    _delays.Add(delay);
                    _tick2SendTimestamp.Remove(data.tick);
                }

                if (data.tick < NextTickToCheck) {
                    //the frame is already checked
                    return;
                }

                if (data.tick > CurTickInServer) {
                    CurTickInServer = data.tick;
                }

                if (data.tick >= NextTickToCheck + _maxServerOverFrameCount - 1) {
                    //to avoid ringBuffer override the frame that have not been checked
                    return;
                }

                //Debug.Log("PushServerFramesSucc" + data.tick);
                if (data.tick > MaxServerTickInBuffer) {
                    MaxServerTickInBuffer = data.tick;
                }

                var targetIdx = data.tick % _bufferSize;
                if (_serverBuffer[targetIdx] == null || _serverBuffer[targetIdx].tick != data.tick) {
                    _serverBuffer[targetIdx] = data;
                }
            }
        }

        public void DoUpdate(float deltaTime,int worldTick){
            UpdatePingVal(deltaTime);

            //Debug.Assert(nextTickToCheck <= nextClientTick, "localServerTick <= localClientTick ");
            //Confirm frames
            //是否需要回滚
            IsNeedRollback = false;
            while (NextTickToCheck <= MaxServerTickInBuffer && NextTickToCheck<worldTick) {
                var sIdx = NextTickToCheck % _bufferSize;
                var cFrame = _clientBuffer[sIdx];
                var sFrame = _serverBuffer[sIdx];
                if (cFrame == null || cFrame.tick != NextTickToCheck || sFrame == null ||
                    sFrame.tick != NextTickToCheck)
                    break;
                //Check client guess input match the real input
                if (object.ReferenceEquals(sFrame, cFrame) || sFrame.Equals(cFrame)) {
                    NextTickToCheck++;
                }
                else {
                    IsNeedRollback = true;
                    break;
                }
            }
            
            // 丢包或者断线重连的情况下 向服务器重新请求数据，虽然使用的是TCP 但接口的实现都是用UDP的思想来的 ==》 推荐使用KCP
            //Request miss frame data
            int tick = NextTickToCheck;
            for (; tick <= MaxServerTickInBuffer; tick++) {
                var idx = tick % _bufferSize;
                if (_serverBuffer[idx] == null || _serverBuffer[idx].tick != tick) {
                    break;
                }
            }

            MaxContinueServerTick = tick - 1;
            if(MaxContinueServerTick <= 0) return;
            if (MaxContinueServerTick < CurTickInServer // has some middle frame pack was lost
                || _nextClientTick >
                MaxContinueServerTick + (_maxClientPredictFrameCount - 3) //client has predict too much
            ) {
                Debug.Log("SendMissFrameReq " + MaxContinueServerTick);
                _networkService.SendMissFrameReq(MaxContinueServerTick);
            }
        }

        private void UpdatePingVal(float deltaTime){
            _pingTimer += deltaTime;
            if (_pingTimer > 0.5f) {
                _pingTimer = 0;
                PingVal = (int) (_delays.Sum() / LMath.Max(_delays.Count, 1));
                _delays.Clear();
            }
        }

        public void SendInput(Msg_PlayerInput input){
            _tick2SendTimestamp[input.Tick] = LTime.realtimeSinceStartupMS;
#if DEBUG_SHOW_INPUT
            var cmd = input.Commands[0];
            var playerInput = new Deserializer(cmd.content).Parse<Lockstep.Game. PlayerInput>();
            if (playerInput.inputUV != LVector2.zero) {
                Debug.Log($"SendInput tick:{input.Tick} uv:{playerInput.inputUV}");
            }
#endif
            _networkService.SendInput(input);
        }

        public ServerFrame GetFrame(int tick){
            var sFrame = GetServerFrame(tick);
            if (sFrame != null) {
                return sFrame;
            }

            return GetLocalFrame(tick);
        }

        public ServerFrame GetServerFrame(int tick){
            if (tick > MaxServerTickInBuffer) {
                return null;
            }

            return _GetFrame(_serverBuffer, tick);
        }

        public ServerFrame GetLocalFrame(int tick){
            if (tick >= _nextClientTick) {
                return null;
            }

            return _GetFrame(_clientBuffer, tick);
        }

        private ServerFrame _GetFrame(ServerFrame[] buffer, int tick){
            var idx = tick % _bufferSize;
            var frame = buffer[idx];
            if (frame == null) return null;
            if (frame.tick != tick) return null;
            return frame;
        }
    }
}