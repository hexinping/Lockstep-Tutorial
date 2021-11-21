using System.Collections.Generic;
using Lockstep.Math;

namespace Lockstep.Game {
    public partial class IdService : IIdService,  ITimeMachine {
        public int CurTick { get; set; }

        private int Id;

        public int GenId(){
            return Id++;
        }
        //简单的数据备份用一个Dictionary就能实现
        Dictionary<int, int> _tick2Id = new Dictionary<int, int>();

        public void RollbackTo(int tick){
            //简单模式还原
            Id = _tick2Id[tick];
        }

        public void Backup(int tick){
            _tick2Id[tick] = Id;
        }

        public void Clean(int maxVerifiedTick){ }

    }
}