using Lockstep.Collision2D;
using Lockstep.Logic;
using Lockstep.Math;
using UnityEngine;
using Debug = Lockstep.Logging.Debug;

namespace LockstepTutorial {

    public class InputMono : UnityEngine.MonoBehaviour {
        private static bool IsReplay => GameManager.Instance.IsReplay;
        [HideInInspector] public int floorMask;
        public float camRayLength = 100;

        public bool hasHitFloor;
        public LVector2 mousePos;
        public LVector2 inputUV;
        public bool isInputFire;
        public int skillId;
        public bool isSpeedUp;

        void Start(){
            floorMask = LayerMask.GetMask("Floor");
        }

        public void Update(){
            if (!IsReplay) {
                //非回放模式业务逻辑(正常逻辑) by add
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                
                //转成定点数UV by add
                inputUV = new LVector2(h.ToLFloat(), v.ToLFloat());

                isInputFire = Input.GetButton("Fire1"); //鼠标左键
                hasHitFloor = Input.GetMouseButtonDown(1); //鼠标右键
                if (hasHitFloor) {
                    Ray camRay = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit floorHit;
                    if (Physics.Raycast(camRay, out floorHit, camRayLength, floorMask)) {
                        //世界空间坐标 floorHit.point  
                        mousePos = floorHit.point.ToLVector2XZ();
                    }
                }
                
                //技能按键 1~6
                skillId = -1;
                for (int i = 0; i < 6; i++) {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) {
                        skillId = i;
                    }
                }
                
                //空格键
                isSpeedUp = Input.GetKeyDown(KeyCode.Space);
                GameManager.CurGameInput =  new PlayerInput() {
                    mousePos = mousePos,
                    inputUV = inputUV,
                    isInputFire = isInputFire,
                    skillId = skillId,
                    isSpeedUp = isSpeedUp,
                };
                
            }
        }
    }
}