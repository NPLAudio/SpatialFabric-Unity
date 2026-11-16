// Original keyboard/mouse walking controller. No template character assets. MIT.
using UnityEngine;
namespace NPLAudio.SpatialFabric.Example
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ShowcaseVisitor : MonoBehaviour
    {
        public Camera viewCamera;
        public ShowcaseRooms rooms;
        public bool automationMode;
        public float walkSpeed=4,mouseSensitivity=2;
        CharacterController body;
        float pitch,vertical;
        public CharacterController Body=>body!=null?body:body=GetComponent<CharacterController>();
        void Start(){body=GetComponent<CharacterController>();if(!Application.isBatchMode)CaptureMouse();}
        public void CaptureMouse(){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
        public void ReleaseMouse(){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        public void Teleport(Vector3 foot){bool enabled=Body.enabled;Body.enabled=false;transform.position=foot;Body.enabled=enabled;vertical=0;Physics.SyncTransforms();}
        void Update()
        {
            if(automationMode || Application.isBatchMode)return;
            if(Input.GetKeyDown(KeyCode.Escape))ReleaseMouse();
            if(Input.GetKeyDown(KeyCode.X))rooms.StopAll();
            if(Input.GetKeyDown(KeyCode.Q) && rooms.CurrentCue!=null)rooms.CurrentCue.Stop();
            if(Cursor.lockState!=CursorLockMode.Locked)return;
            transform.Rotate(0,Input.GetAxisRaw("Mouse X")*mouseSensitivity,0);
            pitch=Mathf.Clamp(pitch-Input.GetAxisRaw("Mouse Y")*mouseSensitivity,-80,80);viewCamera.transform.localRotation=Quaternion.Euler(pitch,0,0);
            Vector3 move=transform.right*Input.GetAxisRaw("Horizontal")+transform.forward*Input.GetAxisRaw("Vertical");if(move.sqrMagnitude>1)move.Normalize();
            if(Body.isGrounded && vertical<0)vertical=-2;
            if(Body.isGrounded && Input.GetKeyDown(KeyCode.Space))vertical=4;
            vertical+=Physics.gravity.y*Time.deltaTime;Body.Move((move*walkSpeed*(Input.GetKey(KeyCode.LeftShift)?1.6f:1)+Vector3.up*vertical)*Time.deltaTime);
            var cue=rooms.CurrentCue;
            if(cue==null)return;
            if(Input.GetKeyDown(KeyCode.E))cue.TryPlay();
            if(Input.GetKeyDown(KeyCode.L))cue.SetLoop(!cue.loop);if(Input.GetKeyDown(KeyCode.R))cue.autoReplay=!cue.autoReplay;
        }
        void OnDisable(){ReleaseMouse();}
    }
}
