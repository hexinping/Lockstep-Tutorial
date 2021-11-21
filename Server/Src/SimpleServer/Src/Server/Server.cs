using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Lockstep.Game;
using Lockstep.Logging;
using Lockstep.Network;
using Lockstep.Util;
using NetMsg.Common;

namespace Lockstep.FakeServer {
    //继承消息派发接口  
    public class Server : IMessageDispatcher {
        //network
        public static IPEndPoint serverIpPoint = NetworkUtil.ToIPEndPoint("127.0.0.1", 10083);
        private NetOuterProxy _netProxy = new NetOuterProxy(); //网络代理

        //update
        private const double UpdateInterval = NetworkDefine.UPDATE_DELTATIME /1000.0f; //frame rate = 30
        private DateTime _lastUpdateTimeStamp;
        private DateTime _startUpTimeStamp;
        private double _deltaTime;
        private double _timeSinceStartUp;

        //user mgr 
        private Game _game;
        private Dictionary<long, Player> _id2Player = new Dictionary<long, Player>();

        //id
        private static int _idCounter = 0;
        private int _curCount = 0;


        public void Start(){
            _netProxy.MessageDispatcher = this;
            //MessagePacker 网络消息初始化的设置
            _netProxy.MessagePacker = MessagePacker.Instance;
            _netProxy.Awake(NetworkProtocol.TCP, serverIpPoint);
            _startUpTimeStamp = _lastUpdateTimeStamp = DateTime.Now;
        }
        //派发消息
        public void Dispatch(Session session, Packet packet){
            ushort opcode = packet.Opcode();
            if (opcode == 39) { 
                int i = 0;
            }

            var message = session.Network.MessagePacker.DeserializeFrom(opcode, packet.Bytes, Packet.Index,
                packet.Length - Packet.Index);
            OnNetMsg(session, opcode, message as BaseMsg);
        }

        void OnNetMsg(Session session, ushort opcode, BaseMsg msg){
            var type = (EMsgSC) opcode;
            switch (type) {
                //login
                // case EMsgSC.L2C_JoinRoomResult: 
                case EMsgSC.C2L_JoinRoom:
                    //加入房间
                    OnPlayerConnect(session, msg);
                    return;
                case EMsgSC.C2L_LeaveRoom:
                    //离开房间
                    OnPlayerQuit(session, msg);
                    return;
                //room
            }
            var player = session.GetBindInfo<Player>();
            _game?.OnNetMsg(player, opcode, msg);
        }

        public void Update(){
            var now = DateTime.Now;
            _deltaTime = (now - _lastUpdateTimeStamp).TotalSeconds;
            //服务器每30毫秒派发一次，但是大部分都是66毫秒（15帧）处理一次  by add
            if (_deltaTime > UpdateInterval) {
                _lastUpdateTimeStamp = now;
                _timeSinceStartUp = (now - _startUpTimeStamp).TotalSeconds;
                DoUpdate();
            }
        }

        public void DoUpdate(){
            //check frame inputs
            var fDeltaTime = (float) _deltaTime;
            var fTimeSinceStartUp = (float) _timeSinceStartUp;
            _game?.DoUpdate(fDeltaTime);
        }


        void OnPlayerConnect(Session session, BaseMsg message){
            //TODO load from db
            //构建玩家信息
            var info = new Player();
            info.UserId = _idCounter++;
            info.PeerTcp = session;
            info.PeerUdp = session;
            _id2Player[info.UserId] = info;
            session.BindInfo = info;
            _curCount++;
            
            if (_curCount >= Game.MaxPlayerCount) {
                //TODO temp code
                //当玩家达到最大房间数量，创建房间
                _game = new Game();
                var players = new Player[_curCount];
                int i = 0;
                //把对应的玩家放入到房间里
                foreach (var player in _id2Player.Values) {
                    player.LocalId = (byte) i;
                    player.Game = _game;
                    players[i] = player;
                    i++;
                }
                //游戏开始
                _game.DoStart(0, 0, 0, players, "123");
            }

            Debug.Log("OnPlayerConnect count:" + _curCount + " ");
        }

        void OnPlayerQuit(Session session, BaseMsg message){
            var player = session.GetBindInfo<Player>();
            if (player == null)
                return;
            _curCount--;
            Debug.Log("OnPlayerQuit count:" + _curCount);
            _id2Player.Remove(player.UserId);
            if (_curCount == 0) {
                _game = null;
            }
        }
    }
}