using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Lockstep.Math;
using Lockstep.Util;
using Lockstep.Game;
using Lockstep.Network;
using NetMsg.Common;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Lockstep.Game {
    [Serializable]
    public class Launcher : ILifeCycle {

        public int CurTick => _serviceContainer.GetService<ICommonStateService>().Tick;

        public static Launcher Instance { get; private set; }

        private ServiceContainer _serviceContainer;
        private ManagerContainer _mgrContainer;
        private TimeMachineContainer _timeMachineContainer;
        private IEventRegisterService _registerService;

        public string RecordPath;
        public int MaxRunTick = int.MaxValue;
        public Msg_G2C_GameStartInfo GameStartInfo;
        public Msg_RepMissFrame FramesInfo;

        public int JumpToTick = 10;

        private SimulatorService _simulatorService = new SimulatorService();
        private NetworkService _networkService = new NetworkService();


        private IConstStateService _constStateService;
        public bool IsRunVideo => _constStateService.IsRunVideo;
        public bool IsVideoMode => _constStateService.IsVideoMode;
        public bool IsClientMode => _constStateService.IsClientMode;

        public object transform;
        private OneThreadSynchronizationContext _syncContext; 
        public void DoAwake(IServiceContainer services){
            
            //设置同步的上下文 网络使用的是aynsc/await编程范式
            //把子线程的回调指定到主线程，在Update里驱动_syncContext.Update
            _syncContext = new OneThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_syncContext);
            Utils.StartServices();
            if (Instance != null) {
                Debug.LogError("LifeCycle Error: Awake more than once!!");
                return;
            }

            Instance = this;
            //service的管理器，管理所有service
            _serviceContainer = services as ServiceContainer; 
            //事件注册的service
            _registerService = new EventRegisterService();
            
            //baseService的管理器，只管理baseService类
            _mgrContainer = new ManagerContainer();
            _timeMachineContainer = new TimeMachineContainer();

            //AutoCreateManagers;
            var svcs = _serviceContainer.GetAllServices();
            foreach (var service in svcs) {
                //继承于ITimeMachine的接口需要实现RollbackTo和Backup，进行数据的回滚和备份
                _timeMachineContainer.RegisterTimeMachine(service as ITimeMachine);
                if (service is BaseService baseService) {
                    //把BaseService类型放入管理器中
                    _mgrContainer.RegisterManager(baseService);
                }
            }

            _serviceContainer.RegisterService(_timeMachineContainer);
            _serviceContainer.RegisterService(_registerService);
        }


        public void DoStart(){
            
            //所有的service都保存在一个局部变量上，每个service都能直接拿到一些共有service对象引用
            foreach (var mgr in _mgrContainer.AllMgrs) {
                mgr.InitReference(_serviceContainer, _mgrContainer);
            }
            
            //bind events 通过反射绑定事件 OnEvent_XXX ==》 XXX是事件名
            foreach (var mgr in _mgrContainer.AllMgrs) {
                _registerService.RegisterEvent<EEvent, GlobalEventHandler>("OnEvent_", "OnEvent_".Length,
                    EventHelper.AddListener, mgr);
            }
            //调用所有service的DoAwake和DoStart
            // NetworkService GameConfigService 有重载DoAwake，否则都调用到BaseService的方法
            //BaseGameServicesContainer里的一些共有服务，以及子类的服务
            foreach (var mgr in _mgrContainer.AllMgrs) {
                mgr.DoAwake(_serviceContainer);
            }

            _DoAwake(_serviceContainer);
            //SimulatorService / NetworkService 有重载DoStart 否则都调用到BaseService的方法
            foreach (var mgr in _mgrContainer.AllMgrs) {
                mgr.DoStart();
            }

            _DoStart();
        }

        public void _DoAwake(IServiceContainer serviceContainer){
            _simulatorService = serviceContainer.GetService<ISimulatorService>() as SimulatorService;
            _networkService = serviceContainer.GetService<INetworkService>() as NetworkService;
            _constStateService = serviceContainer.GetService<IConstStateService>();
            _constStateService = serviceContainer.GetService<IConstStateService>();

            if (IsVideoMode) {
                //回放模式
                _constStateService.SnapshotFrameInterval = 20;
                //OpenRecordFile(RecordPath);
            }
        }

        public void _DoStart(){
            //_debugService.Trace("Before StartGame _IdCounter" + BaseEntity.IdCounter);
            //if (!IsReplay && !IsClientMode) {
            //    netClient = new NetClient();
            //    netClient.Start();
            //    netClient.Send(new Msg_JoinRoom() {name = Application.dataPath});
            //}
            //else {
            //    StartGame(0, playerServerInfos, localPlayerId);
            //}


            if (IsVideoMode) {
                //回放模式
                //发送BorderVideoFrame事件 会调用到OnEvent_BorderVideoFrame方法
                EventHelper.Trigger(EEvent.BorderVideoFrame, FramesInfo);
                //发送OnGameCreate事件，调用OnEvent_OnGameCreate方法
                EventHelper.Trigger(EEvent.OnGameCreate, GameStartInfo);
            }
            else if (IsClientMode) {
                //客户端模式
                GameStartInfo = _serviceContainer.GetService<IGameConfigService>().ClientModeInfo;
                EventHelper.Trigger(EEvent.OnGameCreate, GameStartInfo);
                EventHelper.Trigger(EEvent.LevelLoadDone, GameStartInfo);
            }
        }

        public void DoUpdate(float fDeltaTime){
            //处理网络消息的回调
            _syncContext.Update();
            Utils.UpdateServices();
            var deltaTime = fDeltaTime.ToLFloat();
            
            //_networkService 处理加载进度
            _networkService.DoUpdate(deltaTime);
            if (IsVideoMode && IsRunVideo && CurTick < MaxRunTick) {
                //回放模式
                _simulatorService.RunVideo();
                return;
            }

            if (IsVideoMode && !IsRunVideo) {
                //直接跳帧
                _simulatorService.JumpTo(JumpToTick);
            }

            _simulatorService.DoUpdate(fDeltaTime);
        }

        public void DoDestroy(){
            if (Instance == null) return;
            foreach (var mgr in _mgrContainer.AllMgrs) {
                mgr.DoDestroy();
            }

            Instance = null;
        }

        public void OnApplicationQuit(){
            DoDestroy();
        }
    }
}