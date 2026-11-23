using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Spring
{
    // A human-height walkthrough for inspecting cover and passage widths in Play Mode.
    [RequireComponent(typeof(CharacterController))]
    public sealed class SpringWalkthrough : MonoBehaviour
    {
        public Transform eyes;
        float pitch, verticalSpeed;
        void Start() { Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false; }
        void OnDisable() { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; }
        void Update()
        {
            var mouse=Mouse.current; var keys=Keyboard.current;
            if(mouse!=null && Cursor.lockState==CursorLockMode.Locked){
                Vector2 d=mouse.delta.ReadValue();transform.Rotate(0,d.x*.12f,0);
                pitch=Mathf.Clamp(pitch-d.y*.12f,-75,75);eyes.localRotation=Quaternion.Euler(pitch,0,0);
            }
            if(keys==null)return;
            if(keys.escapeKey.wasPressedThisFrame){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            if(mouse!=null&&mouse.leftButton.wasPressedThisFrame){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
            var motion=new Vector2((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0));
            var body=GetComponent<CharacterController>();
            verticalSpeed=body.isGrounded?-2:verticalSpeed-18*Time.deltaTime;
            motion=Vector2.ClampMagnitude(motion,1)*(keys.leftShiftKey.isPressed?5.3f:3.3f);
            body.Move((transform.right*motion.x+transform.forward*motion.y+Vector3.up*verticalSpeed)*Time.deltaTime);
        }
    }
}
