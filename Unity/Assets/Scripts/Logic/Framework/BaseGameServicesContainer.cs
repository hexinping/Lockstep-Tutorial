using Lockstep.Game;
using Lockstep.Game;

public class BaseGameServicesContainer : ServiceContainer {
    public BaseGameServicesContainer(){
        //注册共有服务
        RegisterService(new RandomService());
        RegisterService(new CommonStateService());
        RegisterService(new ConstStateService());
        RegisterService(new SimulatorService());
        RegisterService(new NetworkService());
        RegisterService(new IdService());
        RegisterService(new GameResourceService());
        
        RegisterService(new GameStateService());
        RegisterService(new GameConfigService());
        RegisterService(new GameInputService());
    }
}