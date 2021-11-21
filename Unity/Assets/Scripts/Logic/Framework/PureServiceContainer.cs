using Lockstep.Game;

//.NET平台下 PureGameViewService里的接口都是空实现
public class PureServiceContainer : BaseGameServicesContainer {
    public PureServiceContainer():base(){
        RegisterService(new PureGameViewService());
    }
}