using System;
using System.Threading;
using Lockstep.Logging;
using Lockstep.Network;
using Lockstep.Util;

namespace Lockstep.FakeServer{
    public class ServerLauncher {
        private static Server server;

        public static void Main(){
            //let async functions call in this thread  
            
            //网络消息最后会在主线程处理  by add
            OneThreadSynchronizationContext contex = new OneThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(contex);
            
            Debug.Log("Main start");
            Utils.StartServices();
            try {
                DoAwake();
                while (true) {
                    try {
                        Thread.Sleep(3);
                        //处理网络消息的处理，最后会在主线程执行，放到一个队列里，执行派发  by add
                        contex.Update();
                        //服务器的正常更新，每30毫秒更新一次，检测帧输入，进行派发到客户端  by add
                        server.Update();
                    }
                    catch (ThreadAbortException e) {
                        return;
                    }
                    catch (Exception e) {
                        Log.Error(e.ToString());
                    }
                }
            }
            catch (ThreadAbortException e) {
                return;
            }
            catch (Exception e) {
                Log.Error(e.ToString());
            }
        }

        static void DoAwake(){
            //创建Server对象
            server = new Server();
            server.Start();
        }
    }
}