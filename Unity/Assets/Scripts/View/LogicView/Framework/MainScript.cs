using Lockstep.Game;
using Lockstep.Math;
using Lockstep.Game;
using UnityEngine;


//Unity 启动器
public class MainScript : MonoBehaviour {
    
    //真正的游戏实例
    public Launcher launcher = new Launcher();
    public int MaxEnemyCount = 10;
    
    //客户端模式
    public bool IsClientMode = false;
    
    //回放模式？
    public bool IsRunVideo;
    public bool IsVideoMode = false;
    
    //回放模式二进制文件存储路径
    public string RecordFilePath;
    public bool HasInit = false;

    private ServiceContainer _serviceContainer;

    private void Awake(){
        //网络通信ping值计算 
        gameObject.AddComponent<PingMono>();
        //输入脚本 
        gameObject.AddComponent<InputMono>();
        
        //serviceContainer 抽象工厂利用桥接模式实现多平台代码
        _serviceContainer = new UnityServiceContainer();
        _serviceContainer.GetService<IConstStateService>().GameName = "ARPGDemo";
        _serviceContainer.GetService<IConstStateService>().IsClientMode = IsClientMode;
        _serviceContainer.GetService<IConstStateService>().IsVideoMode = IsVideoMode;
        _serviceContainer.GetService<IGameStateService>().MaxEnemyCount = MaxEnemyCount;
        Lockstep.Logging.Logger.OnMessage += UnityLogHandler.OnLog;
        Screen.SetResolution(1024, 768, false);
        //一系列服务的管理
        launcher.DoAwake(_serviceContainer);
    }


    private void Start(){
        var stateService = GetService<IConstStateService>();
        string path = Application.dataPath;
#if UNITY_EDITOR
        path = Application.dataPath + "/../../../";
#elif UNITY_STANDALONE_OSX
        path = Application.dataPath + "/../../../../../";
#elif UNITY_STANDALONE_WIN
        path = Application.dataPath + "/../../../";
#endif
        Debug.Log(path);
        stateService.RelPath = path;
        
        //所有服务的doAwake和DoStart 事件反射注册。。
        launcher.DoStart();
        HasInit = true;
    }

    private void Update(){
        _serviceContainer.GetService<IConstStateService>().IsRunVideo = IsVideoMode;
        launcher.DoUpdate(Time.deltaTime);
    }

    private void OnDestroy(){
        launcher.DoDestroy();
    }

    private void OnApplicationQuit(){
        launcher.OnApplicationQuit();
    }

    public T GetService<T>() where T : IService{
        return _serviceContainer.GetService<T>();
    }
}