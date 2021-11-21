using Lockstep.Game;



public class UnityServiceContainer : BaseGameServicesContainer {
    public UnityServiceContainer():base(){
        //Unity平台下使用
        RegisterService(new UnityGameViewService());
    }
}