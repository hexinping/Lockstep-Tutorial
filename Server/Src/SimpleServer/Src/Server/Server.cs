using System;
using System.Collections.Generic;
using System.Net;
using Lockstep.Logging;
using Lockstep.Network;
using Lockstep.Util;

namespace Lockstep.FakeServer{
    
    //继承消息派发接口  by add
    public class Server : IMessageDispatcher {
        //network
        public static IPEndPoint serverIpPoint = NetworkUtil.ToIPEndPoint("127.0.0.1", 10083);
        private NetOuterProxy _netProxy = new NetOuterProxy();

        //update
        private const double UpdateInterval = 0.015; //frame rate = 30
        private DateTime _lastUpdateTimeStamp;
        private DateTime _startUpTimeStamp;
        private double _deltaTime;
        private double _timeSinceStartUp;

        //user mgr 
        private Room _room;
        private Dictionary<int, PlayerServerInfo> _id2Player = new Dictionary<int, PlayerServerInfo>();
        private Dictionary<int, Session> _id2Session = new Dictionary<int, Session>();
        private Dictionary<string, PlayerServerInfo> _name2Player = new Dictionary<string, PlayerServerInfo>();

        //id
        private static int _idCounter = 0;
        private int _curCount = 0;
        
        

        public void Start(){
            _netProxy.MessageDispatcher = this;
            //MessagePacker 网络消息初始化的设置  by add
            _netProxy.MessagePacker = MessagePacker.Instance;
            _netProxy.Awake(NetworkProtocol.TCP, serverIpPoint);
            _startUpTimeStamp = _lastUpdateTimeStamp = DateTime.Now;
        }
        
        //派发消息
        public void Dispatch(Session session, Packet packet){
            ushort opcode = packet.Opcode();
            var message = session.Network.MessagePacker.DeserializeFrom(opcode, packet.Bytes, Packet.Index,
                packet.Length - Packet.Index) as IMessage;
            //var msg = JsonUtil.ToJson(message);
            //Log.sLog("Server " + msg);
            var type = (EMsgType) opcode;
            switch (type) {
                case EMsgType.JoinRoom:
                    OnPlayerConnect(session, message);
                    break;
                case EMsgType.QuitRoom:
                    OnPlayerQuit(session, message);
                    break;
                case EMsgType.PlayerInput:
                    OnPlayerInput(session, message);
                    break;
                case EMsgType.HashCode:
                    //客户端每帧都会发一个哈希值 进行同步校验  by add
                    OnPlayerHashCode(session, message);
                    break;
            }
        }

        public void Update(){
            var now = DateTime.Now;
            _deltaTime = (now - _lastUpdateTimeStamp).TotalSeconds;
            //服务器每30毫秒派发一次，但是大部分都是15毫秒处理一次  by add
            if (_deltaTime > UpdateInterval) {
                _lastUpdateTimeStamp = now;
                //启动到当前的总时间  by add
                _timeSinceStartUp = (now - _startUpTimeStamp).TotalSeconds;
                DoUpdate();
            }
        }

        public void DoUpdate(){
            //check frame inputs
            var fDeltaTime = (float) _deltaTime;
            var fTimeSinceStartUp = (float) _timeSinceStartUp;
            
            //调用房间对象的DoUpdate方法  by add
            _room?.DoUpdate(fTimeSinceStartUp, fDeltaTime);
        }

        //玩家链接回调处理  by add
        void OnPlayerConnect(Session session, IMessage message){
            //TODO load from db
            
            var msg = message as Msg_JoinRoom;
            msg.name = msg.name + _idCounter;
            var name = msg.name;
            if (_name2Player.TryGetValue(name, out var val)) {
                return;
            }
            
            //构建玩家信息  by add
            var info = new PlayerServerInfo();
            info.Id = _idCounter++;
            info.name = name;
            _name2Player[name] = info;
            _id2Player[info.Id] = info;
            _id2Session[info.Id] = session;
            session.BindInfo = info;
            _curCount++;
            if (_curCount >= Room.MaxPlayerCount) {
                //当玩家达到最大房间数量，创建房间  by add
                _room = new Room();
                _room.Init(0);
                
                //把对应的玩家放入到房间里  by add
                foreach (var player in _id2Player.Values) {
                    _room.OnPlayerJoin(_id2Session[player.Id], player);
                }
                
                //游戏开始  by add
                OnGameStart(_room);
            }
            Debug.Log("OnPlayerConnect count:" + _curCount + " " + JsonUtil.ToJson(msg));
        }

        void OnPlayerQuit(Session session, IMessage message){
            Debug.Log("OnPlayerQuit count:" + _curCount);
            var player = session.GetBindInfo<PlayerServerInfo>();
            if (player == null)
                return;
            _id2Player.Remove(player.Id);
            _name2Player.Remove(player.name);
            _id2Session.Remove(player.Id);
            _curCount--;
            if (_curCount == 0) { 
                _room = null;
            }
        }

        void OnPlayerInput(Session session, IMessage message){
            var msg = message as Msg_PlayerInput;
            var player = session.GetBindInfo<PlayerServerInfo>();
            _room?.OnPlayerInput(player.Id, msg);
        }
        void OnPlayerHashCode(Session session, IMessage message){
            var msg = message as Msg_HashCode;
            var player = session.GetBindInfo<PlayerServerInfo>();
            _room?.OnPlayerHashCode(player.Id, msg);
        }

        void OnGameStart(Room room){
            if (room.IsRunning) {
                return;
            }

            room.OnGameStart();
        }
    }
}